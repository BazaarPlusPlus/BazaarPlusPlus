use anyhow::anyhow;
use std::collections::HashSet;
use std::path::{Path, PathBuf};

/// A path the mod stored relative to one directory of the Data Root, accepted
/// by the one rule every History and overlay caller uses.
///
/// The rule reads the text only, the same way on every platform, and never
/// touches the filesystem: `/` and `\` both separate segments, each segment is
/// trimmed, and empty segments between separators are skipped. It rejects an
/// empty or blank path, a leading separator (a root, a UNC path, `\\?\`), a
/// segment that is `.` or `..`, and a segment that starts with a drive prefix
/// (`C:`). An accepted path therefore always joins to a path under its
/// directory, whatever that directory resolves to on disk.
#[derive(Clone, Debug, PartialEq, Eq, Hash)]
pub struct StoredRelativePath {
    /// The accepted segments joined with `/`: the key that compares two rows.
    key: String,
}

impl StoredRelativePath {
    pub fn parse(raw: &str) -> Option<Self> {
        let raw = raw.trim();
        if raw.is_empty() || raw.starts_with(['/', '\\']) {
            return None;
        }
        let mut segments = Vec::new();
        for segment in raw.split(['/', '\\']).map(str::trim) {
            if segment.is_empty() {
                continue;
            }
            if segment == "." || segment == ".." || has_drive_prefix(segment) {
                return None;
            }
            segments.push(segment);
        }
        (!segments.is_empty()).then(|| Self {
            key: segments.join("/"),
        })
    }

    pub fn as_str(&self) -> &str {
        &self.key
    }

    pub fn file_name(&self) -> &str {
        self.key.rsplit('/').next().unwrap_or(&self.key)
    }

    /// The file this path names under `root`.
    pub fn under(&self, root: &Path) -> PathBuf {
        let mut path = root.to_path_buf();
        path.extend(self.key.split('/'));
        path
    }
}

/// `C:` and `C:x`: on Windows such a segment replaces the path it is joined to.
fn has_drive_prefix(segment: &str) -> bool {
    let bytes = segment.as_bytes();
    bytes.len() >= 2 && bytes[0].is_ascii_alphabetic() && bytes[1] == b':'
}

/// The file `raw` names under `root`, or `None` when the rule rejects it.
pub fn resolve_stored_path(root: &Path, raw: &str) -> Option<PathBuf> {
    StoredRelativePath::parse(raw).map(|path| path.under(root))
}

pub fn resolve_screenshot_path(data_root: &Path, raw: &str) -> Option<PathBuf> {
    resolve_stored_path(&crate::services::paths::screenshots_dir_in(data_root), raw)
}

/// The stored paths of rows that keep their files: a candidate file this set
/// protects is never deleted.
///
/// A row the rule rejects names no file History can trust, yet before the rule
/// such a row kept the file its segments named once `.` and `..` were dropped.
/// So that the rule never deletes a file the old resolvers kept, a rejected row
/// protects every candidate with its last such segment as file name.
#[derive(Debug, Default)]
pub struct StoredPathReferences {
    accepted: HashSet<String>,
    rejected_file_names: HashSet<String>,
}

impl StoredPathReferences {
    pub fn insert(&mut self, raw: &str) {
        if let Some(path) = StoredRelativePath::parse(raw) {
            self.accepted.insert(path.key);
            return;
        }
        let last_name = raw
            .split(['/', '\\'])
            .map(str::trim)
            .rfind(|segment| !segment.is_empty() && *segment != "." && *segment != "..");
        if let Some(name) = last_name {
            self.rejected_file_names.insert(name.to_string());
        }
    }

    pub fn protects(&self, candidate: &StoredRelativePath) -> bool {
        self.accepted.contains(candidate.as_str())
            || self.rejected_file_names.contains(candidate.file_name())
    }
}

/// Deletes the video file a row names. A path the rule rejects deletes nothing.
pub fn remove_video_file(root_dir: &Path, relative_path: &str) -> anyhow::Result<()> {
    let Some(path) = resolve_stored_path(root_dir, relative_path) else {
        return Ok(());
    };
    match std::fs::remove_file(&path) {
        Ok(()) => Ok(()),
        Err(err) if err.kind() == std::io::ErrorKind::NotFound => Ok(()),
        Err(err) => Err(anyhow!("failed to remove video {}: {err}", path.display())),
    }
}

#[cfg(test)]
mod tests {
    use super::{remove_video_file, StoredPathReferences, StoredRelativePath};

    #[test]
    fn the_rule_accepts_relative_segments_and_rejects_everything_else() {
        for (raw, expected) in [
            ("a/b.png", Some("a/b.png")),
            (r"a\b.png", Some("a/b.png")),
            (r"2026-06-15\shot.png", Some("2026-06-15/shot.png")),
            ("b.png", Some("b.png")),
            ("  a / b.png  ", Some("a/b.png")),
            ("a//b.png", Some("a/b.png")),
            ("a/b.png/", Some("a/b.png")),
            ("a..b/c...png", Some("a..b/c...png")),
            ("a/.b.png", Some("a/.b.png")),
            ("a/bc:d.png", Some("a/bc:d.png")),
            ("1:x.png", Some("1:x.png")),
            ("", None),
            ("   ", None),
            (".", None),
            ("..", None),
            ("./b.png", None),
            ("a/./b.png", None),
            ("a/../b.png", None),
            (r"a\..\b.png", None),
            ("a/ .. /b.png", None),
            ("a/..", None),
            ("/x", None),
            (r"\x", None),
            ("/", None),
            (r"\\server\share\x", None),
            ("//server/share/x", None),
            (r"\\?\C:\x", None),
            ("C:", None),
            ("C:x", None),
            (r"C:\x", None),
            ("c:/x", None),
            (r"a\C:x.png", None),
            ("a/z:", None),
        ] {
            assert_eq!(
                StoredRelativePath::parse(raw)
                    .as_ref()
                    .map(StoredRelativePath::as_str),
                expected,
                "`{raw}`"
            );
        }
    }

    #[test]
    fn an_accepted_path_joins_under_its_root_on_every_platform() {
        let root = std::path::Path::new("root");
        let path = StoredRelativePath::parse(r"2026-06-15\shot.png").unwrap();
        assert_eq!(path.file_name(), "shot.png");
        assert_eq!(path.under(root), root.join("2026-06-15").join("shot.png"));
        assert!(path.under(root).starts_with(root));
    }

    #[test]
    fn a_rejected_reference_protects_every_candidate_with_its_file_name() {
        let mut references = StoredPathReferences::default();
        for raw in ["a/b.png", "x/../y/c.png", r"\\server\share\d.png", "..", ""] {
            references.insert(raw);
        }
        let protects = |raw| references.protects(&StoredRelativePath::parse(raw).unwrap());

        assert!(protects(r"a\b.png"));
        assert!(!protects("b.png"));
        assert!(protects("x/y/c.png"));
        assert!(protects("c.png"));
        assert!(protects("server/share/d.png"));
        assert!(protects("2020-01-01/d.png"));
        assert!(!protects("x/y/e.png"));
    }

    #[test]
    fn video_removal_never_deletes_outside_the_video_directory() {
        let temp = tempfile::tempdir().unwrap();
        let video_dir = temp.path().join("videos");
        std::fs::create_dir_all(video_dir.join("2026-05-20")).unwrap();
        let inside = video_dir.join("2026-05-20/a.mp4");
        let outside = temp.path().join("outside.mp4");
        std::fs::write(&inside, b"inside").unwrap();
        std::fs::write(&outside, b"outside").unwrap();

        remove_video_file(&video_dir, &outside.to_string_lossy()).unwrap();
        remove_video_file(&video_dir, "../outside.mp4").unwrap();
        assert!(outside.is_file());

        remove_video_file(&video_dir, "2026-05-20/a.mp4").unwrap();
        assert!(!inside.exists());
    }
}
