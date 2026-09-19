import { HttpError, invalidBundle } from "../errors";
import {
  MAX_BATTLES_PER_BUNDLE,
  MAX_BUNDLE_BYTES,
  MAX_PROJECTION_BYTES,
  MAX_RUN_BYTES,
  MAX_SCREENSHOT_BYTES,
} from "../limits";
import { toHex } from "./hex";
import {
  type CombatantProjection,
  type ValidatedBattleProjection,
  type ValidatedBundleDescriptor,
  validBundleId,
  validIdentifier,
} from "./manifest";
import { BUNDLE_PREFIX_BYTES, BUNDLE_VERSION, parseBundlePrefix } from "./prefix";

const STREAM_CHUNK_BYTES = 64 * 1024;
const encoder = new TextEncoder();
const SHA256 = /^[0-9a-f]{64}$/;

type JsonObject = Record<string, unknown>;

export interface OpenedBundle {
  descriptor: ValidatedBundleDescriptor;
  body: ReadableStream<Uint8Array>;
  digest: Promise<string>;
}

interface BoundedBodyReader {
  readExactly(length: number): Promise<Uint8Array>;
  remainder(): ReadableStream<Uint8Array>;
  cancel(reason?: unknown): Promise<void>;
}

interface SegmentDigest {
  start: number;
  end: number;
  expected: string;
  writer: WritableStreamDefaultWriter<ArrayBuffer | ArrayBufferView>;
  digest: Promise<ArrayBuffer>;
}

function createBodyReader(body: ReadableStream<Uint8Array>): BoundedBodyReader {
  try {
    const reader = body.getReader({ mode: "byob" });
    return {
      async readExactly(length) {
        const output = new Uint8Array(length);
        let offset = 0;
        while (offset < length) {
          const requested = new Uint8Array(Math.min(STREAM_CHUNK_BYTES, length - offset));
          const result = await reader.read(requested);
          if (result.done || result.value.byteLength === 0) {
            throw new HttpError(
              400,
              "invalid_content_length",
              "Bundle body ended before Content-Length",
              false,
            );
          }
          output.set(result.value, offset);
          offset += result.value.byteLength;
        }
        return output;
      },
      remainder() {
        return new ReadableStream<Uint8Array>({
          async pull(controller) {
            const result = await reader.read(new Uint8Array(STREAM_CHUNK_BYTES));
            if (result.done) controller.close();
            else controller.enqueue(result.value);
          },
          async cancel(reason) {
            await reader.cancel(reason);
          },
        });
      },
      async cancel(reason) {
        await reader.cancel(reason);
      },
    };
  } catch {
    const reader = body.getReader();
    let pending: Uint8Array | null = null;
    return {
      async readExactly(length) {
        const output = new Uint8Array(length);
        let offset = 0;
        while (offset < length) {
          const result = pending === null ? await reader.read() : { done: false, value: pending };
          pending = null;
          if (result.done) {
            throw new HttpError(
              400,
              "invalid_content_length",
              "Bundle body ended before Content-Length",
              false,
            );
          }
          const needed = length - offset;
          output.set(result.value.subarray(0, needed), offset);
          offset += Math.min(needed, result.value.byteLength);
          if (result.value.byteLength > needed) pending = result.value.subarray(needed);
        }
        return output;
      },
      remainder() {
        return new ReadableStream<Uint8Array>({
          async pull(controller) {
            if (pending !== null) {
              const value = pending;
              pending = null;
              controller.enqueue(value);
              return;
            }
            const result = await reader.read();
            if (result.done) controller.close();
            else controller.enqueue(result.value);
          },
          async cancel(reason) {
            await reader.cancel(reason);
          },
        });
      },
      async cancel(reason) {
        await reader.cancel(reason);
      },
    };
  }
}

function streamWithPrelude(
  prefix: Uint8Array,
  manifest: Uint8Array,
  remainder: ReadableStream<Uint8Array>,
): ReadableStream<Uint8Array> {
  const reader = remainder.getReader();
  const prelude = [prefix, manifest];
  return new ReadableStream<Uint8Array>({
    async pull(controller) {
      const next = prelude.shift();
      if (next !== undefined) {
        controller.enqueue(next);
        return;
      }
      const result = await reader.read();
      if (result.done) controller.close();
      else controller.enqueue(result.value);
    },
    async cancel(reason) {
      await reader.cancel(reason);
    },
  });
}

function object(value: unknown, reason = "manifest_schema_invalid"): JsonObject {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    throw invalidBundle(reason, "Manifest field must be an object");
  }
  return value as JsonObject;
}

function safeInteger(value: unknown, field: string): number {
  if (!Number.isSafeInteger(value) || (value as number) < 0) {
    throw invalidBundle("manifest_schema_invalid", `${field} must be a non-negative safe integer`);
  }
  return value as number;
}

function identifier(value: unknown, field: string): string {
  if (typeof value !== "string" || !validIdentifier(value)) {
    throw invalidBundle("manifest_schema_invalid", `${field} is invalid`);
  }
  return value;
}

function nullableString(value: unknown, field: string): string | null {
  if (value === null) return null;
  if (typeof value !== "string" || value.length > 256) {
    throw invalidBundle("manifest_schema_invalid", `${field} must be a string or null`);
  }
  return value;
}

function nullableSafeInteger(value: unknown, field: string): number | null {
  if (value === null) return null;
  return safeInteger(value, field);
}

function combatant(value: unknown, field: string): CombatantProjection {
  const source = object(value);
  if (typeof source.display_name !== "string" || source.display_name.length > 256) {
    throw invalidBundle("manifest_schema_invalid", `${field}.display_name is invalid`);
  }
  return {
    account_id: identifier(source.account_id, `${field}.account_id`),
    display_name: source.display_name,
    hero_id: nullableString(source.hero_id, `${field}.hero_id`),
    hero_name: nullableString(source.hero_name, `${field}.hero_name`),
    rank: nullableString(source.rank, `${field}.rank`),
    rating: nullableSafeInteger(source.rating, `${field}.rating`),
    level: nullableSafeInteger(source.level, `${field}.level`),
    prestige: nullableSafeInteger(source.prestige, `${field}.prestige`),
    victories: nullableSafeInteger(source.victories, `${field}.victories`),
  };
}

function projectionBattle(value: unknown, uploader: string): ValidatedBattleProjection {
  const source = object(value);
  const player = combatant(source.player, "battle.player");
  const opponent = combatant(source.opponent, "battle.opponent");
  if (player.account_id !== uploader) {
    throw invalidBundle("manifest_schema_invalid", "Battle player identity differs from uploader");
  }
  if (typeof source.is_final_battle !== "boolean") {
    throw invalidBundle("manifest_schema_invalid", "battle.is_final_battle must be boolean");
  }
  return {
    battle_id: identifier(source.battle_id, "battle.battle_id"),
    recorded_at_ms: safeInteger(source.recorded_at_ms, "battle.recorded_at_ms"),
    day: safeInteger(source.day, "battle.day"),
    hour: safeInteger(source.hour, "battle.hour"),
    encounter_id: nullableString(source.encounter_id, "battle.encounter_id"),
    combat_kind: identifier(source.combat_kind, "battle.combat_kind"),
    result: identifier(source.result, "battle.result"),
    winner_combatant_id: nullableString(source.winner_combatant_id, "battle.winner_combatant_id"),
    loser_combatant_id: nullableString(source.loser_combatant_id, "battle.loser_combatant_id"),
    is_final_battle: source.is_final_battle,
    player,
    opponent,
  };
}

function parseJson(bytes: Uint8Array): JsonObject {
  let text: string;
  try {
    text = new TextDecoder("utf-8", { fatal: true, ignoreBOM: false }).decode(bytes);
  } catch {
    throw invalidBundle("manifest_not_json", "Manifest is not valid UTF-8 JSON");
  }
  let depth = 0;
  let inString = false;
  let escaped = false;
  for (const character of text) {
    if (inString) {
      if (escaped) escaped = false;
      else if (character === "\\") escaped = true;
      else if (character === '"') inString = false;
    } else if (character === '"') inString = true;
    else if (character === "{" || character === "[") {
      depth += 1;
      if (depth > 64) throw invalidBundle("manifest_not_json", "Manifest nesting is too deep");
    } else if (character === "}" || character === "]") depth -= 1;
  }
  try {
    return object(JSON.parse(text), "manifest_not_json");
  } catch (error) {
    if (error instanceof HttpError) throw error;
    throw invalidBundle("manifest_not_json", "Manifest is not valid JSON");
  }
}

function validateManifest(
  bytes: Uint8Array,
  declaredObjectBytes: number,
): ValidatedBundleDescriptor {
  const root = parseJson(bytes);
  if (root.bundle_version !== BUNDLE_VERSION) {
    throw new HttpError(
      422,
      "unsupported_bundle_version",
      "Manifest Bundle version is not supported",
      false,
    );
  }
  if (typeof root.bundle_id !== "string" || !validBundleId(root.bundle_id)) {
    throw invalidBundle("manifest_schema_invalid", "bundle_id must be a canonical ULID");
  }
  const createdAtMs = safeInteger(root.created_at_ms, "created_at_ms");
  const run = object(root.run);
  if (run.run_format_version !== 5) {
    throw new HttpError(422, "unsupported_run_format", "Run format is not supported", false);
  }
  const runId = identifier(run.run_id, "run.run_id");
  const uploader = identifier(run.player_account_id, "run.player_account_id");
  const payload = object(run.payload);
  const runOffset = safeInteger(payload.offset, "run.payload.offset");
  const runLength = safeInteger(payload.length, "run.payload.length");
  if (runOffset !== 0 || runLength === 0) {
    throw invalidBundle(
      "run_missing",
      "Run segment must start at payload offset zero and be non-empty",
    );
  }
  if (runLength >= MAX_RUN_BYTES + 1) {
    throw invalidBundle("run_too_large", "Run segment reaches the 2 MiB limit");
  }
  if (payload.content_type !== "application/x-bpp-run-v5") {
    throw invalidBundle("manifest_schema_invalid", "Run content type is invalid");
  }
  if (typeof payload.sha256 !== "string" || !SHA256.test(payload.sha256)) {
    throw invalidBundle("manifest_schema_invalid", "Run digest is invalid");
  }

  const projection = object(run.projection);
  object(projection.run);
  if (!Array.isArray(projection.battles)) {
    throw invalidBundle("manifest_schema_invalid", "run.projection.battles must be an array");
  }
  if (projection.battles.length > MAX_BATTLES_PER_BUNDLE) {
    throw invalidBundle("too_many_battles", "Bundle contains too many Battle projections");
  }
  if (encoder.encode(JSON.stringify(projection)).byteLength > MAX_PROJECTION_BYTES) {
    throw invalidBundle("projection_too_large", "Run projection exceeds its byte limit");
  }
  const battles = projection.battles.map((battle) => projectionBattle(battle, uploader));
  const battleIds = new Set<string>();
  for (const battle of battles) {
    if (battleIds.has(battle.battle_id)) {
      throw invalidBundle("manifest_schema_invalid", "Battle IDs must be unique within a Bundle");
    }
    battleIds.add(battle.battle_id);
  }

  let screenshot: ValidatedBundleDescriptor["screenshot"] = null;
  if (Object.hasOwn(root, "screenshot")) {
    const image = object(root.screenshot);
    const offset = safeInteger(image.offset, "screenshot.offset");
    const length = safeInteger(image.length, "screenshot.length");
    if (length === 0 || length > MAX_SCREENSHOT_BYTES) {
      throw invalidBundle("screenshot_too_large", "Screenshot segment is empty or too large");
    }
    if (image.content_type !== "image/jpeg" && image.content_type !== "image/webp") {
      throw invalidBundle("screenshot_type_unsupported", "Screenshot content type is unsupported");
    }
    if (typeof image.sha256 !== "string" || !SHA256.test(image.sha256)) {
      throw invalidBundle("manifest_schema_invalid", "Screenshot digest is invalid");
    }
    const width = safeInteger(image.width, "screenshot.width");
    const height = safeInteger(image.height, "screenshot.height");
    const quality = safeInteger(image.quality, "screenshot.quality");
    if (width < 1 || height < 1 || quality < 1 || quality > 100) {
      throw invalidBundle(
        "manifest_schema_invalid",
        "Screenshot dimensions or quality are invalid",
      );
    }
    safeInteger(image.captured_at_ms, "screenshot.captured_at_ms");
    if (offset < runLength) {
      throw invalidBundle("segment_overlap", "Screenshot overlaps the Run segment");
    }
    if (offset > runLength) {
      throw invalidBundle(
        "segment_out_of_bounds",
        "Screenshot does not immediately follow the Run segment",
      );
    }
    screenshot = {
      offset,
      length,
      sha256: image.sha256,
      contentType: image.content_type,
    };
  }

  const payloadBytes = runLength + (screenshot?.length ?? 0);
  const describedBytes = BUNDLE_PREFIX_BYTES + bytes.byteLength + payloadBytes;
  if (describedBytes < declaredObjectBytes) {
    throw invalidBundle("undeclared_trailing_bytes", "Bundle has undeclared trailing bytes");
  }
  if (describedBytes > declaredObjectBytes) {
    throw invalidBundle("segment_out_of_bounds", "A segment extends beyond the Bundle body");
  }
  if (createdAtMs > 8_640_000_000_000_000) {
    throw invalidBundle(
      "manifest_schema_invalid",
      "created_at_ms is outside the supported date range",
    );
  }
  const day = new Date(createdAtMs).toISOString().slice(0, 10);
  return {
    bundleId: root.bundle_id,
    runId,
    uploaderAccountId: uploader,
    createdAtMs,
    manifestBytes: bytes.byteLength,
    objectBytes: declaredObjectBytes,
    objectKey: `bundles/${day}/${root.bundle_id}.bundle`,
    run: { offset: runOffset, length: runLength, sha256: payload.sha256 },
    screenshot,
    battles,
  };
}

function validateBundleBody(
  source: ReadableStream<Uint8Array>,
  descriptor: ValidatedBundleDescriptor,
  expectedBundleDigest: string | null,
): { body: ReadableStream<Uint8Array>; digest: Promise<string> } {
  const bundleDigest = new crypto.DigestStream("SHA-256");
  const bundleWriter = bundleDigest.getWriter();
  void bundleDigest.digest.catch(() => undefined);
  const payloadStart = BUNDLE_PREFIX_BYTES + descriptor.manifestBytes;
  const segmentDigests: SegmentDigest[] = [];
  for (const segment of [descriptor.run, descriptor.screenshot].filter(
    (value): value is NonNullable<typeof value> => value !== null,
  )) {
    const digest = new crypto.DigestStream("SHA-256");
    void digest.digest.catch(() => undefined);
    segmentDigests.push({
      start: payloadStart + segment.offset,
      end: payloadStart + segment.offset + segment.length,
      expected: segment.sha256,
      writer: digest.getWriter(),
      digest: digest.digest,
    });
  }

  let position = 0;
  let failure: Promise<void> | undefined;
  let resolveDigest!: (digest: string) => void;
  let rejectDigest!: (error: unknown) => void;
  const digestResult = new Promise<string>((resolve, reject) => {
    resolveDigest = resolve;
    rejectDigest = reject;
  });
  void digestResult.catch(() => undefined);
  const fail = (error: unknown): Promise<void> => {
    if (failure === undefined) {
      rejectDigest(error);
      failure = Promise.allSettled([
        bundleWriter.abort(error),
        ...segmentDigests.map(({ writer }) => writer.abort(error)),
      ]).then(() => undefined);
    }
    return failure;
  };
  const transform = new TransformStream<Uint8Array, Uint8Array>({
    async transform(chunk, controller) {
      try {
        const nextPosition = position + chunk.byteLength;
        if (nextPosition > descriptor.objectBytes) {
          throw new HttpError(
            nextPosition >= MAX_BUNDLE_BYTES ? 413 : 400,
            nextPosition >= MAX_BUNDLE_BYTES ? "bundle_too_large" : "invalid_content_length",
            "Actual Bundle length exceeds Content-Length",
            false,
          );
        }
        await bundleWriter.write(chunk);
        for (const segment of segmentDigests) {
          const start = Math.max(position, segment.start);
          const end = Math.min(nextPosition, segment.end);
          if (start < end) {
            await segment.writer.write(chunk.subarray(start - position, end - position));
          }
        }
        position = nextPosition;
        controller.enqueue(chunk);
      } catch (error) {
        await fail(error);
        throw error;
      }
    },
    async flush() {
      try {
        if (position !== descriptor.objectBytes) {
          throw new HttpError(
            400,
            "invalid_content_length",
            "Actual Bundle length differs from Content-Length",
            false,
          );
        }
        await Promise.all([
          bundleWriter.close(),
          ...segmentDigests.map(({ writer }) => writer.close()),
        ]);
        const segmentHashes = await Promise.all(
          segmentDigests.map(({ digest }) => digest.then(toHex)),
        );
        if (segmentHashes.some((digest, index) => digest !== segmentDigests[index].expected)) {
          throw new HttpError(
            422,
            "segment_digest_mismatch",
            "A Bundle segment digest does not match its manifest",
            false,
          );
        }
        const actual = toHex(await bundleDigest.digest);
        if (expectedBundleDigest !== null && actual !== expectedBundleDigest) {
          throw new HttpError(
            422,
            "bundle_digest_mismatch",
            "Bundle digest does not match Content-Digest",
            false,
          );
        }
        resolveDigest(actual);
      } catch (error) {
        await fail(error);
        throw error;
      }
    },
    cancel(reason) {
      return fail(reason);
    },
  });

  return { body: source.pipeThrough(transform), digest: digestResult };
}

export async function openBundle(
  source: ReadableStream<Uint8Array>,
  contentLength: number,
  expectedDigest: string | null,
): Promise<OpenedBundle> {
  const reader = createBodyReader(source);
  try {
    const prefix = await reader.readExactly(BUNDLE_PREFIX_BYTES);
    const { manifestLength } = parseBundlePrefix(prefix);
    if (BUNDLE_PREFIX_BYTES + manifestLength >= contentLength) {
      throw invalidBundle("run_missing", "Bundle Run segment is missing");
    }
    const manifest = await reader.readExactly(manifestLength);
    const descriptor = validateManifest(manifest, contentLength);
    const validation = validateBundleBody(
      streamWithPrelude(prefix, manifest, reader.remainder()),
      descriptor,
      expectedDigest,
    );
    return { descriptor, ...validation };
  } catch (error) {
    await reader.cancel(error).catch(() => undefined);
    throw error;
  }
}
