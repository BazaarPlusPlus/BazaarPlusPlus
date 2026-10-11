import { apiError, invalidBundle } from "../errors";
import { MAX_MANIFEST_BYTES } from "../limits";

export const BUNDLE_PREFIX_BYTES = 16;
export const BUNDLE_VERSION = 6;
export const RUN_FORMAT_VERSION = 6;
export const BUNDLE_CONTENT_TYPE = "application/x-bpp-bundle-v6";
export const RUN_CONTENT_TYPE = "application/x-bpp-run-v6";

const BUNDLE_MAGIC = "BPPBNDL6";

export interface BundlePrefix {
  manifestLength: number;
}

export function parseBundlePrefix(bytes: Uint8Array): BundlePrefix {
  if (bytes.byteLength !== BUNDLE_PREFIX_BYTES) {
    throw invalidBundle("invalid_prefix", "Bundle prefix is incomplete");
  }

  let magic: string;
  try {
    magic = new TextDecoder("utf-8", { fatal: true, ignoreBOM: false }).decode(
      bytes.subarray(0, 8),
    );
  } catch {
    throw invalidBundle("invalid_prefix", "Bundle magic is invalid");
  }
  if (magic !== BUNDLE_MAGIC) {
    throw invalidBundle("invalid_prefix", "Bundle magic is invalid");
  }

  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const version = view.getUint32(8, false);
  if (version !== BUNDLE_VERSION) {
    throw apiError("unsupported_bundle_version", "Bundle version is not supported", {
      details: { version },
    });
  }

  const manifestLength = view.getUint32(12, false);
  if (manifestLength === 0 || manifestLength > MAX_MANIFEST_BYTES) {
    throw invalidBundle("manifest_too_large", "Manifest length is outside the accepted range");
  }
  return { manifestLength };
}
