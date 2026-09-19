"""Minimal object-store boundary with offline-local and Cloudflare R2 adapters."""

import hashlib
import json
import os
import tempfile
from collections.abc import Callable
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path, PurePosixPath
from typing import Protocol

import boto3
from botocore.config import Config as BotoConfig
from botocore.exceptions import BotoCoreError, ClientError


class ObjectStoreError(RuntimeError):
    """An object-store request failed or returned inconsistent metadata."""


@dataclass(frozen=True, slots=True)
class ObjectStat:
    key: str
    sha256: str
    bytes: int
    cache_control: str
    content_type: str
    last_modified: datetime


@dataclass(frozen=True, slots=True)
class StoredObject:
    body: bytes
    stat: ObjectStat


@dataclass(frozen=True, slots=True)
class StoreRequest:
    operation: str
    key: str


class ObjectStore(Protocol):
    def stat(self, key: str) -> ObjectStat | None: ...

    def get(self, key: str) -> StoredObject | None: ...

    def put(
        self,
        key: str,
        body: bytes,
        *,
        cache_control: str,
        content_type: str = "application/octet-stream",
    ) -> None: ...


class LocalObjectStore:
    """File-backed object store used for every offline test and acceptance run."""

    def __init__(
        self,
        root: str | Path,
        *,
        clock: Callable[[], datetime] = lambda: datetime.now(UTC),
    ) -> None:
        self.root = Path(root)
        self._objects = self.root / "objects"
        self._metadata = self.root / "metadata"
        self._clock = clock
        self.requests: list[StoreRequest] = []

    def clear_requests(self) -> None:
        self.requests.clear()

    def stat(self, key: str) -> ObjectStat | None:
        self.requests.append(StoreRequest("stat", key))
        paths = self._paths(key)
        return self._read_stat(key, *paths)

    def get(self, key: str) -> StoredObject | None:
        self.requests.append(StoreRequest("get", key))
        object_path, metadata_path = self._paths(key)
        stat = self._read_stat(key, object_path, metadata_path)
        if stat is None:
            return None
        try:
            body = object_path.read_bytes()
        except OSError as error:
            raise ObjectStoreError(f"Local object is unreadable: {key}") from error
        return StoredObject(body, stat)

    def put(
        self,
        key: str,
        body: bytes,
        *,
        cache_control: str,
        content_type: str = "application/octet-stream",
    ) -> None:
        self.requests.append(StoreRequest("put", key))
        if not isinstance(body, bytes):
            raise TypeError("Object body must be bytes")
        if not cache_control:
            raise ValueError("Object Cache-Control is required")
        if not content_type:
            raise ValueError("Object Content-Type is required")
        object_path, metadata_path = self._paths(key)
        now = self._clock()
        if now.tzinfo is None or now.utcoffset() is None:
            raise ValueError("Object-store clock must be timezone-aware")
        metadata = {
            "sha256": hashlib.sha256(body).hexdigest(),
            "bytes": len(body),
            "cache_control": cache_control,
            "content_type": content_type,
            "last_modified": now.astimezone(UTC).isoformat().replace("+00:00", "Z"),
        }
        _atomic_write(object_path, body)
        _atomic_write(metadata_path, _canonical_json(metadata))

    def _paths(self, key: str) -> tuple[Path, Path]:
        relative = _safe_key(key)
        return self._objects / relative, self._metadata / f"{relative.as_posix()}.json"

    @staticmethod
    def _read_stat(key: str, object_path: Path, metadata_path: Path) -> ObjectStat | None:
        object_exists = object_path.is_file()
        metadata_exists = metadata_path.is_file()
        if not object_exists and not metadata_exists:
            return None
        if not object_exists or not metadata_exists:
            raise ObjectStoreError(f"Local object metadata is incomplete: {key}")
        try:
            body = object_path.read_bytes()
            metadata = json.loads(metadata_path.read_bytes())
            modified = datetime.fromisoformat(metadata["last_modified"].replace("Z", "+00:00"))
        except (OSError, KeyError, TypeError, ValueError, json.JSONDecodeError) as error:
            raise ObjectStoreError(f"Local object metadata is unreadable: {key}") from error
        digest = hashlib.sha256(body).hexdigest()
        if (
            not isinstance(metadata, dict)
            or metadata.get("sha256") != digest
            or metadata.get("bytes") != len(body)
            or not isinstance(metadata.get("cache_control"), str)
            or not isinstance(metadata.get("content_type"), str)
            or modified.tzinfo is None
        ):
            raise ObjectStoreError(f"Local object metadata differs from bytes: {key}")
        return ObjectStat(
            key=key,
            sha256=digest,
            bytes=len(body),
            cache_control=metadata["cache_control"],
            content_type=metadata["content_type"],
            last_modified=modified.astimezone(UTC),
        )


class R2ObjectStore:
    """Cloudflare R2 adapter using its S3-compatible boto3 endpoint."""

    def __init__(
        self,
        *,
        account_id: str,
        bucket: str,
        access_key_id: str,
        secret_access_key: str,
    ) -> None:
        if not all((account_id, bucket, access_key_id, secret_access_key)):
            raise ValueError("Complete R2 configuration is required")
        self.bucket = bucket
        self._client = boto3.client(
            "s3",
            endpoint_url=f"https://{account_id}.r2.cloudflarestorage.com",
            aws_access_key_id=access_key_id,
            aws_secret_access_key=secret_access_key,
            region_name="auto",
            config=BotoConfig(signature_version="s3v4"),
        )

    def stat(self, key: str) -> ObjectStat | None:
        _safe_key(key)
        try:
            response = self._client.head_object(Bucket=self.bucket, Key=key)
        except ClientError as error:
            if _is_not_found(error):
                return None
            raise ObjectStoreError(f"R2 stat failed: {key}") from error
        except BotoCoreError as error:
            raise ObjectStoreError(f"R2 stat failed: {key}") from error
        return _r2_stat(key, response)

    def get(self, key: str) -> StoredObject | None:
        _safe_key(key)
        try:
            response = self._client.get_object(Bucket=self.bucket, Key=key)
            body = response["Body"].read()
        except ClientError as error:
            if _is_not_found(error):
                return None
            raise ObjectStoreError(f"R2 get failed: {key}") from error
        except (BotoCoreError, KeyError, OSError) as error:
            raise ObjectStoreError(f"R2 get failed: {key}") from error
        stat = _r2_stat(key, response, body=body)
        return StoredObject(body, stat)

    def put(
        self,
        key: str,
        body: bytes,
        *,
        cache_control: str,
        content_type: str = "application/octet-stream",
    ) -> None:
        _safe_key(key)
        digest = hashlib.sha256(body).hexdigest()
        try:
            self._client.put_object(
                Bucket=self.bucket,
                Key=key,
                Body=body,
                CacheControl=cache_control,
                ContentType=content_type,
                Metadata={"sha256": digest},
            )
        except (BotoCoreError, ClientError) as error:
            raise ObjectStoreError(f"R2 put failed: {key}") from error


def _r2_stat(key: str, response: dict, *, body: bytes | None = None) -> ObjectStat:
    try:
        size = int(response["ContentLength"])
        cache_control = response["CacheControl"]
        content_type = response["ContentType"]
        modified = response["LastModified"]
        metadata = response.get("Metadata", {})
    except (KeyError, TypeError, ValueError) as error:
        raise ObjectStoreError(f"R2 metadata is incomplete: {key}") from error
    if (
        not isinstance(cache_control, str)
        or not isinstance(content_type, str)
        or not isinstance(modified, datetime)
    ):
        raise ObjectStoreError(f"R2 metadata is incomplete: {key}")
    digest = metadata.get("sha256")
    if body is not None:
        actual = hashlib.sha256(body).hexdigest()
        if size != len(body) or (digest is not None and digest != actual):
            raise ObjectStoreError(f"R2 object metadata differs from bytes: {key}")
        digest = actual
    if not isinstance(digest, str) or len(digest) != 64:
        raise ObjectStoreError(f"R2 object sha256 metadata is missing: {key}")
    if modified.tzinfo is None:
        modified = modified.replace(tzinfo=UTC)
    return ObjectStat(key, digest, size, cache_control, content_type, modified.astimezone(UTC))


def _is_not_found(error: ClientError) -> bool:
    code = str(error.response.get("Error", {}).get("Code", ""))
    return code in {"404", "NoSuchKey", "NotFound"}


def _safe_key(key: str) -> PurePosixPath:
    if not isinstance(key, str) or not key or "\\" in key:
        raise ValueError("Object key is invalid")
    value = PurePosixPath(key)
    if value.is_absolute() or any(part in {"", ".", ".."} for part in value.parts):
        raise ValueError("Object key is invalid")
    return value


def _atomic_write(path: Path, content: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, name = tempfile.mkstemp(prefix=f".{path.name}.tmp-", dir=path.parent)
    temporary = Path(name)
    try:
        with os.fdopen(descriptor, "wb") as stream:
            descriptor = -1
            stream.write(content)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
        _fsync_directory(path.parent)
    finally:
        if descriptor >= 0:
            os.close(descriptor)
        temporary.unlink(missing_ok=True)


def _canonical_json(value: object) -> bytes:
    return (
        json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False) + "\n"
    ).encode("utf-8")


def _fsync_directory(path: Path) -> None:
    descriptor = os.open(path, os.O_RDONLY)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)
