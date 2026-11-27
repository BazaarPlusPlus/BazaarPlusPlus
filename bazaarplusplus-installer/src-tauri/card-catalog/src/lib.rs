//! Card Art, Card Text and Enchantment FX read from an installed The Bazaar (ADR 0020,
//! 0021, 0022). The publication pipeline and the desktop app both run this crate.

mod addressables;
mod common_strings;
mod extract;
mod fx;
mod json;
mod resample;
mod text;
mod texture;
mod unity;
mod webp;

use anyhow::Result;
use sha2::{Digest, Sha256};
use std::io::Write as _;
use std::path::Path;

pub use extract::{extract, Inputs, Summary};

/// Write one immutable content-addressed file atomically without clobbering concurrent readers
/// or writers (safe while `bppcard://` serves the directory live).
pub(crate) fn write_immutable(
    output: &Path,
    prefix: &str,
    suffix: &str,
    data: &[u8],
) -> Result<String> {
    let filename = format!(
        "{prefix}{}{suffix}",
        &json::hex(&Sha256::digest(data))[..20]
    );
    let path = output.join(&filename);
    if path.exists() {
        return Ok(filename);
    }
    let mut tmp = tempfile::NamedTempFile::new_in(output)?;
    tmp.write_all(data)?;
    match tmp.persist_noclobber(&path) {
        Ok(_) => Ok(filename),
        Err(err) if err.error.kind() == std::io::ErrorKind::AlreadyExists => Ok(filename),
        Err(err) => Err(err.error.into()),
    }
}
