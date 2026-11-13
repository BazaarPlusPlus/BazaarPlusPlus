"""Read the owned contracts from a checkout or an installed distribution."""

import hashlib
from functools import cache
from pathlib import Path


def contract_bytes(name: str) -> bytes:
    package = Path(__file__).resolve().parent
    contracts = package / "contracts" / "v6"
    if not contracts.is_dir():
        contracts = package.parents[1] / "contracts" / "v6"
    return (contracts / name).read_bytes()


@cache
def code_sha256() -> str:
    """Digest every module of this package, so a snapshot is rebuilt after any code change."""
    package = Path(__file__).resolve().parent
    digest = hashlib.sha256()
    for path in sorted(package.rglob("*.py")):
        digest.update(path.relative_to(package).as_posix().encode())
        digest.update(b"\0")
        digest.update(hashlib.sha256(path.read_bytes()).digest())
    return digest.hexdigest()
