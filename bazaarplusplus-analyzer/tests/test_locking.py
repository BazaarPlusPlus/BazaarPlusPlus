import contextlib
import json
import os
import threading
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import pytest

from bppanalyzer.locking import DirectoryLock, LockHeld, LockOwnershipLost


def test_live_lock_rejects_a_concurrent_holder_within_one_second_without_writes(
    tmp_path: Path,
) -> None:
    first = DirectoryLock(tmp_path, "run-one", heartbeat_interval=60)
    first.acquire()
    before = {
        path.relative_to(tmp_path): (path.stat().st_mtime_ns, path.stat().st_size)
        for path in tmp_path.rglob("*")
    }
    started = time.monotonic()

    with pytest.raises(LockHeld):
        DirectoryLock(tmp_path, "run-two", heartbeat_interval=60).acquire()

    assert time.monotonic() - started < 1
    after = {
        path.relative_to(tmp_path): (path.stat().st_mtime_ns, path.stat().st_size)
        for path in tmp_path.rglob("*")
    }
    assert after == before
    first.release()


def test_stale_lock_race_has_exactly_one_winner(tmp_path: Path) -> None:
    lock_dir = tmp_path / ".lock"
    lock_dir.mkdir()
    heartbeat = lock_dir / "heartbeat"
    heartbeat.write_text(
        json.dumps(
            {
                "run_id": "stale-run",
                "pid": 1,
                "started_at": "2026-08-01T00:00:00Z",
                "hostname": "stale-host",
            }
        )
    )
    old = time.time() - 600
    os.utime(heartbeat, (old, old))
    barrier = threading.Barrier(2)

    def contend(run_id: str):
        candidate = DirectoryLock(tmp_path, run_id, heartbeat_interval=60, stale_after=300)
        barrier.wait()
        try:
            candidate.acquire()
        except LockHeld:
            return None
        return candidate

    with ThreadPoolExecutor(max_workers=2) as executor:
        results = list(executor.map(contend, ("winner-a", "winner-b")))

    winners = [value for value in results if value is not None]
    assert len(winners) == 1
    winners[0].release()


def test_resumed_stale_holder_neither_writes_nor_removes_the_new_lock(
    tmp_path: Path,
) -> None:
    old = DirectoryLock(tmp_path, "old-run", heartbeat_interval=60)
    old.acquire()
    heartbeat = tmp_path / ".lock/heartbeat"
    stale = time.time() - 600
    os.utime(heartbeat, (stale, stale))
    current = DirectoryLock(tmp_path, "current-run", heartbeat_interval=60, stale_after=300)
    current.acquire()

    with pytest.raises(LockOwnershipLost):
        old.touch()
    with pytest.raises(LockOwnershipLost):
        old.release()

    assert json.loads(heartbeat.read_text())["run_id"] == "current-run"
    current.release()


def test_missing_lock_observation_does_not_steal_a_new_claim(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    lock_dir = tmp_path / ".lock"
    lock_dir.mkdir()
    heartbeat = lock_dir / "heartbeat"
    heartbeat.write_text(json.dumps({"run_id": "stale-run"}))
    old = time.time() - 600
    os.utime(heartbeat, (old, old))
    contender = DirectoryLock(tmp_path, "contender", heartbeat_interval=60)
    fresh = DirectoryLock(tmp_path, "fresh", heartbeat_interval=60)
    fresh_created = threading.Event()
    resume_fresh = threading.Event()
    fresh_finished = threading.Event()
    original_open = Path.open
    original_rename = os.rename
    original_age = contender._heartbeat_age
    observations = 0
    future = None

    def acquire_fresh():
        try:
            return fresh.acquire()
        finally:
            fresh_finished.set()

    def pause_before_heartbeat(path, *args, **kwargs):
        if path == heartbeat and threading.current_thread().name.startswith("fresh-lock"):
            fresh_created.set()
            assert resume_fresh.wait(5)
        return original_open(path, *args, **kwargs)

    def finish_fresh_after_rename(source, destination):
        original_rename(source, destination)
        if source == lock_dir and destination == tmp_path / ".lock.stale-contender":
            resume_fresh.set()
            assert fresh_finished.wait(5)

    with ThreadPoolExecutor(max_workers=1, thread_name_prefix="fresh-lock") as executor:

        def observe_missing_then_create():
            nonlocal observations, future
            observations += 1
            if observations == 2:
                # A previous stale contender has moved the old claim away.
                original_rename(lock_dir, tmp_path / ".retired")
                age = original_age()
                future = executor.submit(acquire_fresh)
                assert fresh_created.wait(5)
                return age
            return original_age()

        monkeypatch.setattr(Path, "open", pause_before_heartbeat)
        monkeypatch.setattr(os, "rename", finish_fresh_after_rename)
        monkeypatch.setattr(contender, "_heartbeat_age", observe_missing_then_create)
        rejected = False
        try:
            try:
                contender.acquire()
            except LockHeld:
                rejected = True
            finally:
                resume_fresh.set()
            assert future is not None
            assert future.result(timeout=5) is fresh
            assert rejected
            fresh.assert_owned()
        finally:
            resume_fresh.set()
            for candidate in (contender, fresh):
                with contextlib.suppress(LockOwnershipLost):
                    candidate.release()
