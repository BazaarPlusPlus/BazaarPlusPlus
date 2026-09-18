# Bundle V5 binary contract

All integer fields in the fixed prefix are unsigned, big-endian values. Offsets in the manifest are measured from the first byte after the manifest.

| Offset | Bytes | Field | Value |
|---:|---:|---|---|
| 0 | 8 | magic | ASCII `BPPBNDL5` |
| 8 | 4 | bundle version | `5` as u32 big-endian |
| 12 | 4 | manifest length | UTF-8 JSON byte length as u32 big-endian |

The manifest is followed immediately by exactly one Run segment and zero or one Screenshot segment. The Run has offset `0`. When present, the Screenshot offset equals the Run length. No gaps, overlap, or undeclared trailing bytes are allowed.

The manifest fields and bounds are defined by [`manifest.schema.json`](./manifest.schema.json). Unknown fields are ignored. `Content-Digest` covers the complete prefix, manifest, and segments; each segment digest is the lowercase SHA-256 hex value of that segment alone.
