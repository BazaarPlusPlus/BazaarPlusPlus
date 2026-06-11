mod launch_options;
mod parse;

#[cfg(test)]
mod tests;

pub use launch_options::{clear_launch_options_for_steam, patch_launch_options};

#[cfg(target_os = "macos")]
pub(crate) use launch_options::ensure_launcher_executable;
