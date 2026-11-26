//! Card Art, Card Text and Enchantment FX read from an installed The Bazaar (ADR 0020,
//! 0021, 0022). The publication pipeline and the desktop app both run this crate.

pub mod addressables;
mod common_strings;
pub mod extract;
pub mod fx;
pub mod json;
pub mod resample;
pub mod text;
pub mod texture;
pub mod unity;
pub mod webp;

pub use extract::{extract, Inputs, Summary};
