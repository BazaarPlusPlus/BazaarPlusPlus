"""Minimal object-store boundary with its Cloudflare R2 adapter."""

import hashlib
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import PurePosixPath
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
