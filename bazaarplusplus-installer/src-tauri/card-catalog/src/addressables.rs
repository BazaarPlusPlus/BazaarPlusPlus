//! The Addressables binary catalog (`StreamingAssets/aa/catalog.bin`): asset key → internal path.

use anyhow::{bail, Result};
use std::collections::HashMap;

const NONE: u32 = 0xFFFF_FFFF;

struct Catalog<'a> {
    b: &'a [u8],
    version: u32,
}

impl Catalog<'_> {
    fn u32(&self, o: u32) -> Option<u32> {
        let o = o as usize;
        Some(u32::from_le_bytes(self.b.get(o..o + 4)?.try_into().ok()?))
    }
    fn offsets(&self, o: u32) -> Option<Vec<u32>> {
        if o == NONE {
            return Some(vec![]);
        }
        let n = self.u32(o.checked_sub(4)?)? / 4;
        (0..n).map(|i| self.u32(o + 4 * i)).collect()
    }
    fn string(&self, encoded: u32, sep: Option<char>) -> Option<String> {
        if encoded == NONE {
            return None;
        }
        let unicode = encoded & 0x8000_0000 != 0;
        let mut offset = encoded & 0x3FFF_FFFF;
        let Some(sep) = sep.filter(|_| encoded & 0x4000_0000 != 0) else {
            let len = self.u32(offset.checked_sub(4)?)? as usize;
            let raw = self.b.get(offset as usize..offset as usize + len)?;
            return Some(if unicode {
                let units: Vec<u16> = raw
                    .as_chunks::<2>()
                    .0
                    .iter()
                    .map(|c| u16::from_le_bytes(*c))
                    .collect();
                String::from_utf16_lossy(&units)
            } else {
                String::from_utf8_lossy(raw).into_owned()
            });
        };
        let mut parts = Vec::new();
        loop {
            let part = self.u32(offset)?;
            let following = self.u32(offset + 4)?;
            parts.push(self.string(part, Some(sep)).unwrap_or_default());
            if following == NONE {
                break;
            }
            offset = following;
        }
        if self.version > 1 {
            parts.reverse();
        }
        Some(parts.join(&sep.to_string()))
    }
    fn key(&self, o: u32) -> Option<String> {
        let type_offset = self.u32(o)?;
        let object_offset = self.u32(o + 4)?;
        if self.string(self.u32(type_offset + 4)?, Some('.'))? != "System.String" {
            return None;
        }
        let encoded = self.u32(object_offset)?;
        let sep = char::from_u32(self.u32(object_offset + 4)?)?;
        self.string(encoded, Some(sep))
    }
}

/// Each string key's first location, as the path the asset bundles' containers use.
pub fn paths(bytes: &[u8]) -> Result<HashMap<String, String>> {
    let head = |i: usize| {
        bytes
            .get(i..i + 4)
            .map(|b| u32::from_le_bytes([b[0], b[1], b[2], b[3]]))
    };
    let (Some(magic), Some(version), Some(keys)) = (head(0), head(4), head(8)) else {
        bail!("truncated Addressables catalog");
    };
    if magic != 0x0DE3_8942 || !(1..=2).contains(&version) {
        bail!("not an Addressables binary catalog");
    }
    let catalog = Catalog { b: bytes, version };
    let mut out = HashMap::new();
    let offsets = catalog.offsets(keys).unwrap_or_default();
    for pair in offsets.as_chunks::<2>().0 {
        let Some(key) = catalog.key(pair[0]) else {
            continue;
        };
        let Some(&first) = catalog.offsets(pair[1]).as_deref().and_then(<[u32]>::first) else {
            continue;
        };
        let Some(path) = catalog
            .u32(first + 4)
            .and_then(|internal| catalog.string(internal, Some('/')))
        else {
            continue;
        };
        out.entry(key).or_insert(path);
    }
    Ok(out)
}
