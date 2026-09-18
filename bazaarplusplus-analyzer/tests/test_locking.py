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
