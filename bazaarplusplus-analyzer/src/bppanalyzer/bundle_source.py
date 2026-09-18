"""Bounded, in-memory access to immutable Bundle Server Source Hours."""

import hashlib
import json
import math
import random
import re
import threading
import time
from collections.abc import Callable, Iterator, Mapping
from concurrent.futures import Future, ThreadPoolExecutor
from dataclasses import dataclass
from datetime import UTC, datetime, timedelta
from email.utils import parsedate_to_datetime
from typing import Any, Self, TypeVar

import httpx

BUNDLE_MAGIC = b"BPPBNDL5"
BUNDLE_VERSION = 5
MAX_BUNDLE_BYTES = 8_388_607
MAX_MANIFEST_BYTES = 2_097_152
MAX_RUN_BYTES = 2_097_151
MAX_SCREENSHOT_BYTES = 1_048_576

_SHA256 = re.compile(r"^[0-9a-f]{64}$")
RETRY_BACKOFF_SECONDS = (1.0, 2.0, 4.0)
MAX_RETRY_AFTER_SECONDS = 60.0

_T = TypeVar("_T")


def _jittered_delay(delay: float) -> float:
    return random.uniform(delay * 0.5, delay * 1.5)


class BundleSourceError(RuntimeError):
    """A safe, classified Bundle Server or Bundle contract failure."""

    def __init__(
        self, reason: str, message: str, *, retry_after_seconds: float | None = None
    ) -> None:
        super().__init__(message)
        self.reason = reason
        self.retry_after_seconds = retry_after_seconds


class HourExpired(BundleSourceError):
    """The requested Source Hour is no longer recoverable."""


class RetryableSourceError(BundleSourceError):
    """A transient request or download failure."""


class SourceContractError(BundleSourceError):
    """The server response violates the fixed collection contract."""


class DownloadUrlExpired(BundleSourceError):
    """A presigned download capability must be refreshed by re-enumeration."""


class _InvalidDownload(BundleSourceError):
    def __init__(self, reason: str, message: str, observed_bytes: int) -> None:
        super().__init__(reason, message)
        self.observed_bytes = observed_bytes


@dataclass(frozen=True, slots=True)
class SourcePerformance:
    listing_requests: int
    listing_retries: int
    download_attempts: int
    download_retries: int
    downloaded_bytes: int
    download_wait_seconds: float
    retry_sleep_seconds: float
    download_latency_ms_p50: float | None
    download_latency_ms_p95: float | None


@dataclass(frozen=True, slots=True)
class BundleRef:
    bundle_id: str
    available_at_ms: int
    download_url: str
    download_expires_at_ms: int
    sha256: str | None
    bytes: int | None

    def __post_init__(self) -> None:
        if not self.bundle_id or not self.download_url:
            raise ValueError("Bundle reference identity and URL are required")
        for name, value in (
            ("available_at_ms", self.available_at_ms),
            ("download_expires_at_ms", self.download_expires_at_ms),
        ):
            if not isinstance(value, int) or isinstance(value, bool) or value < 0:
                raise ValueError(f"Bundle reference {name} is invalid")
        if self.sha256 is not None and _SHA256.fullmatch(self.sha256) is None:
            raise ValueError("Bundle reference sha256 is invalid")
        if self.bytes is not None and (
            not isinstance(self.bytes, int) or isinstance(self.bytes, bool) or self.bytes < 0
        ):
            raise ValueError("Bundle reference byte count is invalid")

    @property
    def cursor(self) -> tuple[int, str]:
        return self.available_at_ms, self.bundle_id


@dataclass(frozen=True, slots=True)
class RawHourIndex:
    source_hour: datetime
    items: tuple[BundleRef, ...]
    raw_commit_sha256: str
    pages: int

    def __post_init__(self) -> None:
        object.__setattr__(self, "source_hour", parse_source_hour(self.source_hour))
        _validate_index(self)


@dataclass(frozen=True, slots=True)
class Bundle:
    """One admitted in-memory Bundle ready for projection."""

    ref: BundleRef
    sha256: str
    bytes: int
    manifest: Mapping[str, Any]
    run_content: bytes

    def __post_init__(self) -> None:
        if (
            not isinstance(self.sha256, str)
            or _SHA256.fullmatch(self.sha256) is None
            or not isinstance(self.bytes, int)
            or isinstance(self.bytes, bool)
            or self.bytes < 1
        ):
            raise ValueError("Admitted Bundle digest and byte count are required")
        if not isinstance(self.manifest, Mapping) or not isinstance(self.run_content, bytes):
            raise TypeError("Admitted Bundle manifest and Run content are required")
        if not self.run_content:
            raise ValueError("Admitted Bundle Run content is required")


class BundleSource:
    """Enumerate a complete hour, then stream verified downloads in index order."""

    def __init__(
        self,
        *,
        api_base_url: str,
        sync_token: str,
        client: httpx.Client | None = None,
        clock: Callable[[], datetime] = lambda: datetime.now(UTC),
        retention_days: int = 8,
        download_concurrency: int = 64,
        lookahead: int = 128,
        page_limit: int = 200,
        sleep: Callable[[float], None] = time.sleep,
        jitter: Callable[[float], float] = _jittered_delay,
    ) -> None:
        if not api_base_url or not sync_token:
            raise ValueError("Bundle Server URL and sync token are required")
        if retention_days < 1 or download_concurrency < 1 or lookahead < 1:
            raise ValueError("Bundle Source limits must be positive")
        if not 1 <= page_limit <= 500:
            raise ValueError("Bundle Server page limit must be between 1 and 500")
        self._api_base_url = api_base_url.rstrip("/")
        self._sync_token = sync_token
        self._client = client or httpx.Client(
            timeout=httpx.Timeout(30, connect=10),
            follow_redirects=False,
            limits=httpx.Limits(
                max_connections=download_concurrency + 4,
                max_keepalive_connections=download_concurrency,
            ),
        )
        self._owns_client = client is None
        self._clock = clock
        self._retention = timedelta(days=retention_days)
        self._download_concurrency = download_concurrency
        self._lookahead = lookahead
        self._page_limit = page_limit
        self._sleep = sleep
        self._jitter = jitter
        self._refresh_lock = threading.Lock()
        self._performance_lock = threading.Lock()
        self._listing_requests = 0
        self._listing_retries = 0
        self._download_attempts = 0
        self._download_retries = 0
        self._downloaded_bytes = 0
        self._download_wait_seconds = 0.0
        self._retry_sleep_seconds = 0.0
        self._download_latencies_ms: list[float] = []

    def close(self) -> None:
        if self._owns_client:
            self._client.close()

    def __enter__(self) -> Self:
        return self

    def __exit__(self, *_args: object) -> None:
        self.close()

    def performance_snapshot(self, *, reset: bool = False) -> SourcePerformance:
        """Return low-cardinality collection measurements for one Run."""
        with self._performance_lock:
            latencies = tuple(self._download_latencies_ms)
            value = SourcePerformance(
                listing_requests=self._listing_requests,
                listing_retries=self._listing_retries,
                download_attempts=self._download_attempts,
                download_retries=self._download_retries,
                downloaded_bytes=self._downloaded_bytes,
                download_wait_seconds=round(self._download_wait_seconds, 6),
                retry_sleep_seconds=round(self._retry_sleep_seconds, 6),
                download_latency_ms_p50=_percentile(latencies, 0.50),
                download_latency_ms_p95=_percentile(latencies, 0.95),
            )
            if reset:
                self._listing_requests = 0
                self._listing_retries = 0
                self._download_attempts = 0
                self._download_retries = 0
                self._downloaded_bytes = 0
                self._download_wait_seconds = 0.0
                self._retry_sleep_seconds = 0.0
                self._download_latencies_ms.clear()
            return value

    def hour_index(self, source_hour: datetime | str) -> RawHourIndex:
        hour = parse_source_hour(source_hour)
        start_ms = int(hour.timestamp() * 1_000)
        end_ms = start_ms + 3_600_000
        cursor: tuple[int, str] | None = None
        pages = 0
        by_id: dict[str, BundleRef] = {}

        while True:
            params = {
                "available_from_ms": str(start_ms),
                "available_before_ms": str(end_ms),
                "limit": str(self._page_limit),
            }
            if cursor is not None:
                params["after_available_at_ms"] = str(cursor[0])
                params["after_bundle_id"] = cursor[1]
            response = self._retry(lambda: self._listing_request(params), kind="listing")
            try:
                payload = response.json()
            except ValueError as error:
                raise SourceContractError(
                    "source_response_invalid", "Bundle collection response is not JSON"
                ) from error
            page_items, next_cursor = self._parse_page(payload, start_ms=start_ms, end_ms=end_ms)
            pages += 1
            if not page_items and next_cursor is not None:
                raise SourceContractError(
                    "source_cursor_invalid", "An empty page supplied a next cursor"
                )
            previous = cursor
            for item in page_items:
                if previous is not None and item.cursor < previous:
                    raise SourceContractError(
                        "source_keyset_order_invalid",
                        "Bundle keyset order is not increasing",
                    )
                previous = item.cursor
                existing = by_id.get(item.bundle_id)
                if existing is not None:
                    if _stable_ref(existing) != _stable_ref(item):
                        raise SourceContractError(
                            "source_identity_conflict",
                            "A Bundle ID was listed with conflicting immutable metadata",
                        )
                    by_id[item.bundle_id] = item
                    continue
                by_id[item.bundle_id] = item
            if next_cursor is None:
                break
            if not page_items or next_cursor != page_items[-1].cursor:
                raise SourceContractError(
                    "source_cursor_invalid", "The next cursor is not the final page item"
                )
            if cursor is not None and next_cursor <= cursor:
                raise SourceContractError(
                    "source_cursor_invalid", "The Bundle keyset cursor did not advance"
                )
            cursor = next_cursor

        items = tuple(sorted(by_id.values(), key=lambda item: item.cursor))
        if not items and self._is_past_retention(hour):
            raise HourExpired("source_hour_expired", "An empty Source Hour is past retention")
        return RawHourIndex(
            source_hour=hour,
            items=items,
            raw_commit_sha256=raw_commit_sha256(items),
            pages=pages,
        )

    def _listing_request(self, params: Mapping[str, str]) -> httpx.Response:
        with self._performance_lock:
            self._listing_requests += 1
        try:
            response = self._client.get(
                f"{self._api_base_url}/bundles",
                params=params,
                headers={"Authorization": f"Bearer {self._sync_token}"},
            )
        except httpx.TransportError as error:
            raise RetryableSourceError(
                "source_transport_error", "Bundle collection transport failed"
            ) from error
        self._raise_response_error(response)
        return response

    def _retry(self, operation: Callable[[], _T], *, kind: str) -> _T:
        for attempt in range(len(RETRY_BACKOFF_SECONDS) + 1):
            try:
                return operation()
            except RetryableSourceError as error:
                if attempt == len(RETRY_BACKOFF_SECONDS):
                    raise
                with self._performance_lock:
                    if kind == "listing":
                        self._listing_retries += 1
                    else:
                        self._download_retries += 1
                delay = self._jitter(RETRY_BACKOFF_SECONDS[attempt])
                if not isinstance(delay, int | float) or isinstance(delay, bool):
                    raise TypeError("Retry jitter must return seconds")
                delay = float(delay)
                if not math.isfinite(delay) or delay < 0:
                    raise ValueError("Retry jitter must return finite non-negative seconds")
                if error.retry_after_seconds is not None:
                    delay = max(delay, min(error.retry_after_seconds, MAX_RETRY_AFTER_SECONDS))
                with self._performance_lock:
                    self._retry_sleep_seconds += delay
                self._sleep(delay)
        raise AssertionError("Retry loop must return or raise")

    def stream(self, index: RawHourIndex) -> Iterator[Bundle]:
        """Yield at most ``lookahead`` retained downloads, in index order."""
        _validate_index(index)
        executor = ThreadPoolExecutor(max_workers=self._download_concurrency)
        try:
            pending: dict[int, Future[Bundle]] = {}
            submitted = 0
            yielded = 0
            while yielded < len(index.items):
                while submitted < len(index.items) and len(pending) < self._lookahead:
                    pending[submitted] = executor.submit(
                        self._download_and_validate, index, index.items[submitted]
                    )
                    submitted += 1
                future = pending.pop(yielded)
                wait_started = time.perf_counter()
                try:
                    bundle = future.result()
                finally:
                    waited = time.perf_counter() - wait_started
                    with self._performance_lock:
                        self._download_wait_seconds += waited
                yield bundle
                yielded += 1
        except BaseException:
            executor.shutdown(wait=True, cancel_futures=True)
            raise
        else:
            executor.shutdown(wait=True)

    def _download_and_validate(self, index: RawHourIndex, item: BundleRef) -> Bundle:
        active = item
        try:
            content = self._download(active)
        except _InvalidDownload as error:
            raise _bundle_admission_failure(error) from error
        except DownloadUrlExpired:
            with self._refresh_lock:
                refreshed = self.hour_index(index.source_hour)
            if refreshed.raw_commit_sha256 != index.raw_commit_sha256 or [
                _stable_ref(value) for value in refreshed.items
            ] != [_stable_ref(value) for value in index.items]:
                raise SourceContractError(
                    "source_window_changed",
                    "Bundle identities changed while refreshing a download URL",
                ) from None
            try:
                active = next(
                    value for value in refreshed.items if value.bundle_id == item.bundle_id
                )
            except StopIteration:
                raise SourceContractError(
                    "source_window_changed", "A Bundle disappeared during URL refresh"
                ) from None
            try:
                content = self._download(active)
            except _InvalidDownload as error:
                raise _bundle_admission_failure(error) from error
            except DownloadUrlExpired:
                raise RetryableSourceError(
                    "download_url_expired", "Refreshed Bundle capability expired"
                ) from None
        try:
            return admit_bundle(item, content)
        except BundleSourceError as error:
            raise _bundle_admission_failure(error) from error

    def _download(self, item: BundleRef) -> bytes:
        return self._retry(lambda: self._download_once(item), kind="download")

    def _download_once(self, item: BundleRef) -> bytes:
        started = time.perf_counter()
        with self._performance_lock:
            self._download_attempts += 1
        try:
            with self._client.stream("GET", item.download_url) as response:
                if response.status_code >= 400:
                    response.read()
                    code, body_retryable = _response_error_details(response)
                else:
                    code, body_retryable = None, False
                if response.status_code == 410 and code == "window_expired":
                    raise HourExpired(code, "The Bundle Server no longer retains this hour")
                if response.status_code == 403:
                    raise DownloadUrlExpired(
                        "download_url_expired", "Bundle download capability expired"
                    )
                if response.status_code == 401:
                    raise SourceContractError(
                        code or "bundle_download_failed",
                        "Bundle download authentication failed",
                    )
                if (
                    response.status_code in {408, 429}
                    or response.status_code >= 500
                    or body_retryable
                ):
                    raise RetryableSourceError(
                        code or "bundle_download_retryable",
                        "Bundle download temporarily failed",
                        retry_after_seconds=_retry_after_seconds(response, self._clock()),
                    )
                if response.status_code >= 400:
                    raise SourceContractError(
                        code or "bundle_download_failed", "Bundle download was rejected"
                    )
                declared_length: int | None = None
                if "Content-Length" in response.headers:
                    try:
                        declared_length = int(response.headers["Content-Length"])
                    except ValueError:
                        raise _InvalidDownload(
                            "bundle_length_mismatch", "Bundle length header is invalid", 0
                        ) from None
                    if declared_length < 0 or declared_length > MAX_BUNDLE_BYTES:
                        raise _InvalidDownload(
                            "bundle_too_large",
                            "Bundle length exceeds the byte limit",
                            max(declared_length, 0),
                        )
                chunks: list[bytes] = []
                observed = 0
                for chunk in response.iter_bytes():
                    observed += len(chunk)
                    if observed > MAX_BUNDLE_BYTES:
                        raise _InvalidDownload(
                            "bundle_too_large", "Bundle exceeds the byte limit", observed
                        )
                    chunks.append(chunk)
                content = b"".join(chunks)
                if declared_length is not None and len(content) != declared_length:
                    raise _InvalidDownload(
                        "bundle_length_mismatch",
                        "Bundle length differs from Content-Length",
                        len(content),
                    )
                with self._performance_lock:
                    self._downloaded_bytes += len(content)
                return content
        except httpx.TransportError as error:
            raise RetryableSourceError(
                "source_transport_error", "Bundle download transport failed"
            ) from error
        finally:
            elapsed_ms = max(time.perf_counter() - started, 0.0) * 1000
            with self._performance_lock:
                self._download_latencies_ms.append(elapsed_ms)

    def _is_past_retention(self, hour: datetime) -> bool:
        now = self._clock()
        if now.tzinfo is None or now.utcoffset() is None:
            raise ValueError("Bundle Source clock must be timezone-aware")
        return hour < now.astimezone(UTC) - self._retention

    def _raise_response_error(self, response: httpx.Response) -> None:
        if response.status_code < 400:
            return
        observed_code, retryable = _response_error_details(response)
        code = observed_code or "source_request_failed"
        if response.status_code == 410:
            raise HourExpired(code, "The Bundle Server no longer retains this hour")
        if response.status_code in {401, 403}:
            raise SourceContractError(code, "Bundle collection authentication failed")
        if response.status_code in {408, 429} or response.status_code >= 500 or retryable:
            raise RetryableSourceError(
                code,
                "Bundle collection temporarily failed",
                retry_after_seconds=_retry_after_seconds(response, self._clock()),
            )
        raise SourceContractError(code, "Bundle collection request was rejected")

    @classmethod
    def _parse_page(
        cls, payload: object, *, start_ms: int, end_ms: int
    ) -> tuple[tuple[BundleRef, ...], tuple[int, str] | None]:
        root = _mapping(payload, "response")
        window = _mapping(root.get("window"), "window")
        if (
            _integer(window.get("available_from_ms"), "available_from_ms") != start_ms
            or _integer(window.get("available_before_ms"), "available_before_ms") != end_ms
        ):
            raise SourceContractError(
                "source_window_mismatch", "Bundle response changed the fixed window"
            )
        raw_items = root.get("items")
        if not isinstance(raw_items, list):
            raise SourceContractError(
                "source_response_invalid", "Bundle response items must be an array"
            )
        items: list[BundleRef] = []
        for raw in raw_items:
            value = _mapping(raw, "item")
            bundle_id = value.get("bundle_id")
            download_url = value.get("download_url")
            if not isinstance(bundle_id, str) or not bundle_id:
                raise SourceContractError("source_response_invalid", "Bundle item ID is invalid")
            if not isinstance(download_url, str) or not download_url:
                raise SourceContractError(
                    "source_response_invalid", "Bundle download URL is invalid"
                )
            available_at_ms = _integer(value.get("available_at_ms"), "available_at_ms")
            if not start_ms <= available_at_ms < end_ms:
                raise SourceContractError(
                    "source_item_outside_window", "Bundle item falls outside the Source Hour"
                )
            digest = value.get("sha256", value.get("bundle_sha256"))
            object_bytes = value.get("bytes", value.get("object_bytes"))
            if digest is not None and (
                not isinstance(digest, str) or _SHA256.fullmatch(digest) is None
            ):
                raise SourceContractError(
                    "source_response_invalid", "Bundle item sha256 is invalid"
                )
            if object_bytes is not None:
                object_bytes = _integer(object_bytes, "bytes")
            items.append(
                BundleRef(
                    bundle_id=bundle_id,
                    available_at_ms=available_at_ms,
                    download_url=download_url,
                    download_expires_at_ms=_integer(
                        value.get("download_expires_at_ms"), "download_expires_at_ms"
                    ),
                    sha256=digest,
                    bytes=object_bytes,
                )
            )
        raw_next = root.get("next_after")
        next_cursor = None
        if raw_next is not None:
            value = _mapping(raw_next, "next_after")
            bundle_id = value.get("bundle_id")
            if not isinstance(bundle_id, str) or not bundle_id:
                raise SourceContractError("source_response_invalid", "Bundle cursor ID is invalid")
            next_cursor = (
                _integer(value.get("available_at_ms"), "available_at_ms"),
                bundle_id,
            )
        return tuple(items), next_cursor


def parse_source_hour(value: datetime | str) -> datetime:
    if isinstance(value, str):
        try:
            value = datetime.strptime(value, "%Y-%m-%dT%H").replace(tzinfo=UTC)
        except ValueError as error:
            raise ValueError("Source Hour must use YYYY-MM-DDTHH") from error
    if not isinstance(value, datetime) or value.tzinfo is None or value.utcoffset() is None:
        raise ValueError("Source Hour must be timezone-aware")
    hour = value.astimezone(UTC)
    if hour.minute or hour.second or hour.microsecond:
        raise ValueError("Source Hour must align to a UTC hour")
    return hour


def source_hour_key(value: datetime | str) -> str:
    return parse_source_hour(value).strftime("%Y-%m-%dT%H")


def raw_commit_sha256(items: tuple[BundleRef, ...]) -> str:
    return hashlib.sha256(_canonical_json([_identity(item) for item in items])).hexdigest()


def admit_bundle(ref: BundleRef, content: bytes) -> Bundle:
    """Validate one complete Bundle once and return its admitted representation."""
    if not isinstance(content, bytes):
        raise TypeError("Bundle content must be bytes")
    digest = hashlib.sha256(content).hexdigest()
    if ref.bytes is not None and len(content) != ref.bytes:
        raise SourceContractError(
            "bundle_length_mismatch", "Downloaded Bundle length differs from its listing"
        )
    if ref.sha256 is not None and digest != ref.sha256:
        raise SourceContractError(
            "bundle_sha256_mismatch", "Downloaded Bundle digest differs from its listing"
        )
    manifest, run_content = open_bundle(content, expected_bundle_id=ref.bundle_id)
    return Bundle(ref, digest, len(content), manifest, run_content)


def validate_bundle(content: bytes, *, expected_bundle_id: str) -> Mapping[str, Any]:
    """Validate the Bundle V5 envelope and every declared segment digest."""
    if len(content) > MAX_BUNDLE_BYTES:
        raise SourceContractError("bundle_too_large", "Bundle exceeds its byte limit")
    if len(content) < 16 or content[:8] != BUNDLE_MAGIC:
        raise SourceContractError("invalid_prefix", "Bundle magic is invalid")
    if int.from_bytes(content[8:12], "big") != BUNDLE_VERSION:
        raise SourceContractError("unsupported_bundle_version", "Bundle version is unsupported")
    manifest_length = int.from_bytes(content[12:16], "big")
    if not 1 <= manifest_length <= MAX_MANIFEST_BYTES:
        raise SourceContractError("manifest_too_large", "Bundle manifest length is invalid")
    payload_start = 16 + manifest_length
    if payload_start >= len(content):
        raise SourceContractError("run_missing", "Bundle Run segment is missing")
    try:
        manifest = json.loads(content[16:payload_start].decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise SourceContractError("manifest_not_json", "Bundle manifest is invalid") from error
    manifest = _mapping(manifest, "manifest")
    if manifest.get("bundle_version") != BUNDLE_VERSION:
        raise SourceContractError(
            "unsupported_bundle_version", "Manifest Bundle version is unsupported"
        )
    if manifest.get("bundle_id") != expected_bundle_id:
        raise SourceContractError(
            "bundle_identity_mismatch", "Listed and manifest Bundle IDs differ"
        )
    _integer(manifest.get("created_at_ms"), "created_at_ms")
    run = _mapping(manifest.get("run"), "run")
    if run.get("run_format_version") != BUNDLE_VERSION:
        raise SourceContractError("unsupported_run_format", "Run format is unsupported")
    if not isinstance(run.get("run_id"), str) or not isinstance(run.get("player_account_id"), str):
        raise SourceContractError("manifest_schema_invalid", "Run identities are invalid")
    projection = _mapping(run.get("projection"), "run.projection")
    _mapping(projection.get("run"), "run.projection.run")
    projected_battles = projection.get("battles")
    if not isinstance(projected_battles, list):
        raise SourceContractError("manifest_schema_invalid", "Run Battle projection is invalid")
    payload = _mapping(run.get("payload"), "run.payload")
    run_offset = _integer(payload.get("offset"), "run.payload.offset")
    run_length = _integer(payload.get("length"), "run.payload.length")
    run_digest = payload.get("sha256")
    if (
        run_offset != 0
        or not 1 <= run_length <= MAX_RUN_BYTES
        or not isinstance(run_digest, str)
        or _SHA256.fullmatch(run_digest) is None
    ):
        raise SourceContractError("run_missing", "Run segment declaration is invalid")
    if payload.get("content_type") != "application/x-bpp-run-v5":
        raise SourceContractError("manifest_schema_invalid", "Run content type is invalid")

    screenshot_length = 0
    screenshot_digest: str | None = None
    if "screenshot" in manifest:
        screenshot = _mapping(manifest["screenshot"], "screenshot")
        offset = _integer(screenshot.get("offset"), "screenshot.offset")
        screenshot_length = _integer(screenshot.get("length"), "screenshot.length")
        screenshot_digest = screenshot.get("sha256")
        if offset != run_length:
            reason = "segment_overlap" if offset < run_length else "segment_out_of_bounds"
            raise SourceContractError(reason, "Screenshot must immediately follow the Run")
        if not 1 <= screenshot_length <= MAX_SCREENSHOT_BYTES:
            raise SourceContractError("screenshot_too_large", "Screenshot length is invalid")
        if not isinstance(screenshot_digest, str) or _SHA256.fullmatch(screenshot_digest) is None:
            raise SourceContractError("manifest_schema_invalid", "Screenshot digest is invalid")

    described_end = payload_start + run_length + screenshot_length
    if described_end != len(content):
        reason = (
            "undeclared_trailing_bytes" if described_end < len(content) else "segment_out_of_bounds"
        )
        raise SourceContractError(reason, "Bundle segment layout is invalid")
    run_bytes = content[payload_start : payload_start + run_length]
    if hashlib.sha256(run_bytes).hexdigest() != run_digest:
        raise SourceContractError("segment_digest_mismatch", "Run segment digest does not match")
    if screenshot_digest is not None:
        screenshot_bytes = content[payload_start + run_length : described_end]
        if hashlib.sha256(screenshot_bytes).hexdigest() != screenshot_digest:
            raise SourceContractError(
                "segment_digest_mismatch", "Screenshot segment digest does not match"
            )
    return manifest


def open_bundle(content: bytes, *, expected_bundle_id: str) -> tuple[Mapping[str, Any], bytes]:
    manifest = validate_bundle(content, expected_bundle_id=expected_bundle_id)
    manifest_length = int.from_bytes(content[12:16], "big")
    payload_start = 16 + manifest_length
    run = _mapping(manifest["run"], "run")
    payload = _mapping(run["payload"], "run.payload")
    length = _integer(payload["length"], "run.payload.length")
    return manifest, content[payload_start : payload_start + length]


def _bundle_admission_failure(error: BundleSourceError) -> RetryableSourceError:
    return RetryableSourceError(
        "bundle_validation_failed", f"Bundle validation failed: {error.reason}"
    )


def _retry_after_seconds(response: httpx.Response, now: datetime) -> float | None:
    value = response.headers.get("Retry-After")
    if value is None:
        return None
    try:
        seconds = float(value)
    except ValueError:
        try:
            retry_at = parsedate_to_datetime(value)
        except TypeError, ValueError, OverflowError:
            return None
        if retry_at.tzinfo is None or retry_at.utcoffset() is None:
            return None
        if now.tzinfo is None or now.utcoffset() is None:
            raise ValueError("Bundle Source clock must be timezone-aware")
        seconds = (retry_at.astimezone(UTC) - now.astimezone(UTC)).total_seconds()
    if not math.isfinite(seconds) or seconds < 0:
        return None
    return seconds


def _percentile(values: tuple[float, ...], quantile: float) -> float | None:
    if not values:
        return None
    ordered = sorted(values)
    index = math.ceil(quantile * len(ordered)) - 1
    return round(ordered[max(index, 0)], 3)


def _validate_index(index: RawHourIndex) -> None:
    if not isinstance(index.pages, int) or isinstance(index.pages, bool) or index.pages < 1:
        raise SourceContractError("raw_index_invalid", "Raw Hour page count is invalid")
    if any(not isinstance(item, BundleRef) for item in index.items):
        raise SourceContractError("raw_index_invalid", "Raw Hour index item is invalid")
    if tuple(sorted(index.items, key=lambda item: item.cursor)) != index.items:
        raise SourceContractError("raw_index_invalid", "Raw Hour index is not ordered")
    if len({item.bundle_id for item in index.items}) != len(index.items):
        raise SourceContractError("raw_index_invalid", "Raw Hour index is not deduplicated")
    expected = raw_commit_sha256(index.items)
    if expected != index.raw_commit_sha256:
        raise SourceContractError("raw_index_invalid", "Raw Hour index digest differs")


def _stable_ref(item: BundleRef) -> tuple[object, ...]:
    return item.bundle_id, item.available_at_ms, item.sha256, item.bytes


def _identity(item: BundleRef) -> dict[str, object]:
    value: dict[str, object] = {
        "available_at_ms": item.available_at_ms,
        "bundle_id": item.bundle_id,
    }
    if item.bytes is not None:
        value["bytes"] = item.bytes
    if item.sha256 is not None:
        value["sha256"] = item.sha256
    return value


def _canonical_json(value: object) -> bytes:
    return (
        json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False) + "\n"
    ).encode("utf-8")


def _response_error_details(response: httpx.Response) -> tuple[str | None, bool]:
    try:
        payload = response.json()
    except ValueError:
        return None, False
    error = payload.get("error") if isinstance(payload, dict) else None
    if not isinstance(error, dict):
        return None, False
    code = error.get("code")
    return (code if isinstance(code, str) else None), error.get("retryable") is True


def _mapping(value: object, field: str) -> Mapping[str, Any]:
    if not isinstance(value, dict):
        raise SourceContractError("source_response_invalid", f"{field} must be an object")
    return value


def _integer(value: object, field: str) -> int:
    if not isinstance(value, int) or isinstance(value, bool) or value < 0:
        raise SourceContractError(
            "source_response_invalid", f"{field} must be a non-negative integer"
        )
    return value
