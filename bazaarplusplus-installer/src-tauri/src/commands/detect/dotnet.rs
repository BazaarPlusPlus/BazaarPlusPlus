use std::collections::HashSet;
use std::path::{Path, PathBuf};

fn parse_version_tuple(v: &str) -> (u32, u32, u32) {
    let mut parts = v.split('.').filter_map(|p| p.parse::<u32>().ok());
    (
        parts.next().unwrap_or(0),
        parts.next().unwrap_or(0),
        parts.next().unwrap_or(0),
    )
}

fn is_supported_dotnet_version(version: &str) -> bool {
    version
        .split('.')
        .next()
        .and_then(|major| major.parse::<u32>().ok())
        .map(|major| major >= 6)
        .unwrap_or(false)
}

fn is_version_like(name: &str) -> bool {
    let mut any = false;
    for part in name.split('.') {
        if part.is_empty() || part.chars().any(|c| !c.is_ascii_digit()) {
            return false;
        }
        any = true;
    }
    any
}

/// Scans `<root>/shared/Microsoft.NETCore.App/<version>` directories across the
/// given install roots and returns the highest supported version name. Pure
/// filesystem reads — no subprocess — so it avoids the expensive
/// `CreateProcess` path (and Windows Defender hook) that `dotnet --list-runtimes`
/// triggers on Windows.
fn detect_dotnet_from_install_roots<I, P>(roots: I) -> Option<String>
where
    I: IntoIterator<Item = P>,
    P: AsRef<Path>,
{
    let mut best: Option<String> = None;

    for root in roots {
        let runtime_dir = root.as_ref().join("shared").join("Microsoft.NETCore.App");

        let Ok(entries) = std::fs::read_dir(&runtime_dir) else {
            continue;
        };

        for entry in entries.flatten() {
            let Ok(file_type) = entry.file_type() else {
                continue;
            };
            if !file_type.is_dir() {
                continue;
            }
            let Some(name) = entry.file_name().to_str().map(str::to_string) else {
                continue;
            };
            if !is_version_like(&name) || !is_supported_dotnet_version(&name) {
                continue;
            }

            let is_better = match &best {
                Some(current) => parse_version_tuple(&name) > parse_version_tuple(current),
                None => true,
            };
            if is_better {
                best = Some(name);
            }
        }
    }

    best
}

fn candidate_install_roots() -> Vec<PathBuf> {
    let mut roots: Vec<PathBuf> = Vec::new();
    let mut seen: HashSet<PathBuf> = HashSet::new();

    let push = |path: PathBuf, roots: &mut Vec<PathBuf>, seen: &mut HashSet<PathBuf>| {
        if seen.insert(path.clone()) {
            roots.push(path);
        }
    };

    if let Some(value) = std::env::var_os("DOTNET_ROOT") {
        push(PathBuf::from(value), &mut roots, &mut seen);
    }

    #[cfg(target_os = "windows")]
    {
        let env_candidates = [
            std::env::var_os("ProgramW6432")
                .map(PathBuf::from)
                .map(|p| p.join("dotnet")),
            std::env::var_os("ProgramFiles")
                .map(PathBuf::from)
                .map(|p| p.join("dotnet")),
            std::env::var_os("ProgramFiles(x86)")
                .map(PathBuf::from)
                .map(|p| p.join("dotnet")),
            std::env::var_os("LOCALAPPDATA")
                .map(PathBuf::from)
                .map(|p| p.join("Microsoft").join("dotnet")),
            Some(PathBuf::from(r"C:\Program Files\dotnet")),
            Some(PathBuf::from(r"C:\Program Files (x86)\dotnet")),
        ];
        for candidate in env_candidates.into_iter().flatten() {
            push(candidate, &mut roots, &mut seen);
        }
    }

    #[cfg(not(target_os = "windows"))]
    {
        let mut unix_candidates = vec![
            PathBuf::from("/usr/local/share/dotnet"),
            PathBuf::from("/usr/share/dotnet"),
            PathBuf::from("/opt/homebrew/share/dotnet"),
        ];
        if let Some(home) = dirs::home_dir() {
            unix_candidates.push(home.join(".dotnet"));
        }
        for candidate in unix_candidates {
            push(candidate, &mut roots, &mut seen);
        }
    }

    roots
}

pub(crate) fn detect_dotnet() -> (Option<String>, bool) {
    match detect_dotnet_from_install_roots(candidate_install_roots()) {
        Some(version) => (Some(version), true),
        None => (None, false),
    }
}

#[cfg(test)]
mod tests {
    use super::{detect_dotnet_from_install_roots, is_supported_dotnet_version, is_version_like};
    use std::path::Path;

    fn make_runtime_dir(root: &Path, version: &str) {
        let version_dir = root
            .join("shared")
            .join("Microsoft.NETCore.App")
            .join(version);
        std::fs::create_dir_all(&version_dir).unwrap();
    }

    #[test]
    fn test_is_supported_dotnet_version_requires_major_6_or_higher() {
        assert!(!is_supported_dotnet_version("5.0.17"));
        assert!(is_supported_dotnet_version("6.0.0"));
        assert!(is_supported_dotnet_version("8.0.1"));
    }

    #[test]
    fn test_is_version_like_accepts_dotted_digits_only() {
        assert!(is_version_like("8.0.1"));
        assert!(is_version_like("10"));
        assert!(!is_version_like("host"));
        assert!(!is_version_like("8.0.1-preview"));
        assert!(!is_version_like(""));
    }

    #[test]
    fn test_detect_dotnet_from_install_roots_returns_highest_supported_version() {
        let tmp = tempfile::tempdir().unwrap();
        make_runtime_dir(tmp.path(), "5.0.17");
        make_runtime_dir(tmp.path(), "6.0.25");
        make_runtime_dir(tmp.path(), "8.0.1");
        make_runtime_dir(tmp.path(), "10.0.0");

        let version = detect_dotnet_from_install_roots([tmp.path()]);

        assert_eq!(version.as_deref(), Some("10.0.0"));
    }

    #[test]
    fn test_detect_dotnet_from_install_roots_ignores_non_version_entries() {
        let tmp = tempfile::tempdir().unwrap();
        make_runtime_dir(tmp.path(), "8.0.1");
        std::fs::create_dir_all(
            tmp.path()
                .join("shared")
                .join("Microsoft.NETCore.App")
                .join("host"),
        )
        .unwrap();
        std::fs::write(
            tmp.path()
                .join("shared")
                .join("Microsoft.NETCore.App")
                .join("README.md"),
            b"",
        )
        .unwrap();

        let version = detect_dotnet_from_install_roots([tmp.path()]);

        assert_eq!(version.as_deref(), Some("8.0.1"));
    }

    #[test]
    fn test_detect_dotnet_from_install_roots_returns_none_when_only_old_versions_present() {
        let tmp = tempfile::tempdir().unwrap();
        make_runtime_dir(tmp.path(), "3.1.32");
        make_runtime_dir(tmp.path(), "5.0.17");

        let version = detect_dotnet_from_install_roots([tmp.path()]);

        assert_eq!(version, None);
    }

    #[test]
    fn test_detect_dotnet_from_install_roots_skips_missing_roots() {
        let tmp = tempfile::tempdir().unwrap();
        let missing = tmp.path().join("does-not-exist");
        make_runtime_dir(tmp.path(), "8.0.1");

        let version = detect_dotnet_from_install_roots([missing.as_path(), tmp.path()]);

        assert_eq!(version.as_deref(), Some("8.0.1"));
    }

    #[test]
    fn test_detect_dotnet_from_install_roots_picks_highest_across_multiple_roots() {
        let tmp = tempfile::tempdir().unwrap();
        let root_a = tmp.path().join("a");
        let root_b = tmp.path().join("b");
        make_runtime_dir(&root_a, "6.0.25");
        make_runtime_dir(&root_b, "8.0.1");

        let version = detect_dotnet_from_install_roots([root_a.as_path(), root_b.as_path()]);

        assert_eq!(version.as_deref(), Some("8.0.1"));
    }
}
