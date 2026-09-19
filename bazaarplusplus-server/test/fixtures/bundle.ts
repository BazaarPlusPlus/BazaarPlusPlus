import type { ValidatedBundleDescriptor } from "../../src/bundle/manifest";
import { openBundle } from "../../src/bundle/open";

const encoder = new TextEncoder();

export interface BundleFixtureOptions {
  bundleId?: string;
  runId?: string;
  uploaderAccountId?: string;
  opponentAccountId?: string;
  createdAtMs?: number;
  runBytes?: Uint8Array;
  screenshotBytes?: Uint8Array | null;
  battles?: unknown[];
}

// Deliberately local: fixtures must not import the digest/hex helpers they
// exist to verify, or a bug in src would corrupt fixtures and tests together.
function hex(bytes: Uint8Array): string {
  return [...bytes].map((byte) => byte.toString(16).padStart(2, "0")).join("");
}

function base64(bytes: Uint8Array): string {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary);
}

export async function sha256(bytes: Uint8Array): Promise<Uint8Array> {
  return new Uint8Array(await crypto.subtle.digest("SHA-256", bytes));
}

export async function sha256Hex(bytes: Uint8Array): Promise<string> {
  return hex(await sha256(bytes));
}

export function decodeBase64(value: string): Uint8Array {
  const binary = atob(value.trim());
  return Uint8Array.from(binary, (character) => character.charCodeAt(0));
}

export function stream(bytes: Uint8Array): ReadableStream<Uint8Array> {
  return new ReadableStream<Uint8Array>({
    start(controller) {
      controller.enqueue(bytes);
      controller.close();
    },
  });
}

export async function contentDigest(bytes: Uint8Array): Promise<string> {
  return `sha-256=:${base64(await sha256(bytes))}:`;
}

export async function makeBundleFixture(options: BundleFixtureOptions = {}): Promise<{
  body: Uint8Array;
  manifest: Record<string, unknown>;
  headers: Headers;
}> {
  const bundleId = options.bundleId ?? "01J00000000000000000000001";
  const runId = options.runId ?? "run-001";
  const uploader = options.uploaderAccountId ?? "account-uploader";
  const opponent = options.opponentAccountId ?? uploader;
  const createdAtMs = options.createdAtMs ?? 1_785_628_800_000;
  const run = options.runBytes ?? new Uint8Array([0x1f, 0x8b, 0x08, 0, 5, 4, 3, 2, 1]);
  const screenshot = options.screenshotBytes === undefined ? null : options.screenshotBytes;
  const battles = options.battles ?? [
    {
      battle_id: "battle-001",
      recorded_at_ms: createdAtMs - 1_000,
      day: 10,
      hour: 18,
      encounter_id: null,
      combat_kind: "pvp",
      result: "win",
      winner_combatant_id: "combatant-uploader",
      loser_combatant_id: "combatant-opponent",
      is_final_battle: true,
      player: {
        account_id: uploader,
        display_name: "Uploader",
        hero_id: null,
        hero_name: "Vanessa",
        rank: "Gold",
        rating: 1234,
        level: 10,
        prestige: 2,
        victories: 9,
      },
      opponent: {
        account_id: opponent,
        display_name: "Opponent",
        hero_id: null,
        hero_name: "Pygmalien",
        rank: "Gold",
        rating: 1200,
        level: 10,
        prestige: 3,
        victories: 8,
      },
    },
  ];
  const runHash = hex(await sha256(run));
  const manifest: Record<string, unknown> = {
    bundle_id: bundleId,
    bundle_version: 5,
    created_at_ms: createdAtMs,
    run: {
      run_id: runId,
      player_account_id: uploader,
      run_format_version: 5,
      projection: { run: {}, battles },
      payload: {
        offset: 0,
        length: run.byteLength,
        sha256: runHash,
        content_type: "application/x-bpp-run-v5",
      },
    },
  };

  if (screenshot !== null) {
    manifest.screenshot = {
      offset: run.byteLength,
      length: screenshot.byteLength,
      sha256: hex(await sha256(screenshot)),
      content_type: "image/jpeg",
      width: 1600,
      height: 900,
      quality: 80,
      captured_at_ms: createdAtMs,
    };
  }

  const sealed = await sealBundle(manifest, run, screenshot);
  return { ...sealed, manifest };
}

export async function sealBundle(
  manifest: Record<string, unknown>,
  run: Uint8Array,
  screenshot: Uint8Array | null = null,
): Promise<{ body: Uint8Array; headers: Headers }> {
  const manifestBytes = encoder.encode(JSON.stringify(manifest));
  const prefix = new Uint8Array(16);
  prefix.set(encoder.encode("BPPBNDL5"), 0);
  const view = new DataView(prefix.buffer);
  view.setUint32(8, 5, false);
  view.setUint32(12, manifestBytes.byteLength, false);

  const body = new Uint8Array(
    prefix.byteLength + manifestBytes.byteLength + run.byteLength + (screenshot?.byteLength ?? 0),
  );
  body.set(prefix, 0);
  body.set(manifestBytes, prefix.byteLength);
  body.set(run, prefix.byteLength + manifestBytes.byteLength);
  if (screenshot !== null) {
    body.set(screenshot, prefix.byteLength + manifestBytes.byteLength + run.byteLength);
  }

  return {
    body,
    headers: new Headers({
      "Content-Type": "application/x-bpp-bundle-v5",
      "Content-Length": String(body.byteLength),
      "Content-Digest": await contentDigest(body),
    }),
  };
}

export function uploadRequest(body: Uint8Array, headers: Headers): Request {
  return new Request("https://mod-api-v5.bazaarplusplus.com/bundles", {
    method: "POST",
    headers,
    body,
  });
}

export async function bundleData(options: BundleFixtureOptions): Promise<{
  descriptor: ValidatedBundleDescriptor;
  digest: string;
}> {
  const fixture = await makeBundleFixture(options);
  const opened = await openBundle(stream(fixture.body), fixture.body.byteLength, null);
  await opened.body.pipeTo(new WritableStream<Uint8Array>());
  return { descriptor: opened.descriptor, digest: await opened.digest };
}
