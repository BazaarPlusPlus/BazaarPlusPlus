//! The Managed Payload (root `GLOSSARY.md`) as install and Repair see it.
//!
//! The mod's self-update (mod ADR-0014) can leave a newer Managed Payload in
//! the game directory than the one in the installer's bundled zip. Repair keeps
//! that payload instead of writing the bundled one back (installer ADR-0009):
//! the bundled zip still supplies every non-managed file, and supplies the
//! managed files only on a first install or as the fallback.

use anyhow::{anyhow, bail, Context as _};
use std::collections::HashSet;
use std::io::Read;
use std::path::Path;

use sha2::{Digest, Sha256};

/// The install-relative directory the desktop Managed Payload lives in; the
/// mod's `ManagedPayloadHost.BepInEx` writes only directly inside it.
const MOD_DIRECTORY: &str = "BepInEx/plugins/";
const MANIFEST_HEADER: &str = "bpp-managed-payload-manifest 1";

/// A product version as the payload orders it: the leading major.minor.patch
/// of `BazaarPlusPlus.version` (`6.1.0.prod`, `6.1.0.t20261022.120000.dev`) or a
/// manifest's `version`. The mod's `ManagedPayloadVersion.TryParse` defines the
/// same order; the installer and the Updater must agree on which copy is newer.
#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord)]
pub(crate) struct ProductVersion(u32, u32, u32);

impl ProductVersion {
    pub(crate) fn parse(value: &str) -> Option<Self> {
        let mut parts = value.trim().split('.');
        let mut number = || {
            let part = parts.next()?;
            let numeric = !part.is_empty()
                && part.bytes().all(|byte| byte.is_ascii_digit())
                && !(part.len() > 1 && part.starts_with('0'));
            numeric.then(|| part.parse().ok()).flatten()
        };
        Some(Self(number()?, number()?, number()?))
    }
}

/// Readiness: the installed copy is current when it is not older than the
/// bundled one. A version that does not parse is never current.
pub(crate) fn installed_version_is_current(installed: &str, bundled: &str) -> bool {
    match (
        ProductVersion::parse(installed),
        ProductVersion::parse(bundled),
    ) {
        (Some(installed), Some(bundled)) => installed >= bundled,
        _ => false,
    }
}

#[derive(Debug, PartialEq, Eq)]
struct ManagedPayloadFile {
    path: String,
    size: u64,
    sha256: String,
}

/// The Managed Payload Manifest line format (`release/managed-payload.mjs`,
/// golden `release/fixtures/managed-payload/manifest.txt`), parsed as strictly
/// as the mod's `ManagedPayloadManifest.Parse`.
#[derive(Debug)]
struct ManagedPayloadManifest {
    version: ProductVersion,
    platform: String,
    history_database_user_version: u32,
    non_managed_payload_digest: String,
    files: Vec<ManagedPayloadFile>,
}

fn is_sha256(value: &str) -> bool {
    value.len() == 64
        && value
            .bytes()
            .all(|byte| byte.is_ascii_digit() || (b'a'..=b'f').contains(&byte))
}

fn is_safe_relative(path: &str) -> bool {
    !path.is_empty()
        && !path.contains(['\\', ':', '\0'])
        && path
            .split('/')
            .all(|part| !part.is_empty() && part != "." && part != "..")
}

impl ManagedPayloadManifest {
    fn parse(text: &str) -> anyhow::Result<Self> {
        let body = text
            .strip_suffix('\n')
            .filter(|_| !text.contains('\r'))
            .context("a Managed Payload Manifest is LF-terminated lines")?;
        let lines: Vec<&str> = body.split('\n').collect();
        if lines.len() < 5 || lines[0] != MANIFEST_HEADER {
            bail!("not a Managed Payload Manifest");
        }
        let field = |index: usize, name: &str| {
            lines[index]
                .strip_prefix(name)
                .and_then(|rest| rest.strip_prefix(' '))
                .with_context(|| format!("expected manifest field '{name}'"))
        };
        let version_text = field(1, "version")?;
        let version = ProductVersion::parse(version_text)
            .filter(|_| version_text.split('.').count() == 3)
            .with_context(|| format!("invalid manifest version '{version_text}'"))?;
        let platform = field(2, "platform")?;
        if platform.is_empty()
            || !platform
                .chars()
                .all(|c| c.is_alphanumeric() || c == '-' || c == '_')
        {
            bail!("invalid manifest platform '{platform}'");
        }
        let history = field(3, "history-database-user-version")?;
        let history_database_user_version = history
            .bytes()
            .all(|byte| byte.is_ascii_digit())
            .then(|| history.parse::<u32>().ok())
            .flatten()
            .filter(|value| *value > 0)
            .context("invalid history-database-user-version")?;
        let digest = field(4, "non-managed-payload-digest")?;
        if !is_sha256(digest) {
            bail!("invalid non-managed-payload-digest");
        }

        let mut files: Vec<ManagedPayloadFile> = Vec::new();
        for (index, line) in lines.iter().enumerate().skip(5) {
            let parts: Vec<&str> = line.splitn(4, ' ').collect();
            let [kind, sha256, size, path] = parts[..] else {
                bail!("invalid manifest line {}", index + 1);
            };
            let size = size
                .bytes()
                .all(|byte| byte.is_ascii_digit())
                .then(|| size.parse::<u64>().ok())
                .flatten();
            let (Some(size), true, true) = (size, kind == "file", is_sha256(sha256)) else {
                bail!("invalid manifest line {}", index + 1);
            };
            if !is_safe_relative(path) {
                bail!("unsafe manifest path '{path}'");
            }
            if files
                .last()
                .is_some_and(|previous| previous.path.as_str() >= path)
            {
                bail!("manifest files must be sorted and unique");
            }
            files.push(ManagedPayloadFile {
                path: path.to_owned(),
                size,
                sha256: sha256.to_owned(),
            });
        }
        if files.is_empty() {
            bail!("a Managed Payload Manifest names at least one file");
        }

        Ok(Self {
            version,
            platform: platform.to_owned(),
            history_database_user_version,
            non_managed_payload_digest: digest.to_owned(),
            files,
        })
    }
}

/// What install writes for the managed part of the Payload.
#[derive(Debug, PartialEq, Eq)]
pub(super) enum ManagedPayloadSource {
    /// Extract every bundled file: a first install, a bundled release that is
    /// not older than the installed one, or the fallback.
    Bundled,
    /// Keep the installed Managed Payload. `skip` lists the bundled entries
    /// install must not write; `keep` the installed paths it must not remove.
    Installed {
        version: String,
        skip: HashSet<String>,
        keep: HashSet<String>,
    },
}

/// Chooses between the bundled and the installed Managed Payload. The
/// installed one wins only when it is newer than the bundled one, built for
/// the same platform, history database schema and non-managed files (so the
/// bundled non-managed files are exactly the ones it was released with), and
/// every file its manifest lists is present with the recorded size and
/// SHA-256. Anything else, including a development build without a manifest,
/// falls back to the bundled zip.
pub(super) fn choose_managed_payload_source(
    game_path: &Path,
    manifest_path: &str,
    bundled_manifest: Option<&[u8]>,
) -> ManagedPayloadSource {
    match installed_payload_to_keep(game_path, manifest_path, bundled_manifest) {
        Ok(source) => source,
        Err(_reason) => {
            crate::services::debug_log!(
                "[install] writing the bundled Managed Payload: {_reason:#}"
            );
            ManagedPayloadSource::Bundled
        }
    }
}

fn installed_payload_to_keep(
    game_path: &Path,
    manifest_path: &str,
    bundled_manifest: Option<&[u8]>,
) -> anyhow::Result<ManagedPayloadSource> {
    let bundled =
        bundled_manifest.context("the bundled Payload has no Managed Payload Manifest")?;
    let bundled = std::str::from_utf8(bundled)
        .map_err(|_| anyhow!("the bundled Managed Payload Manifest is not UTF-8"))
        .and_then(ManagedPayloadManifest::parse)?;
    let installed = match std::fs::read(game_path.join(manifest_path)) {
        Ok(bytes) => bytes,
        Err(err) if err.kind() == std::io::ErrorKind::NotFound => {
            return Ok(ManagedPayloadSource::Bundled)
        }
        Err(err) => bail!("cannot read the installed manifest: {err}"),
    };
    let installed = std::str::from_utf8(&installed)
        .map_err(|_| anyhow!("the installed Managed Payload Manifest is not UTF-8"))
        .and_then(ManagedPayloadManifest::parse)?;

    if installed.version <= bundled.version {
        return Ok(ManagedPayloadSource::Bundled);
    }
    if installed.platform != bundled.platform {
        bail!(
            "the installed payload is for {}, the bundled one for {}",
            installed.platform,
            bundled.platform
        );
    }
    if installed.history_database_user_version != bundled.history_database_user_version {
        bail!("the installed payload needs another history database schema");
    }
    if installed.non_managed_payload_digest != bundled.non_managed_payload_digest {
        bail!("the installed payload was released with other non-managed files");
    }
    for file in &installed.files {
        let writable = file
            .path
            .strip_prefix(MOD_DIRECTORY)
            .is_some_and(|name| !name.contains('/'));
        if !writable || file.path == manifest_path {
            bail!("the installed manifest lists {}", file.path);
        }
        if !file_matches(&game_path.join(&file.path), file)? {
            bail!("{} differs from the installed manifest", file.path);
        }
    }

    let version = installed.version;
    let paths = |manifest: ManagedPayloadManifest| -> HashSet<String> {
        manifest
            .files
            .into_iter()
            .map(|file| file.path)
            .chain(std::iter::once(manifest_path.to_owned()))
            .collect()
    };
    Ok(ManagedPayloadSource::Installed {
        version: format!("{}.{}.{}", version.0, version.1, version.2),
        skip: paths(bundled),
        keep: paths(installed),
    })
}

fn file_matches(path: &Path, expected: &ManagedPayloadFile) -> anyhow::Result<bool> {
    let mut file = match std::fs::File::open(path) {
        Ok(file) => file,
        Err(err) if err.kind() == std::io::ErrorKind::NotFound => return Ok(false),
        Err(err) => bail!("cannot read {}: {err}", path.display()),
    };
    let size = file
        .metadata()
        .with_context(|| format!("cannot inspect {}", path.display()))?
        .len();
    if size != expected.size {
        return Ok(false);
    }
    let mut digest = Sha256::new();
    let mut buffer = [0; 64 * 1024];
    loop {
        let count = file
            .read(&mut buffer)
            .with_context(|| format!("cannot read {}", path.display()))?;
        if count == 0 {
            break;
        }
        digest.update(&buffer[..count]);
    }
    let actual: String = digest
        .finalize()
        .iter()
        .map(|byte| format!("{byte:02x}"))
        .collect();
    Ok(actual == expected.sha256)
}

#[cfg(test)]
pub(super) mod tests {
    use super::*;

    pub(crate) const MANIFEST_PATH: &str = "BepInEx/plugins/BazaarPlusPlus.managed-manifest.txt";
    const DIGEST: &str = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";

    pub(crate) fn sha256_hex(bytes: &[u8]) -> String {
        Sha256::digest(bytes)
            .iter()
            .map(|byte| format!("{byte:02x}"))
            .collect()
    }

    /// Renders a manifest the way `renderManagedPayloadManifest` does.
    pub(crate) fn render(
        version: &str,
        history: u32,
        digest: &str,
        files: &[(&str, &[u8])],
    ) -> String {
        let mut files = files.to_vec();
        files.sort_by(|left, right| left.0.cmp(right.0));
        let mut text = format!(
            "{MANIFEST_HEADER}\nversion {version}\nplatform darwin-aarch64\nhistory-database-user-version {history}\nnon-managed-payload-digest {digest}\n"
        );
        for (path, bytes) in files {
            text.push_str(&format!(
                "file {} {} {path}\n",
                sha256_hex(bytes),
                bytes.len()
            ));
        }
        text
    }

    /// The manifest a self-update one patch release past `bundled` would
    /// install: the same platform, schema and non-managed files, with the
    /// files in `replaced` changed. Returns the new version and the manifest.
    #[cfg(target_os = "macos")]
    pub(crate) fn self_updated(bundled: &str, replaced: &[(&str, &[u8])]) -> (String, String) {
        let bundled = ManagedPayloadManifest::parse(bundled).unwrap();
        let ProductVersion(major, minor, patch) = bundled.version;
        let version = format!("{major}.{minor}.{}", patch + 1);
        let mut text = format!(
            "{MANIFEST_HEADER}\nversion {version}\nplatform {}\nhistory-database-user-version {}\nnon-managed-payload-digest {}\n",
            bundled.platform,
            bundled.history_database_user_version,
            bundled.non_managed_payload_digest
        );
        for file in bundled.files {
            let (sha256, size) = match replaced.iter().find(|(path, _)| *path == file.path) {
                Some((_, bytes)) => (sha256_hex(bytes), bytes.len() as u64),
                None => (file.sha256, file.size),
            };
            text.push_str(&format!("file {sha256} {size} {}\n", file.path));
        }
        (version, text)
    }

    fn write(root: &Path, relative: &str, bytes: &[u8]) {
        let path = root.join(relative);
        std::fs::create_dir_all(path.parent().unwrap()).unwrap();
        std::fs::write(path, bytes).unwrap();
    }

    const BUNDLED_FILES: &[(&str, &[u8])] = &[
        ("BepInEx/plugins/BazaarPlusPlus.dll", b"bundled mod"),
        ("BepInEx/plugins/BazaarPlusPlus.version", b"6.0.0.prod\n"),
    ];
    type File = (&'static str, &'static [u8]);
    const INSTALLED_FILES: &[File] = &[
        ("BepInEx/plugins/BazaarPlusPlus.dll", b"self-updated mod"),
        ("BepInEx/plugins/BazaarPlusPlus.version", b"6.1.0.prod\n"),
    ];

    fn install(root: &Path, manifest: &str, files: &[(&str, &[u8])]) {
        for (path, bytes) in files {
            write(root, path, bytes);
        }
        write(root, MANIFEST_PATH, manifest.as_bytes());
    }

    fn choose(root: &Path, bundled: &str) -> ManagedPayloadSource {
        choose_managed_payload_source(root, MANIFEST_PATH, Some(bundled.as_bytes()))
    }

    #[test]
    fn the_shared_golden_parses() {
        let golden = include_str!(concat!(
            env!("CARGO_MANIFEST_DIR"),
            "/../../release/fixtures/managed-payload/manifest.txt"
        ));
        let manifest = ManagedPayloadManifest::parse(golden).unwrap();
        assert_eq!(manifest.version, ProductVersion(6, 1, 0));
        assert_eq!(manifest.platform, "darwin-aarch64");
        assert_eq!(manifest.history_database_user_version, 1);
        assert_eq!(manifest.non_managed_payload_digest, DIGEST);
        assert_eq!(
            manifest
                .files
                .iter()
                .map(|file| (file.path.as_str(), file.size))
                .collect::<Vec<_>>(),
            [
                ("BepInEx/plugins/BazaarPlusPlus.Core.dll", 4),
                ("BepInEx/plugins/BazaarPlusPlus.dll", 3)
            ]
        );
    }

    #[test]
    fn malformed_manifests_are_rejected() {
        let valid = render("6.1.0", 1, DIGEST, INSTALLED_FILES);
        assert!(ManagedPayloadManifest::parse(&valid).is_ok());
        for broken in [
            valid.trim_end().to_owned(),
            valid.replace('\n', "\r\n"),
            valid.replace("version 6.1.0", "version 6.1.0.prod"),
            valid.replace("version 6.1.0", "version 6.01.0"),
            valid.replace("user-version 1", "user-version 0"),
            valid.replace(DIGEST, "abc"),
            valid.replace("BepInEx/plugins/BazaarPlusPlus.dll", "BepInEx/../x.dll"),
            render("6.1.0", 1, DIGEST, &[]),
            {
                let mut lines: Vec<&str> = valid.lines().collect();
                lines.swap(5, 6);
                format!("{}\n", lines.join("\n"))
            },
        ] {
            assert!(ManagedPayloadManifest::parse(&broken).is_err(), "{broken}");
        }
    }

    #[test]
    fn versions_order_like_the_mod() {
        for (installed, bundled, current) in [
            ("6.0.0.prod", "6.0.0.prod", true),
            ("6.1.0.prod", "6.0.0.prod", true),
            ("6.0.10.prod", "6.0.9.prod", true),
            ("6.0.0.t20261022.120000.dev", "6.0.0.prod", true),
            ("5.9.9.prod", "6.0.0.prod", false),
            ("garbage", "6.0.0.prod", false),
            ("6.01.0.prod", "6.0.0.prod", false),
            ("packaged", "packaged", false),
            ("6.0.0.prod", "packaged", false),
        ] {
            assert_eq!(
                installed_version_is_current(installed, bundled),
                current,
                "{installed} vs {bundled}"
            );
        }
    }

    #[test]
    fn a_newer_intact_compatible_installed_payload_is_kept() {
        let tmp = tempfile::tempdir().unwrap();
        install(
            tmp.path(),
            &render("6.1.0", 1, DIGEST, INSTALLED_FILES),
            INSTALLED_FILES,
        );

        let ManagedPayloadSource::Installed {
            version,
            skip,
            keep,
        } = choose(tmp.path(), &render("6.0.0", 1, DIGEST, BUNDLED_FILES))
        else {
            panic!("the self-updated payload must be kept");
        };
        assert_eq!(version, "6.1.0");
        let expected: HashSet<String> = INSTALLED_FILES
            .iter()
            .map(|(path, _)| *path)
            .chain([MANIFEST_PATH])
            .map(str::to_owned)
            .collect();
        assert_eq!(keep, expected);
        assert_eq!(skip, expected);
    }

    #[test]
    fn every_other_installed_state_falls_back_to_the_bundled_payload() {
        let bundled = render("6.0.0", 1, DIGEST, BUNDLED_FILES);
        let other_digest = "d".repeat(64);
        let cases: Vec<(&str, String, Vec<File>)> = vec![
            (
                "same version",
                render("6.0.0", 1, DIGEST, BUNDLED_FILES),
                BUNDLED_FILES.to_vec(),
            ),
            (
                "older",
                render("5.9.0", 1, DIGEST, INSTALLED_FILES),
                INSTALLED_FILES.to_vec(),
            ),
            (
                "other schema",
                render("6.1.0", 2, DIGEST, INSTALLED_FILES),
                INSTALLED_FILES.to_vec(),
            ),
            (
                "other non-managed files",
                render("6.1.0", 1, &other_digest, INSTALLED_FILES),
                INSTALLED_FILES.to_vec(),
            ),
            (
                "corrupt file",
                render("6.1.0", 1, DIGEST, INSTALLED_FILES),
                vec![
                    (
                        "BepInEx/plugins/BazaarPlusPlus.dll",
                        b"truncated".as_slice(),
                    ),
                    INSTALLED_FILES[1],
                ],
            ),
            (
                "missing file",
                render("6.1.0", 1, DIGEST, INSTALLED_FILES),
                vec![INSTALLED_FILES[1]],
            ),
            (
                "file outside the mod directory",
                render(
                    "6.1.0",
                    1,
                    DIGEST,
                    &[("BepInEx/patchers/BazaarPlusPlus.Updater.dll", b"updater")],
                ),
                vec![(
                    "BepInEx/patchers/BazaarPlusPlus.Updater.dll",
                    b"updater".as_slice(),
                )],
            ),
            (
                "unparseable",
                "not a manifest\n".to_owned(),
                INSTALLED_FILES.to_vec(),
            ),
        ];
        for (case, manifest, files) in cases {
            let tmp = tempfile::tempdir().unwrap();
            install(tmp.path(), &manifest, &files);
            assert_eq!(
                choose(tmp.path(), &bundled),
                ManagedPayloadSource::Bundled,
                "{case}"
            );
        }

        let tmp = tempfile::tempdir().unwrap();
        for (path, bytes) in INSTALLED_FILES {
            write(tmp.path(), path, bytes);
        }
        assert_eq!(
            choose(tmp.path(), &bundled),
            ManagedPayloadSource::Bundled,
            "development build without a manifest"
        );
        install(
            tmp.path(),
            &render("6.1.0", 1, DIGEST, INSTALLED_FILES),
            INSTALLED_FILES,
        );
        assert_eq!(
            choose_managed_payload_source(tmp.path(), MANIFEST_PATH, None),
            ManagedPayloadSource::Bundled,
            "bundled Payload without a manifest"
        );
    }
}
