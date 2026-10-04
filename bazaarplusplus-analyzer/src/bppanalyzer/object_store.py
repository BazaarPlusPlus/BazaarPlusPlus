"""Minimal object-store boundary with its Cloudflare R2 adapter."""

import hashlib
import re
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import PurePosixPath
from typing import Protocol

import boto3
import httpx
from botocore.config import Config as BotoConfig
from botocore.exceptions import BotoCoreError, ClientError

CLOUDFLARE_API = "https://api.cloudflare.com/client/v4"
_ACCOUNT_ID = re.compile(r"[0-9a-fA-F]{32}")
_TOKEN_ID = re.compile(r"[0-9a-f]{32}")
_BUCKET = re.compile(r"[a-z0-9][a-z0-9-]*[a-z0-9]")
# Verified token ids per process, keyed by the token's digest, never its value.
_verified_token_ids: dict[str, str] = {}


class ObjectStoreError(RuntimeError):
    """An object-store request failed or returned inconsistent metadata."""


def _verify_token(token: str, client: httpx.Client | None) -> str:
    def request(http: httpx.Client) -> httpx.Response:
        return http.get(
            f"{CLOUDFLARE_API}/user/tokens/verify",
            headers={"Authorization": f"Bearer {token}"},
            timeout=30.0,
        )

    try:
        if client is not None:
            response = request(client)
        else:
            with httpx.Client() as http:
                response = request(http)
    except httpx.HTTPError as error:
        raise ObjectStoreError("Cloudflare token verification failed") from error
    try:
        body = response.json()
    except ValueError:
        body = None
    if response.status_code != 200 or not isinstance(body, dict) or body.get("success") is not True:
        raise ObjectStoreError(
            f"Cloudflare rejected CLOUDFLARE_API_TOKEN: HTTP {response.status_code}"
        )
    result = body.get("result") or {}
    token_id = result.get("id")
    status = result.get("status")
    if not isinstance(token_id, str) or _TOKEN_ID.fullmatch(token_id) is None:
        raise ObjectStoreError("Cloudflare token verification returned no token id")
    if status != "active":
        raise ObjectStoreError(f"CLOUDFLARE_API_TOKEN is {status}, not active")
    return token_id


def derive_s3_credentials(api_token: str, *, client: httpx.Client | None = None) -> tuple[str, str]:
    """The S3 pair R2 accepts for an API token.

    The Access Key ID is the token's id, which only ``GET /user/tokens/verify``
    reveals, and the Secret Access Key is the SHA-256 of the token value. The
    verified id is reused for the rest of the process.
    """
    token = api_token.strip()
    if not token:
        raise ValueError("CLOUDFLARE_API_TOKEN is empty")
    secret_access_key = hashlib.sha256(token.encode("utf-8")).hexdigest()
    access_key_id = _verified_token_ids.get(secret_access_key)
    if access_key_id is None:
        access_key_id = _verify_token(token, client)
        _verified_token_ids[secret_access_key] = access_key_id
    return access_key_id, secret_access_key


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
    """Cloudflare R2 adapter using its S3-compatible boto3 endpoint.

    The S3 pair is derived from ``api_token`` with one verify call before any
    bucket request; an empty token, a malformed account id or bucket name, and
    a rejected, malformed or inactive token all fail here.
    """

    def __init__(
        self,
        *,
        account_id: str,
        bucket: str,
        api_token: str,
        http_client: httpx.Client | None = None,
    ) -> None:
        if not all((account_id, bucket, api_token)):
            raise ValueError("Complete R2 configuration is required")
        if _ACCOUNT_ID.fullmatch(account_id) is None:
            raise ValueError(
                "CLOUDFLARE_ACCOUNT_ID must be the 32-character hexadecimal account id"
            )
        if _BUCKET.fullmatch(bucket) is None:
            raise ValueError("Invalid R2 bucket")
        access_key_id, secret_access_key = derive_s3_credentials(api_token, client=http_client)
        self.bucket = bucket
        self._client = boto3.client(
            "s3",
            endpoint_url=f"https://{account_id.lower()}.r2.cloudflarestorage.com",
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
