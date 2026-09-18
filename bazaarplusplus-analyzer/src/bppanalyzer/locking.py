"""Single-machine mkdir lock with stale takeover and zombie fencing."""

import json
import os
import shutil
import socket
import threading
import time
from datetime import UTC, datetime
from pathlib import Path
from typing import Callable, Self


class LockError(RuntimeError):
    """Base run-lock failure."""


class LockHeld(LockError):
    """Another fresh run owns the local lock."""


class LockOwnershipLost(LockError):
    """This process was fenced by a stale-lock takeover."""


class MaximumRunTimeExceeded(LockOwnershipLost):
    """A run exceeded its configured ownership lifetime."""


class DirectoryLock:
    """Own ``.lock`` only while its heartbeat still names this run."""

    def __init__(
        self,
        data_root: str | Path,
        run_id: str,
        *,
        heartbeat_interval: float = 30,
        stale_after: float = 300,
        max_run_seconds: float = 21600,
        wall_clock: Callable[[], float] = time.time,
        monotonic: Callable[[], float] = time.monotonic,
    ) -> None:
        if not run_id or any(character in run_id for character in "/\\"):
            raise ValueError("Run lock identity is invalid")
        if heartbeat_interval <= 0 or stale_after <= 0 or max_run_seconds <= 0:
            raise ValueError("Run lock time limits must be positive")
        self._root = Path(data_root)
        self._path = self._root / ".lock"
        self._heartbeat = self._path / "heartbeat"
        self.run_id = run_id
        self._interval = heartbeat_interval
        self._stale_after = stale_after
        self._max_seconds = max_run_seconds
        self._wall_clock = wall_clock
        self._monotonic = monotonic
        self._started_monotonic: float | None = None
        self._stop = threading.Event()
        self._lost = threading.Event()
        self._lost_reason = "Run lock ownership was lost"
        self._thread: threading.Thread | None = None
        self.stale_run_id: str | None = None

    def acquire(self) -> Self:
        self._root.mkdir(parents=True, exist_ok=True)
        stale_path = self._root / f".lock.stale-{self.run_id}"
        takeover_guard = self._root / ".lock.takeover"
        while True:
            try:
                self._path.mkdir()
            except FileExistsError:
                age = self._heartbeat_age()
                if age <= self._stale_after:
                    raise LockHeld("Another bpp run holds the lock") from None
                try:
                    takeover_guard.mkdir()
                except FileExistsError:
                    self._recover_takeover_guard(takeover_guard)
                    time.sleep(0.001)
                    continue
                try:
                    # Another stale contender may have completed between our
                    # first observation and winning the takeover guard.
                    if self._heartbeat_age() <= self._stale_after:
                        raise LockHeld("Another bpp run holds the lock")
                    self.stale_run_id = self._read_run_id(self._heartbeat)
                    try:
                        os.rename(self._path, stale_path)
                    except FileNotFoundError:
                        continue
                    except FileExistsError:
                        shutil.rmtree(stale_path, ignore_errors=True)
                        continue
                finally:
                    try:
                        takeover_guard.rmdir()
                    except OSError:
                        pass
                continue
            break

        started = datetime.fromtimestamp(self._wall_clock(), tz=UTC)
        body = {
            "run_id": self.run_id,
            "pid": os.getpid(),
            "started_at": started.isoformat().replace("+00:00", "Z"),
            "hostname": socket.gethostname(),
        }
        try:
            with self._heartbeat.open("x", encoding="utf-8") as stream:
                json.dump(body, stream, sort_keys=True, separators=(",", ":"))
                stream.write("\n")
                stream.flush()
                os.fsync(stream.fileno())
        except BaseException:
            try:
                self._path.rmdir()
            except OSError:
                pass
            raise
        if stale_path.exists():
            shutil.rmtree(stale_path, ignore_errors=True)
        self._started_monotonic = self._monotonic()
        self._thread = threading.Thread(
            target=self._heartbeat_loop,
            name=f"bpp-heartbeat-{self.run_id}",
            daemon=True,
        )
        self._thread.start()
        return self

    def assert_owned(self) -> None:
        if self._lost.is_set():
            if self._lost_reason == "Maximum run time exceeded":
                raise MaximumRunTimeExceeded(self._lost_reason)
            raise LockOwnershipLost(self._lost_reason)
        if self._started_monotonic is None:
            raise LockOwnershipLost("Run lock was not acquired")
        if self._monotonic() - self._started_monotonic > self._max_seconds:
            self._mark_lost("Maximum run time exceeded")
            raise MaximumRunTimeExceeded("Maximum run time exceeded")
        owner = self._read_run_id(self._heartbeat)
        if owner != self.run_id:
            self._mark_lost("Run lock ownership was replaced")
            raise LockOwnershipLost("Run lock ownership was replaced")

    def assert_current_owner(self) -> None:
        """Fence writes without extending an exceeded run deadline."""
        if self._lost.is_set() and self._lost_reason != "Maximum run time exceeded":
            raise LockOwnershipLost(self._lost_reason)
        if self._started_monotonic is None:
            raise LockOwnershipLost("Run lock was not acquired")
        owner = self._read_run_id(self._heartbeat)
        if owner != self.run_id:
            self._mark_lost("Run lock ownership was replaced")
            raise LockOwnershipLost("Run lock ownership was replaced")

    def touch(self) -> None:
        self.assert_owned()
        try:
            os.utime(self._heartbeat, None)
        except FileNotFoundError:
            self._mark_lost("Run lock heartbeat disappeared")
            raise LockOwnershipLost("Run lock heartbeat disappeared") from None

    def release(self) -> None:
        self._stop.set()
        if self._thread is not None and self._thread is not threading.current_thread():
            self._thread.join(timeout=min(self._interval + 0.1, 1.0))
        self.assert_current_owner()
        try:
            self._heartbeat.unlink()
            self._path.rmdir()
        except FileNotFoundError:
            self._mark_lost("Run lock disappeared during release")
            raise LockOwnershipLost("Run lock disappeared during release") from None
        finally:
            self._started_monotonic = None

    def __enter__(self) -> Self:
        return self.acquire()

    def __exit__(
        self,
        _exception_type: object,
        exception: object,
        _traceback: object,
    ) -> None:
        try:
            self.release()
        except LockOwnershipLost:
            if exception is None:
                raise

    def _heartbeat_loop(self) -> None:
        while not self._stop.wait(self._interval):
            try:
                self.touch()
            except LockOwnershipLost:
                return

    def _mark_lost(self, reason: str) -> None:
        self._lost_reason = reason
        self._lost.set()
        self._stop.set()

    def _heartbeat_age(self) -> float:
        try:
            modified = self._heartbeat.stat().st_mtime
        except FileNotFoundError:
            try:
                modified = self._path.stat().st_mtime
            except FileNotFoundError:
                return float("inf")
        return max(0.0, self._wall_clock() - modified)

    def _recover_takeover_guard(self, path: Path) -> None:
        try:
            age = max(0.0, self._wall_clock() - path.stat().st_mtime)
        except FileNotFoundError:
            return
        if age > self._stale_after:
            try:
                path.rmdir()
            except OSError:
                pass

    @staticmethod
    def _read_run_id(path: Path) -> str | None:
        try:
            value = json.loads(path.read_bytes())
        except FileNotFoundError, OSError, json.JSONDecodeError, UnicodeDecodeError:
            return None
        run_id = value.get("run_id") if isinstance(value, dict) else None
        return run_id if isinstance(run_id, str) else None
