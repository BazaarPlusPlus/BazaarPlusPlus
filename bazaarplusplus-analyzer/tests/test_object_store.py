import hashlib
from datetime import UTC, datetime
from io import BytesIO
from pathlib import Path

import pytest
from botocore.exceptions import BotoCoreError, ClientError

import bppanalyzer.object_store as object_store_module
from bppanalyzer.object_store import LocalObjectStore, ObjectStoreError, R2ObjectStore


def test_local_object_store_exposes_stat_get_put_as_the_offline_system_boundary(
    tmp_path: Path,
) -> None:
    now = datetime(2026, 8, 11, 12, 30, tzinfo=UTC)
    store = LocalObjectStore(tmp_path, clock=lambda: now)
    content = b'{"hello":"world"}\n'

    assert store.stat("fixture/data.json") is None
    store.put(
        "fixture/data.json",
        content,
        cache_control="public,max-age=60,must-revalidate",
        content_type="application/json",
    )

    observed = store.get("fixture/data.json")
    assert observed is not None
    assert observed.body == content
    assert observed.stat.sha256 == hashlib.sha256(content).hexdigest()
    assert observed.stat.bytes == len(content)
    assert observed.stat.cache_control == "public,max-age=60,must-revalidate"
    assert observed.stat.content_type == "application/json"
    assert observed.stat.last_modified == now
    assert [(item.operation, item.key) for item in store.requests] == [
        ("stat", "fixture/data.json"),
        ("put", "fixture/data.json"),
        ("get", "fixture/data.json"),
    ]


def test_local_object_store_rejects_invalid_writes_and_corrupt_metadata(tmp_path: Path) -> None:
    store = LocalObjectStore(tmp_path, clock=lambda: datetime(2026, 8, 11, tzinfo=UTC))
    with pytest.raises(TypeError):
        store.put("data.json", "not-bytes", cache_control="cache")  # type: ignore[arg-type]
    with pytest.raises(ValueError, match="Cache-Control"):
        store.put("data.json", b"{}", cache_control="")
    with pytest.raises(ValueError, match="Content-Type"):
        store.put("data.json", b"{}", cache_control="cache", content_type="")
    with pytest.raises(ValueError, match="key"):
        store.stat("../escape.json")

    store.put("data.json", b"{}", cache_control="cache", content_type="application/json")
    metadata = tmp_path / "metadata/data.json.json"
    metadata.write_text("{}")
    with pytest.raises(ObjectStoreError, match="unreadable"):
        store.stat("data.json")
    metadata.unlink()
    with pytest.raises(ObjectStoreError, match="incomplete"):
        store.stat("data.json")


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
