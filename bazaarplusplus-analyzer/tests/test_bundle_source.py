import hashlib
import json
import threading
import time
from datetime import UTC, datetime

import httpx
import pytest

from bppanalyzer.bundle_source import (
    BundleSource,
    HourExpired,
    RetryableSourceError,
    SourceContractError,
)
from tests.bundle_fixtures import bundle_bytes

HOUR = datetime(2026, 8, 10, 12, tzinfo=UTC)


def _digest(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def _empty_page() -> dict[str, object]:
    return {
        "window": {
            "available_from_ms": int(HOUR.timestamp() * 1_000),
            "available_before_ms": int(HOUR.timestamp() * 1_000) + 3_600_000,
        },
        "items": [],
        "next_after": None,
    }


def _one_bundle_page(
    content: bytes,
    *,
    download_url: str = "https://download.invalid/bundle-a",
) -> dict[str, object]:
    page = _empty_page()
    page["items"] = [
        {
            "bundle_id": "bundle-a",
            "available_at_ms": int(HOUR.timestamp() * 1_000) + 1,
            "download_url": download_url,
            "download_expires_at_ms": int(HOUR.timestamp() * 1_000) + 60_000,
            "sha256": _digest(content),
            "bytes": len(content),
        }
    ]
    return page


def _retrying_source(
    server: httpx.MockTransport,
    backoffs: list[float],
) -> BundleSource:
    return BundleSource(
        api_base_url="https://api.invalid",
        sync_token="test-token",
        client=httpx.Client(transport=server),
        clock=lambda: datetime(2026, 8, 11, tzinfo=UTC),
        sleep=backoffs.append,
        jitter=lambda delay: delay,
    )


def test_listing_succeeds_after_two_transient_503_responses() -> None:
    attempts = 0
    backoffs: list[float] = []

    def server(_request: httpx.Request) -> httpx.Response:
        nonlocal attempts
        attempts += 1
        if attempts <= 2:
            return httpx.Response(503)
        return httpx.Response(200, json=_empty_page())

    source = _retrying_source(httpx.MockTransport(server), backoffs)

    index = source.hour_index(HOUR)

    assert index.items == ()
    assert attempts == 3
    assert backoffs == [1.0, 2.0]


@pytest.mark.parametrize(
    ("retry_after", "expected_delay"),
    [
        ("5", 5.0),
        ("Tue, 11 Aug 2026 00:00:05 GMT", 5.0),
        ("120", 60.0),
    ],
)
def test_retry_after_delays_the_next_listing_attempt(
    retry_after: str, expected_delay: float
) -> None:
    attempts = 0
    backoffs: list[float] = []

    def server(_request: httpx.Request) -> httpx.Response:
        nonlocal attempts
        attempts += 1
        if attempts == 1:
            return httpx.Response(429, headers={"Retry-After": retry_after})
        return httpx.Response(200, json=_empty_page())

    source = _retrying_source(httpx.MockTransport(server), backoffs)

    assert source.hour_index(HOUR).items == ()
    assert attempts == 2
    assert backoffs == [expected_delay]


def test_default_retry_backoff_uses_bounded_jitter() -> None:
    attempts = 0
    backoffs: list[float] = []

    def server(_request: httpx.Request) -> httpx.Response:
        nonlocal attempts
        attempts += 1
        if attempts == 1:
            return httpx.Response(503)
        return httpx.Response(200, json=_empty_page())

    source = BundleSource(
        api_base_url="https://api.invalid",
        sync_token="test-token",
        client=httpx.Client(transport=httpx.MockTransport(server)),
        clock=lambda: datetime(2026, 8, 11, tzinfo=UTC),
        sleep=backoffs.append,
    )

    assert source.hour_index(HOUR).items == ()
    assert len(backoffs) == 1
    assert 0.5 <= backoffs[0] <= 1.5


@pytest.mark.parametrize("failure", ["transport", "retryable_body"])
def test_listing_retries_transport_and_retryable_error_bodies(failure: str) -> None:
    attempts = 0
    backoffs: list[float] = []

    def server(request: httpx.Request) -> httpx.Response:
        nonlocal attempts
        attempts += 1
        if attempts == 1 and failure == "transport":
            raise httpx.ReadError("fixture transport failure", request=request)
        if attempts == 1:
            return httpx.Response(
                400,
                json={
                    "error": {
                        "code": "listing_not_ready",
                        "message": "try later",
                        "retryable": True,
                    }
                },
            )
        return httpx.Response(200, json=_empty_page())

    source = _retrying_source(httpx.MockTransport(server), backoffs)

    assert source.hour_index(HOUR).items == ()
    assert attempts == 2
    assert backoffs == [1.0]


def test_download_succeeds_after_a_transport_error() -> None:
    content = bundle_bytes("bundle-a")
    download_attempts = 0
    backoffs: list[float] = []

    def server(request: httpx.Request) -> httpx.Response:
        nonlocal download_attempts
        if request.url.host == "api.invalid":
            return httpx.Response(200, json=_one_bundle_page(content))
        download_attempts += 1
        if download_attempts == 1:
            raise httpx.ConnectError("fixture transport failure", request=request)
        return httpx.Response(200, content=content)

    source = _retrying_source(httpx.MockTransport(server), backoffs)

    downloaded = list(source.stream(source.hour_index(HOUR)))

    assert download_attempts == 2
    assert backoffs == [1.0]
    assert downloaded[0].bytes == len(content)
    assert downloaded[0].manifest["bundle_id"] == "bundle-a"


def test_window_expired_410_is_never_retried() -> None:
    attempts = 0
    backoffs: list[float] = []

    def server(_request: httpx.Request) -> httpx.Response:
        nonlocal attempts
        attempts += 1
        return httpx.Response(
            410,
            json={
                "error": {
                    "code": "window_expired",
                    "message": "outside retention",
                    "retryable": False,
                }
            },
        )

    source = _retrying_source(httpx.MockTransport(server), backoffs)

    with pytest.raises(HourExpired) as raised:
        source.hour_index(HOUR)

    assert raised.value.reason == "window_expired"
    assert attempts == 1
    assert backoffs == []


def test_download_window_expired_410_is_never_retried() -> None:
    content = bundle_bytes("bundle-a")
    download_attempts = 0
    backoffs: list[float] = []

    def server(request: httpx.Request) -> httpx.Response:
        nonlocal download_attempts
        if request.url.host == "api.invalid":
            return httpx.Response(200, json=_one_bundle_page(content))
        download_attempts += 1
        return httpx.Response(
            410,
            json={
                "error": {
                    "code": "window_expired",
                    "message": "outside retention",
                    "retryable": True,
                }
            },
        )

    source = _retrying_source(httpx.MockTransport(server), backoffs)

    with pytest.raises(HourExpired):
        list(source.stream(source.hour_index(HOUR)))

    assert download_attempts == 1
    assert backoffs == []


def test_retries_exhausted_still_raise_the_retryable_source_error() -> None:
    attempts = 0
    backoffs: list[float] = []

    def server(_request: httpx.Request) -> httpx.Response:
        nonlocal attempts
        attempts += 1
        return httpx.Response(
            429,
            json={
                "error": {
                    "code": "rate_limited",
                    "message": "try later",
                    "retryable": True,
                }
            },
        )

    source = _retrying_source(httpx.MockTransport(server), backoffs)

    with pytest.raises(RetryableSourceError) as raised:
        source.hour_index(HOUR)

    assert raised.value.reason == "rate_limited"
    assert attempts == 4
    assert backoffs == [1.0, 2.0, 4.0]


@pytest.mark.parametrize(
    ("status_code", "error_body"),
    [
        (408, None),
        (429, None),
        (503, None),
        (
            400,
            {
                "error": {
                    "code": "download_not_ready",
                    "message": "try later",
                    "retryable": True,
                }
            },
        ),
    ],
)
def test_download_retries_every_declared_transient_http_response(
    status_code: int,
    error_body: dict[str, object] | None,
) -> None:
    content = bundle_bytes("bundle-a")
    download_attempts = 0
    backoffs: list[float] = []

    def server(request: httpx.Request) -> httpx.Response:
        nonlocal download_attempts
        if request.url.host == "api.invalid":
            return httpx.Response(200, json=_one_bundle_page(content))
        download_attempts += 1
        if download_attempts == 1:
            if error_body is not None:
                return httpx.Response(status_code, json=error_body)
            return httpx.Response(status_code)
        return httpx.Response(200, content=content)

    source = _retrying_source(httpx.MockTransport(server), backoffs)

    downloaded = list(source.stream(source.hour_index(HOUR)))

    assert download_attempts == 2
    assert backoffs == [1.0]
    assert downloaded[0].bytes == len(content)


@pytest.mark.parametrize(
    ("status_code", "error_body"),
    [
        (
            401,
            {
                "error": {
                    "code": "unauthorized",
                    "message": "bad credential",
                    "retryable": True,
                }
            },
        ),
        (404, None),
    ],
)
def test_download_auth_and_contract_failures_are_never_retried(
    status_code: int,
    error_body: dict[str, object] | None,
) -> None:
    content = bundle_bytes("bundle-a")
    download_attempts = 0
    backoffs: list[float] = []

    def server(request: httpx.Request) -> httpx.Response:
        nonlocal download_attempts
        if request.url.host == "api.invalid":
            return httpx.Response(200, json=_one_bundle_page(content))
        download_attempts += 1
        if error_body is not None:
            return httpx.Response(status_code, json=error_body)
        return httpx.Response(status_code)

    source = _retrying_source(httpx.MockTransport(server), backoffs)

    with pytest.raises(SourceContractError):
        list(source.stream(source.hour_index(HOUR)))

    assert download_attempts == 1
    assert backoffs == []


@pytest.mark.parametrize("failure", ["auth", "contract"])
def test_listing_auth_and_contract_failures_are_never_retried(failure: str) -> None:
    attempts = 0
    backoffs: list[float] = []

    def server(_request: httpx.Request) -> httpx.Response:
        nonlocal attempts
        attempts += 1
        if failure == "auth":
            return httpx.Response(
                401,
                json={
                    "error": {
                        "code": "unauthorized",
                        "message": "bad credential",
                        "retryable": True,
                    }
                },
            )
        return httpx.Response(200, json={"not": "the listing contract"})

    source = _retrying_source(httpx.MockTransport(server), backoffs)

    with pytest.raises(SourceContractError):
        source.hour_index(HOUR)

    assert attempts == 1
    assert backoffs == []


def test_hour_index_exhausts_keyset_pages_and_returns_ordered_deduplicated_index() -> None:
    first = b"first"
    second = b"second"
    requests: list[httpx.Request] = []

    def server(request: httpx.Request) -> httpx.Response:
        requests.append(request)
        cursor = request.url.params.get("after_bundle_id")
        common = {
            "window": {
                "available_from_ms": int(HOUR.timestamp() * 1_000),
                "available_before_ms": int(HOUR.timestamp() * 1_000) + 3_600_000,
            }
        }
        if cursor is None:
            return httpx.Response(
                200,
                json={
                    **common,
                    "items": [
                        {
                            "bundle_id": "bundle-a",
                            "available_at_ms": int(HOUR.timestamp() * 1_000) + 1,
                            "download_url": "https://download.invalid/a",
                            "download_expires_at_ms": int(HOUR.timestamp() * 1_000) + 9_000,
                            "sha256": _digest(first),
                            "bytes": len(first),
                        }
                    ],
                    "next_after": {
                        "available_at_ms": int(HOUR.timestamp() * 1_000) + 1,
                        "bundle_id": "bundle-a",
                    },
                },
            )
        return httpx.Response(
            200,
            json={
                **common,
                "items": [
                    {
                        "bundle_id": "bundle-a",
                        "available_at_ms": int(HOUR.timestamp() * 1_000) + 1,
                        "download_url": "https://download.invalid/a-refreshed",
                        "download_expires_at_ms": int(HOUR.timestamp() * 1_000) + 10_000,
                        "sha256": _digest(first),
                        "bytes": len(first),
                    },
                    {
                        "bundle_id": "bundle-b",
                        "available_at_ms": int(HOUR.timestamp() * 1_000) + 2,
                        "download_url": "https://download.invalid/b",
                        "download_expires_at_ms": int(HOUR.timestamp() * 1_000) + 9_000,
                        "sha256": _digest(second),
                        "bytes": len(second),
                    },
                ],
                "next_after": None,
            },
        )

    source = BundleSource(
        api_base_url="https://api.invalid",
        sync_token="test-token",
        client=httpx.Client(transport=httpx.MockTransport(server)),
        clock=lambda: datetime(2026, 8, 11, tzinfo=UTC),
    )

    index = source.hour_index(HOUR)

    assert [item.bundle_id for item in index.items] == ["bundle-a", "bundle-b"]
    assert index.pages == 2
    assert len(requests) == 2
    assert requests[1].url.params["after_available_at_ms"] == str(int(HOUR.timestamp() * 1_000) + 1)
    assert requests[1].url.params["after_bundle_id"] == "bundle-a"
    expected_identity = [
        {
            "available_at_ms": int(HOUR.timestamp() * 1_000) + 1,
            "bundle_id": "bundle-a",
            "bytes": len(first),
            "sha256": _digest(first),
        },
        {
            "available_at_ms": int(HOUR.timestamp() * 1_000) + 2,
            "bundle_id": "bundle-b",
            "bytes": len(second),
            "sha256": _digest(second),
        },
    ]
    canonical = (
        json.dumps(expected_identity, sort_keys=True, separators=(",", ":")) + "\n"
    ).encode()
    assert index.raw_commit_sha256 == hashlib.sha256(canonical).hexdigest()


def test_empty_index_past_retention_is_expired_instead_of_a_zero_row_hour() -> None:
    def empty(request: httpx.Request) -> httpx.Response:
        return httpx.Response(
            200,
            json={
                "window": {
                    "available_from_ms": int(HOUR.timestamp() * 1_000),
                    "available_before_ms": int(HOUR.timestamp() * 1_000) + 3_600_000,
                },
                "items": [],
                "next_after": None,
            },
        )

    source = BundleSource(
        api_base_url="https://api.invalid",
        sync_token="test-token",
        client=httpx.Client(transport=httpx.MockTransport(empty)),
        clock=lambda: datetime(2026, 8, 21, tzinfo=UTC),
        retention_days=8,
    )

    with pytest.raises(HourExpired, match="past retention"):
        source.hour_index(HOUR)


def test_expired_download_capability_reenumerates_the_same_fixed_hour() -> None:
    content = bundle_bytes("bundle-a")
    enumerations = 0
    refreshed_attempts = 0
    backoffs: list[float] = []

    def server(request: httpx.Request) -> httpx.Response:
        nonlocal enumerations, refreshed_attempts
        if request.url.host == "api.invalid":
            enumerations += 1
            return httpx.Response(
                200,
                json=_one_bundle_page(
                    content,
                    download_url=(
                        "https://download.invalid/expired"
                        if enumerations == 1
                        else "https://download.invalid/refreshed"
                    ),
                ),
            )
        if request.url.path == "/expired":
            return httpx.Response(403)
        refreshed_attempts += 1
        if refreshed_attempts <= 2:
            return httpx.Response(503)
        return httpx.Response(200, content=content)

    source = _retrying_source(httpx.MockTransport(server), backoffs)

    index = source.hour_index(HOUR)
    downloaded = list(source.stream(index))

    assert enumerations == 2
    assert refreshed_attempts == 3
    assert backoffs == [1.0, 2.0]
    assert downloaded[0].bytes == len(content)
    assert downloaded[0].manifest["bundle_id"] == "bundle-a"


def test_download_url_refresh_is_bounded_to_one_reenumeration() -> None:
    content = bundle_bytes("bundle-a")
    enumerations = 0
    download_attempts = 0
    backoffs: list[float] = []

    def server(request: httpx.Request) -> httpx.Response:
        nonlocal enumerations, download_attempts
        if request.url.host == "api.invalid":
            enumerations += 1
            return httpx.Response(
                200,
                json=_one_bundle_page(
                    content,
                    download_url=f"https://download.invalid/url-{enumerations}",
                ),
            )
        download_attempts += 1
        return httpx.Response(403)

    source = _retrying_source(httpx.MockTransport(server), backoffs)

    with pytest.raises(RetryableSourceError) as raised:
        list(source.stream(source.hour_index(HOUR)))

    assert raised.value.reason == "download_url_expired"
    assert enumerations == 2
    assert download_attempts == 2
    assert backoffs == []


def test_stream_never_downloads_beyond_its_eight_bundle_lookahead() -> None:
    contents = {
        f"bundle-{number:02d}": bundle_bytes(f"bundle-{number:02d}") for number in range(20)
    }
    gate = threading.Event()
    eight_started = threading.Event()
    counter_lock = threading.Lock()
    started = 0

    def server(request: httpx.Request) -> httpx.Response:
        nonlocal started
        if request.url.host == "api.invalid":
            return httpx.Response(
                200,
                json={
                    "window": {
                        "available_from_ms": int(HOUR.timestamp() * 1_000),
                        "available_before_ms": int(HOUR.timestamp() * 1_000) + 3_600_000,
                    },
                    "items": [
                        {
                            "bundle_id": bundle_id,
                            "available_at_ms": int(HOUR.timestamp() * 1_000) + number,
                            "download_url": f"https://download.invalid/{bundle_id}",
                            "download_expires_at_ms": int(HOUR.timestamp() * 1_000) + 60_000,
                            "sha256": hashlib.sha256(content).hexdigest(),
                            "bytes": len(content),
                        }
                        for number, (bundle_id, content) in enumerate(contents.items())
                    ],
                    "next_after": None,
                },
            )
        with counter_lock:
            started += 1
            if started == 8:
                eight_started.set()
        assert gate.wait(2)
        return httpx.Response(200, content=contents[request.url.path.removeprefix("/")])

    source = BundleSource(
        api_base_url="https://api.invalid",
        sync_token="test-token",
        client=httpx.Client(transport=httpx.MockTransport(server)),
        clock=lambda: datetime(2026, 8, 11, tzinfo=UTC),
        download_concurrency=16,
        lookahead=8,
    )
    index = source.hour_index(HOUR)
    stream = source.stream(index)
    result: list[object] = []
    worker = threading.Thread(target=lambda: result.append(next(stream)))
    worker.start()

    assert eight_started.wait(1)
    time.sleep(0.02)
    assert started == 8
    gate.set()
    worker.join(2)
    assert len(result) == 1
    stream.close()


def test_stream_stops_starting_downloads_after_a_fatal_bundle_failure() -> None:
    contents = {f"bundle-{number:02d}": bundle_bytes(f"bundle-{number:02d}") for number in range(8)}
    second_started = threading.Event()
    release_second = threading.Event()
    counter_lock = threading.Lock()
    started = 0

    def server(request: httpx.Request) -> httpx.Response:
        nonlocal started
        if request.url.host == "api.invalid":
            return httpx.Response(
                200,
                json={
                    "window": {
                        "available_from_ms": int(HOUR.timestamp() * 1_000),
                        "available_before_ms": int(HOUR.timestamp() * 1_000) + 3_600_000,
                    },
                    "items": [
                        {
                            "bundle_id": bundle_id,
                            "available_at_ms": int(HOUR.timestamp() * 1_000) + number,
                            "download_url": f"https://download.invalid/{bundle_id}",
                            "download_expires_at_ms": int(HOUR.timestamp() * 1_000) + 60_000,
                            "sha256": hashlib.sha256(content).hexdigest(),
                            "bytes": len(content),
                        }
                        for number, (bundle_id, content) in enumerate(contents.items())
                    ],
                    "next_after": None,
                },
            )
        with counter_lock:
            started += 1
            observed = started
        if observed == 1:
            return httpx.Response(404)
        if observed == 2:
            second_started.set()
            assert release_second.wait(2)
        bundle_id = request.url.path.removeprefix("/")
        return httpx.Response(200, content=contents[bundle_id])

    source = BundleSource(
        api_base_url="https://api.invalid",
        sync_token="test-token",
        client=httpx.Client(transport=httpx.MockTransport(server)),
        clock=lambda: datetime(2026, 8, 11, tzinfo=UTC),
        download_concurrency=1,
        lookahead=8,
    )
    errors: list[BaseException] = []

    def consume() -> None:
        try:
            list(source.stream(source.hour_index(HOUR)))
        except BaseException as error:
            errors.append(error)

    worker = threading.Thread(target=consume)
    worker.start()
    assert second_started.wait(1)
    release_second.set()
    worker.join(2)

    assert not worker.is_alive()
    assert len(errors) == 1 and isinstance(errors[0], SourceContractError)
    assert started <= 2
