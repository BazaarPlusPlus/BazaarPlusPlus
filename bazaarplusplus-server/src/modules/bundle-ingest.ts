import { toHex } from "../bundle/hex";
import { type OpenedBundle, openBundle } from "../bundle/open";
import type { Env } from "../env";
import { HttpError } from "../errors";
import type { HandlerDeps } from "../http/deps";
import { MAX_BUNDLE_BYTES } from "../limits";
import { logError, logEvent } from "../observability";
import {
  type BundleReceipt,
  type CommitOutcome,
  commitBundle,
  inspectExistingBundle,
} from "./bundle-commit";

function parseContentDigest(value: string | null): string {
  const match = /^sha-256=:([A-Za-z0-9+/]{43}=):$/.exec(value ?? "");
  if (match === null) {
    throw new HttpError(
      400,
      "invalid_content_digest",
      "Content-Digest must contain exactly one sha-256 value",
      false,
    );
  }
  try {
    const binary = atob(match[1]);
    if (binary.length !== 32) throw new Error("wrong digest length");
    return toHex(Uint8Array.from(binary, (character) => character.charCodeAt(0)));
  } catch {
    throw new HttpError(400, "invalid_content_digest", "Content-Digest is invalid", false);
  }
}

function parseContentLength(value: string | null): number {
  if (value === null) {
    throw new HttpError(411, "content_length_required", "Content-Length is required", false);
  }
  if (!/^[1-9][0-9]*$/.test(value)) {
    throw new HttpError(400, "invalid_content_length", "Content-Length is invalid", false);
  }
  const length = Number(value);
  if (!Number.isSafeInteger(length)) {
    throw new HttpError(400, "invalid_content_length", "Content-Length is invalid", false);
  }
  if (length >= MAX_BUNDLE_BYTES + 1) {
    throw new HttpError(413, "bundle_too_large", "Bundle reaches the 8 MiB limit", false);
  }
  return length;
}

async function validateExistingObject(
  env: Env,
  key: string,
  incomingDigest: string,
  incomingBundleId: string,
): Promise<number> {
  const object = await env.BUNDLE_BUCKET.get(key);
  if (object === null) {
    throw new HttpError(
      503,
      "storage_unavailable",
      "Conditional R2 conflict object disappeared",
      true,
    );
  }
  try {
    const opened = await openBundle(object.body, object.size, null);
    await opened.body.pipeTo(new WritableStream<Uint8Array>());
    const digest = await opened.digest;
    if (opened.descriptor.bundleId !== incomingBundleId || digest !== incomingDigest) {
      throw new HttpError(
        409,
        "bundle_id_conflict",
        "Bundle ID already has different bytes",
        false,
      );
    }
    return object.uploaded.getTime();
  } catch (error) {
    if (error instanceof HttpError && error.code === "bundle_id_conflict") throw error;
    logError("bundle.orphan.invalid", { object_key: key, reason: "existing_object_invalid" });
    throw new HttpError(503, "storage_unavailable", "Existing Bundle object is invalid", true);
  }
}

async function putConditionally(
  env: Env,
  opened: OpenedBundle,
  digest: string,
): Promise<{ created: boolean; storedAtMs: number }> {
  const descriptor = opened.descriptor;
  const fixed = new FixedLengthStream(descriptor.objectBytes);
  const pump = opened.body.pipeTo(fixed.writable);
  void pump.catch(() => undefined);
  let object: R2Object | null;
  try {
    object = await env.BUNDLE_BUCKET.put(descriptor.objectKey, fixed.readable, {
      onlyIf: { etagDoesNotMatch: "*" },
      httpMetadata: { contentType: "application/x-bpp-bundle-v5" },
      customMetadata: {
        bundle_version: "5",
        manifest_length: String(descriptor.manifestBytes),
        bundle_sha256: digest,
      },
    });
  } catch (putError) {
    await fixed.readable.cancel(putError).catch(() => undefined);
    const pumpError = await pump.then(
      () => null,
      (error: unknown) => error,
    );
    if (pumpError instanceof HttpError) {
      const candidate = await env.BUNDLE_BUCKET.head(descriptor.objectKey).catch(() => null);
      if (candidate?.customMetadata?.bundle_sha256 === digest) {
        await env.BUNDLE_BUCKET.delete(descriptor.objectKey).catch(() => undefined);
      }
      throw pumpError;
    }
    throw new HttpError(503, "storage_unavailable", "R2 Bundle write failed", true);
  }

  if (object === null) {
    await fixed.readable.cancel("conditional R2 conflict").catch(() => undefined);
    await pump.catch(() => undefined);
    const storedAtMs = await validateExistingObject(
      env,
      descriptor.objectKey,
      digest,
      descriptor.bundleId,
    );
    return { created: false, storedAtMs };
  }

  try {
    await pump;
    await opened.digest;
    return { created: true, storedAtMs: object.uploaded.getTime() };
  } catch (error) {
    await env.BUNDLE_BUCKET.delete(descriptor.objectKey).catch(() => undefined);
    if (error instanceof HttpError) throw error;
    throw new HttpError(503, "storage_unavailable", "R2 Bundle streaming failed", true);
  }
}

function conflictError(reason: Extract<CommitOutcome, { kind: "conflict" }>["reason"]): HttpError {
  return reason === "bundle_id_conflict"
    ? new HttpError(409, reason, "Bundle ID already has different bytes", false)
    : new HttpError(409, reason, "Run already belongs to another Bundle", false);
}

export async function ingestBundle(
  request: Request,
  env: Env,
  requestId: string,
  deps: HandlerDeps,
): Promise<{ status: 200 | 201; receipt: BundleReceipt }> {
  if (request.headers.get("Content-Type") !== "application/x-bpp-bundle-v5") {
    throw new HttpError(
      415,
      "unsupported_content_type",
      "Bundle content type is unsupported",
      false,
    );
  }
  const contentLength = parseContentLength(request.headers.get("Content-Length"));
  const digest = parseContentDigest(request.headers.get("Content-Digest"));
  if (request.body === null) {
    throw new HttpError(400, "invalid_content_length", "Bundle body is missing", false);
  }
  const opened = await openBundle(request.body, contentLength, digest);
  const { descriptor } = opened;
  let existing: CommitOutcome | null;
  try {
    existing = await inspectExistingBundle(env.DB, descriptor, digest);
  } catch (error) {
    await opened.body.cancel(error).catch(() => undefined);
    throw new HttpError(503, "storage_unavailable", "Bundle index is unavailable", true);
  }
  if (existing?.kind === "duplicate") {
    await opened.body.cancel("duplicate Bundle").catch(() => undefined);
    return { status: 200, receipt: existing.receipt };
  }
  if (existing?.kind === "conflict") {
    const error = conflictError(existing.reason);
    await opened.body.cancel(error).catch(() => undefined);
    throw error;
  }

  const objectWrite = await putConditionally(env, opened, digest);
  const now = deps.now();
  let outcome: CommitOutcome;
  try {
    outcome = await commitBundle(env.DB, descriptor, digest, {
      availableAtMs: now,
      storedAtMs: objectWrite.storedAtMs,
    });
  } catch (error) {
    if (error instanceof HttpError && error.status === 503) {
      logError("bundle.ingest.orphan", {
        request_id: requestId,
        bundle_id: descriptor.bundleId,
        object_key: descriptor.objectKey,
      });
    }
    throw error;
  }
  if (outcome.kind === "duplicate") {
    return { status: 200, receipt: outcome.receipt };
  }
  if (outcome.kind === "conflict") {
    if (outcome.reason === "run_already_bundled" && objectWrite.created) {
      await env.BUNDLE_BUCKET.delete(descriptor.objectKey).catch(() => undefined);
    }
    throw conflictError(outcome.reason);
  }

  logEvent("bundle.ingest", {
    request_id: requestId,
    bundle_id: descriptor.bundleId,
    run_id: descriptor.runId,
    bytes: descriptor.objectBytes,
    has_screenshot: descriptor.screenshot !== null,
    outcome: "stored",
  });
  return {
    status: 201,
    receipt: {
      bundle_id: descriptor.bundleId,
      run_id: descriptor.runId,
      outcome: "stored",
      bazaardb_delivery: descriptor.screenshot === null ? "not_applicable" : "created",
    },
  };
}
