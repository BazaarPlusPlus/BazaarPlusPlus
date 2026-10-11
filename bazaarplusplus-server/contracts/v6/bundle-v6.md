# Bundle V6 binary contract

All integer fields in the fixed prefix are unsigned, big-endian values. Offsets in the manifest are measured from the first byte after the manifest.

| Offset | Bytes | Field | Value |
|---:|---:|---|---|
| 0 | 8 | magic | ASCII `BPPBNDL6` |
| 8 | 4 | bundle version | `6` as u32 big-endian |
| 12 | 4 | manifest length | UTF-8 JSON byte length as u32 big-endian |

The manifest is followed immediately by exactly one Run segment and zero or one Screenshot segment. The Run has offset `0`. When present, the Screenshot offset equals the Run length. No gaps, overlap, or undeclared trailing bytes are allowed.

The manifest fields and bounds are defined by [`manifest.schema.json`](./manifest.schema.json). Unknown fields are ignored. Duplicate `battle_id` values within one manifest make the Bundle invalid. `Content-Digest` covers the complete prefix, manifest, and segments; each segment digest is the lowercase SHA-256 hex value of that segment alone. Uploads use the media type `application/x-bpp-bundle-v6`.

Every time in a V6 Bundle is a Unix millisecond integer. The manifest already was in V5 (`created_at_ms`, `recorded_at_ms`, `captured_at_ms`), so its layout is unchanged apart from `bundle_version: 6`, `run_format_version: 6`, and the Run media type `application/x-bpp-run-v6`. The Run segment is defined by [`run-payload-v6.md`](../../../bazaarplusplus-mod/docs/contracts/run-payload-v6.md).

A V6 reader rejects V5 Bundles (`unsupported_bundle_version` or `invalid_prefix`); [`../v5/`](../v5/) stays as the frozen contract for V5 data still served by the V5 Worker.
