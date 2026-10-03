import hashlib
from datetime import UTC, datetime
from io import BytesIO

import pytest
from botocore.exceptions import BotoCoreError, ClientError

import bppanalyzer.object_store as object_store_module
from bppanalyzer.object_store import ObjectStoreError, R2ObjectStore


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


def test_r2_adapter_uses_exact_json_metadata_without_network(monkeypatch) -> None:
    now = datetime(2026, 8, 11, tzinfo=UTC)
    client = _FakeR2Client(now)
    monkeypatch.setattr(object_store_module.boto3, "client", lambda *_args, **_kwargs: client)
    store = R2ObjectStore(
        account_id="account",
        bucket="bucket",
        access_key_id="access",
        secret_access_key="secret",
    )

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
    store = R2ObjectStore(
        account_id="account",
        bucket="bucket",
        access_key_id="access",
        secret_access_key="secret",
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


def test_r2_configuration_requires_every_credential(monkeypatch) -> None:
    monkeypatch.setattr(
        object_store_module.boto3,
        "client",
        lambda *_args, **_kwargs: pytest.fail("invalid configuration must not create a client"),
    )
    with pytest.raises(ValueError, match="Complete R2"):
        R2ObjectStore(
            account_id="",
            bucket="bucket",
            access_key_id="access",
            secret_access_key="secret",
        )
