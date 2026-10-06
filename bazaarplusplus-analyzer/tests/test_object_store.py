"""R2 adapter tests.

Credential derivation fails closed, before any bucket request, when:
  the token is empty                        -> ValueError "Complete R2 configuration"
  the account id is not 32 hex characters   -> ValueError "CLOUDFLARE_ACCOUNT_ID must be"
  the bucket name is not a valid R2 bucket  -> ValueError "Invalid R2 bucket"
  /user/tokens/verify is not 200 or success -> ObjectStoreError "Cloudflare rejected"
  the verify result has no 32-hex token id  -> ObjectStoreError "returned no token id"
  the token is not active                   -> ObjectStoreError "not active"
  the verify request itself fails           -> ObjectStoreError "verification failed"
A verified id is reused for the process; a failed verification is not.
"""

import hashlib
import uuid
from datetime import UTC, datetime
from io import BytesIO

import httpx
import pytest
from botocore.exceptions import BotoCoreError, ClientError

import bppanalyzer.object_store as object_store_module
from bppanalyzer.object_store import (
    CLOUDFLARE_API,
    ObjectStoreError,
    R2ObjectStore,
    derive_s3_credentials,
)

ACCOUNT = "a" * 32
TOKEN_ID = "b" * 32


class _FakeR2Client:
    def __init__(self, now: datetime) -> None:
        self.now = now
        self.body = b'{"ok":true}\n'
        self.requests: list[tuple[str, dict]] = []
        self.failure: BaseException | None = None
        self.metadata: dict[str, object] = {
            "ContentLength": len(self.body),
            "CacheControl": "public,max-age=60,must-revalidate",
            "ContentType": "application/json",
            "LastModified": now.replace(tzinfo=None),
            "Metadata": {"sha256": hashlib.sha256(self.body).hexdigest()},
        }

    def head_object(self, **kwargs):
        self.requests.append(("head", kwargs))
        if self.failure is not None:
            raise self.failure
        return dict(self.metadata)

    def get_object(self, **kwargs):
        self.requests.append(("get", kwargs))
        if self.failure is not None:
            raise self.failure
        return {**self.metadata, "Body": BytesIO(self.body)}

    def put_object(self, **kwargs):
        self.requests.append(("put", kwargs))
        if self.failure is not None:
            raise self.failure


def _client_error(code: str) -> ClientError:
    return ClientError({"Error": {"Code": code}}, "fixture")


class _Verify:
    """A Cloudflare API stand-in on a real httpx client; records each call."""

    def __init__(self, *responses: httpx.Response) -> None:
        self.responses = list(responses)
        self.requests: list[httpx.Request] = []

    def handler(self, request: httpx.Request) -> httpx.Response:
        self.requests.append(request)
        if not self.responses:
            raise httpx.ConnectError("no response scripted")
        return self.responses.pop(0)

    def client(self) -> httpx.Client:
        return httpx.Client(transport=httpx.MockTransport(self.handler))


def _verified(token_id: str = TOKEN_ID, status: str = "active", *, code: int = 200, success=True):
    return httpx.Response(
        code, json={"success": success, "errors": [], "result": {"id": token_id, "status": status}}
    )


def _token() -> str:
    return f"token-{uuid.uuid4()}"


def test_s3_pair_is_the_verified_token_id_and_the_sha256_of_the_token() -> None:
    token = _token()
    verify = _Verify(_verified())
    with verify.client() as client:
        access_key_id, secret = derive_s3_credentials(f" {token} ", client=client)
        again = derive_s3_credentials(token, client=client)

    assert (access_key_id, secret) == (TOKEN_ID, hashlib.sha256(token.encode()).hexdigest())
    assert again == (access_key_id, secret)
    assert len(verify.requests) == 1
    assert str(verify.requests[0].url) == f"{CLOUDFLARE_API}/user/tokens/verify"
    assert verify.requests[0].headers["authorization"] == f"Bearer {token}"


def test_r2_adapter_derives_its_pair_once_and_uses_exact_json_metadata(monkeypatch) -> None:
    now = datetime(2026, 8, 11, tzinfo=UTC)
    client = _FakeR2Client(now)
    created: list[dict] = []

    def fake_client(*_args, **kwargs):
        created.append(kwargs)
        return client

    monkeypatch.setattr(object_store_module.boto3, "client", fake_client)
    token = _token()
    verify = _Verify(_verified())
    with verify.client() as http:
        store = R2ObjectStore(
            account_id=ACCOUNT.upper(), bucket="bucket", api_token=token, http_client=http
        )
        R2ObjectStore(account_id=ACCOUNT, bucket="bucket", api_token=token, http_client=http)
    assert len(verify.requests) == 1
    assert created[0]["aws_access_key_id"] == TOKEN_ID
    assert created[0]["aws_secret_access_key"] == hashlib.sha256(token.encode()).hexdigest()
    assert created[0]["endpoint_url"] == f"https://{ACCOUNT}.r2.cloudflarestorage.com"

    stat = store.stat("analyzer-v5/heroes/latest.json")
    observed = store.get("analyzer-v5/heroes/latest.json")
    store.put(
        "analyzer-v5/builds/latest.json",
        client.body,
        cache_control="public,max-age=60,must-revalidate",
        content_type="application/json",
    )

    assert stat is not None and stat.last_modified == now
    assert observed is not None and observed.body == client.body
    put = client.requests[-1][1]
    assert put["ContentType"] == "application/json"
    assert put["Metadata"] == {"sha256": hashlib.sha256(client.body).hexdigest()}


def test_r2_adapter_maps_not_found_errors_and_rejects_bad_metadata(monkeypatch) -> None:
    client = _FakeR2Client(datetime(2026, 8, 11, tzinfo=UTC))
    monkeypatch.setattr(object_store_module.boto3, "client", lambda *_args, **_kwargs: client)
    with _Verify(_verified()).client() as http:
        store = R2ObjectStore(
            account_id=ACCOUNT, bucket="bucket", api_token=_token(), http_client=http
        )
    client.failure = _client_error("NoSuchKey")
    assert store.stat("missing.json") is None
    assert store.get("missing.json") is None

    client.failure = _client_error("AccessDenied")
    with pytest.raises(ObjectStoreError, match="stat failed"):
        store.stat("denied.json")
    with pytest.raises(ObjectStoreError, match="get failed"):
        store.get("denied.json")
    with pytest.raises(ObjectStoreError, match="put failed"):
        store.put("denied.json", b"{}", cache_control="cache")

    client.failure = BotoCoreError()
    with pytest.raises(ObjectStoreError, match="stat failed"):
        store.stat("failed.json")
    client.failure = None
    client.metadata.pop("ContentType")
    with pytest.raises(ObjectStoreError, match="metadata is incomplete"):
        store.stat("bad.json")


def test_r2_configuration_is_validated_before_any_request(monkeypatch) -> None:
    monkeypatch.setattr(
        object_store_module.boto3,
        "client",
        lambda *_args, **_kwargs: pytest.fail("invalid configuration must not create a client"),
    )
    verify = _Verify()
    with verify.client() as http:
        for kwargs, message in (
            ({"account_id": "", "bucket": "bucket", "api_token": "t"}, "Complete R2"),
            ({"account_id": ACCOUNT, "bucket": "bucket", "api_token": ""}, "Complete R2"),
            (
                {"account_id": "account", "bucket": "bucket", "api_token": "t"},
                "CLOUDFLARE_ACCOUNT_ID",
            ),
            (
                {"account_id": ACCOUNT, "bucket": "Bad_Bucket", "api_token": "t"},
                "Invalid R2 bucket",
            ),
        ):
            with pytest.raises(ValueError, match=message):
                R2ObjectStore(http_client=http, **kwargs)
    assert verify.requests == []


@pytest.mark.parametrize(
    ("response", "message"),
    (
        (_verified(code=401, success=False), "Cloudflare rejected CLOUDFLARE_API_TOKEN: HTTP 401"),
        (httpx.Response(500, text="not json"), "HTTP 500"),
        (
            httpx.Response(200, json={"success": True, "result": {"status": "active"}}),
            "no token id",
        ),
        (_verified(status="expired"), "is expired, not active"),
        (None, "verification failed"),
    ),
)
def test_a_rejected_malformed_or_failed_verification_fails_and_is_retried(
    monkeypatch, response, message
) -> None:
    monkeypatch.setattr(
        object_store_module.boto3,
        "client",
        lambda *_args, **_kwargs: _FakeR2Client(datetime(2026, 8, 11, tzinfo=UTC)),
    )
    token = _token()
    verify = _Verify() if response is None else _Verify(response, _verified())
    with verify.client() as http:
        with pytest.raises(ObjectStoreError, match=message):
            R2ObjectStore(account_id=ACCOUNT, bucket="bucket", api_token=token, http_client=http)
        if response is None:
            verify.responses.append(_verified())
        R2ObjectStore(account_id=ACCOUNT, bucket="bucket", api_token=token, http_client=http)
    assert len(verify.requests) == 2
