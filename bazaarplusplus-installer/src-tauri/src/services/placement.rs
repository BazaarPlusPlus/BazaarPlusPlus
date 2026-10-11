//! Places source files at target paths without overwriting anything, and can
//! undo exactly what it placed.
//!
//! A target on the source's volume shares the source's data: a `clonefile`
//! clone on macOS, otherwise a hard link. Anything else is copied into a
//! partial file next to the target and renamed into place, so an interrupted
//! copy never leaves a file at the target path. An existing target is accepted
//! only when its size and sha256 match the source. Free space for every copy is
//! checked before the first write.

// The Payload migration is its first caller; until it lands only tests use it.
#![cfg_attr(not(test), allow(dead_code))]

use std::collections::{BTreeMap, HashSet};
use std::fmt;
use std::fs;
use std::io::{self, Read};
use std::path::{Path, PathBuf};

use sha2::{Digest, Sha256};

/// One file to place: `source` must be a regular file.
#[derive(Debug, Clone)]
pub(crate) struct PlacementRequest {
    pub source: PathBuf,
    pub target: PathBuf,
}

/// How a target came to hold the source's bytes.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum PlacementMethod {
    Clone,
    HardLink,
    Copy,
    /// The target already held identical bytes; nothing was written.
    AlreadyPresent,
}

/// A placement that shares the source's data instead of copying it.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum LinkKind {
    #[cfg_attr(not(target_os = "macos"), allow(dead_code))]
    Clone,
    HardLink,
}

#[derive(Debug)]
pub(crate) enum PlacementError {
    /// The target exists with different bytes; it is left untouched.
    TargetConflict {
        target: PathBuf,
    },
    /// The copies bound for one volume need more space than it has.
    InsufficientSpace {
        volume: PathBuf,
        required: u64,
        available: u64,
    },
    Io {
        path: PathBuf,
        source: io::Error,
    },
}

impl fmt::Display for PlacementError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::TargetConflict { target } => {
                write!(
                    f,
                    "{} already exists with different content",
                    target.display()
                )
            }
            Self::InsufficientSpace {
                volume,
                required,
                available,
            } => write!(
                f,
                "{} needs {required} bytes free but has {available}",
                volume.display()
            ),
            Self::Io { path, source } => write!(f, "{}: {source}", path.display()),
        }
    }
}

impl std::error::Error for PlacementError {
    fn source(&self) -> Option<&(dyn std::error::Error + 'static)> {
        match self {
            Self::Io { source, .. } => Some(source),
            _ => None,
        }
    }
}

fn io_error(path: &Path) -> impl FnOnce(io::Error) -> PlacementError + '_ {
    move |source| PlacementError::Io {
        path: path.to_path_buf(),
        source,
    }
}

/// The filesystem operations whose outcome depends on the platform or volume.
pub(crate) trait PlacementBackend {
    /// A key equal for two existing paths exactly when they share a volume.
    fn volume_id(&self, existing: &Path) -> io::Result<String>;
    /// Bytes the current user may still write on the volume holding `existing`.
    fn available_space(&self, existing: &Path) -> io::Result<u64>;
    /// Creates `target` sharing `source`'s data, or fails when the volume
    /// cannot.
    fn link(&self, source: &Path, target: &Path) -> io::Result<LinkKind>;
    /// Writes `source`'s bytes durably to `partial`, replacing any content.
    fn copy(&self, source: &Path, partial: &Path) -> io::Result<()>;
}

/// The real filesystem.
#[derive(Debug, Default, Clone, Copy)]
pub(crate) struct SystemBackend;

impl PlacementBackend for SystemBackend {
    fn volume_id(&self, existing: &Path) -> io::Result<String> {
        platform::volume_id(existing)
    }

    fn available_space(&self, existing: &Path) -> io::Result<u64> {
        platform::available_space(existing)
    }

    fn link(&self, source: &Path, target: &Path) -> io::Result<LinkKind> {
        #[cfg(target_os = "macos")]
        if platform::clone_file(source, target).is_ok() {
            return Ok(LinkKind::Clone);
        }
        fs::hard_link(source, target).map(|()| LinkKind::HardLink)
    }

    fn copy(&self, source: &Path, partial: &Path) -> io::Result<()> {
        fs::copy(source, partial)?;
        // Windows flushes only through a handle opened for writing.
        fs::OpenOptions::new().write(true).open(partial)?.sync_all()
    }
}

#[cfg(unix)]
mod platform {
    use std::ffi::CString;
    use std::io;
    use std::os::unix::ffi::OsStrExt;
    use std::os::unix::fs::MetadataExt;
    use std::path::Path;

    fn c_path(path: &Path) -> io::Result<CString> {
        Ok(CString::new(path.as_os_str().as_bytes())?)
    }

    pub(super) fn volume_id(existing: &Path) -> io::Result<String> {
        Ok(std::fs::metadata(existing)?.dev().to_string())
    }

    // `fsblkcnt_t` and `c_ulong` are not `u64` on every unix target.
    #[allow(clippy::unnecessary_cast)]
    pub(super) fn available_space(existing: &Path) -> io::Result<u64> {
        let path = c_path(existing)?;
        let mut stat = std::mem::MaybeUninit::<libc::statvfs>::zeroed();
        // SAFETY: `path` is NUL-terminated and `stat` is a writable statvfs.
        if unsafe { libc::statvfs(path.as_ptr(), stat.as_mut_ptr()) } != 0 {
            return Err(io::Error::last_os_error());
        }
        // SAFETY: statvfs returned 0, so it filled the struct.
        let stat = unsafe { stat.assume_init() };
        Ok((stat.f_bavail as u64).saturating_mul(stat.f_frsize as u64))
    }

    #[cfg(target_os = "macos")]
    pub(super) fn clone_file(source: &Path, target: &Path) -> io::Result<()> {
        let source = c_path(source)?;
        let target = c_path(target)?;
        // SAFETY: both paths are NUL-terminated and outlive the call.
        if unsafe { libc::clonefile(source.as_ptr(), target.as_ptr(), 0) } == 0 {
            Ok(())
        } else {
            Err(io::Error::last_os_error())
        }
    }
}

#[cfg(windows)]
mod platform {
    use std::io;
    use std::os::windows::ffi::OsStrExt;
    use std::path::Path;

    use windows::core::PCWSTR;
    use windows::Win32::Storage::FileSystem::{GetDiskFreeSpaceExW, GetVolumePathNameW};

    fn wide(path: &Path) -> Vec<u16> {
        path.as_os_str().encode_wide().chain(Some(0)).collect()
    }

    pub(super) fn volume_id(existing: &Path) -> io::Result<String> {
        let path = wide(existing);
        // The volume path is a prefix of the input, so the input's length bounds it.
        let mut volume = vec![0u16; path.len().max(261)];
        // SAFETY: `path` is NUL-terminated and `volume` is a writable buffer.
        unsafe { GetVolumePathNameW(PCWSTR(path.as_ptr()), &mut volume) }?;
        let len = volume.iter().position(|&c| c == 0).unwrap_or(volume.len());
        Ok(String::from_utf16_lossy(&volume[..len]).to_lowercase())
    }

    pub(super) fn available_space(existing: &Path) -> io::Result<u64> {
        let path = wide(existing);
        let mut available = 0u64;
        // SAFETY: `path` is NUL-terminated and `available` outlives the call.
        unsafe {
            GetDiskFreeSpaceExW(PCWSTR(path.as_ptr()), Some(&raw mut available), None, None)
        }?;
        Ok(available)
    }
}

#[derive(Debug, Clone, Copy)]
enum Plan {
    AlreadyPresent,
    Link,
    Copy,
}

#[derive(Debug)]
enum Created {
    File(PathBuf),
    Directory(PathBuf),
}

/// Places files and journals every path it creates so `rollback` can remove
/// them again.
pub(crate) struct Placer<B> {
    backend: B,
    created: Vec<Created>,
}

impl<B: PlacementBackend> Placer<B> {
    pub(crate) fn new(backend: B) -> Self {
        Self {
            backend,
            created: Vec::new(),
        }
    }

    /// Every file and directory this placer created, in creation order.
    pub(crate) fn created_paths(&self) -> impl Iterator<Item = &Path> {
        self.created.iter().map(|created| match created {
            Created::File(path) | Created::Directory(path) => path.as_path(),
        })
    }

    /// Places every request, or fails before the first write when a target
    /// conflicts or a volume lacks space for its copies. A failure after
    /// writing began leaves the journal intact for `rollback`.
    pub(crate) fn place(
        &mut self,
        requests: &[PlacementRequest],
    ) -> Result<Vec<PlacementMethod>, PlacementError> {
        let mut plans = Vec::with_capacity(requests.len());
        // Volume id -> (an existing path on it, bytes still to copy there).
        let mut pending: BTreeMap<String, (PathBuf, u64)> = BTreeMap::new();
        let mut targets = HashSet::new();
        for request in requests {
            if !targets.insert(request.target.as_path()) {
                return Err(PlacementError::Io {
                    path: request.target.clone(),
                    source: io::Error::new(io::ErrorKind::InvalidInput, "duplicate target"),
                });
            }
            let (plan, volume, anchor, len) = self.plan(request)?;
            if matches!(plan, Plan::Link | Plan::Copy) {
                let entry = pending.entry(volume).or_insert((anchor, 0));
                if matches!(plan, Plan::Copy) {
                    entry.1 += len;
                }
            }
            plans.push((plan, len));
        }
        for (anchor, required) in pending.values() {
            self.ensure_space(anchor, *required)?;
        }

        let mut methods = Vec::with_capacity(requests.len());
        for (request, (plan, len)) in requests.iter().zip(plans) {
            let method = match plan {
                Plan::AlreadyPresent => PlacementMethod::AlreadyPresent,
                Plan::Link => {
                    self.create_parents(&request.target)?;
                    match self.backend.link(&request.source, &request.target) {
                        Ok(kind) => {
                            self.created.push(Created::File(request.target.clone()));
                            match kind {
                                LinkKind::Clone => PlacementMethod::Clone,
                                LinkKind::HardLink => PlacementMethod::HardLink,
                            }
                        }
                        Err(error) if error.kind() == io::ErrorKind::AlreadyExists => {
                            return Err(PlacementError::TargetConflict {
                                target: request.target.clone(),
                            });
                        }
                        Err(_) => {
                            // The volume refused to share data (FAT, exFAT, a
                            // network share): this file now needs real space on
                            // top of the copies still pending there.
                            let volume = self.anchor_volume(&request.target)?;
                            let entry = pending.entry(volume.0).or_insert((volume.1, 0));
                            entry.1 += len;
                            self.ensure_space(&entry.0, entry.1)?;
                            self.copy_into_place(request)?;
                            entry.1 -= len;
                            PlacementMethod::Copy
                        }
                    }
                }
                Plan::Copy => {
                    self.create_parents(&request.target)?;
                    self.copy_into_place(request)?;
                    let volume = self.anchor_volume(&request.target)?;
                    if let Some(entry) = pending.get_mut(&volume.0) {
                        entry.1 = entry.1.saturating_sub(len);
                    }
                    PlacementMethod::Copy
                }
            };
            methods.push(method);
        }
        Ok(methods)
    }

    /// Removes every path this placer created, newest first. Keeps going past
    /// failures and reports the first; a directory that gained foreign files
    /// is left in place.
    pub(crate) fn rollback(&mut self) -> Result<(), PlacementError> {
        let mut first_error = None;
        while let Some(created) = self.created.pop() {
            let (path, result) = match &created {
                Created::File(path) => (path, fs::remove_file(path)),
                Created::Directory(path) => (path, fs::remove_dir(path)),
            };
            match result {
                Ok(()) => {}
                Err(error) if error.kind() == io::ErrorKind::NotFound => {}
                Err(error) => {
                    first_error.get_or_insert_with(|| io_error(path)(error));
                }
            }
        }
        first_error.map_or(Ok(()), Err)
    }

    fn plan(
        &self,
        request: &PlacementRequest,
    ) -> Result<(Plan, String, PathBuf, u64), PlacementError> {
        let source = fs::metadata(&request.source).map_err(io_error(&request.source))?;
        if !source.is_file() {
            return Err(PlacementError::Io {
                path: request.source.clone(),
                source: io::Error::new(io::ErrorKind::InvalidInput, "source is not a file"),
            });
        }
        let len = source.len();
        match fs::symlink_metadata(&request.target) {
            Ok(target) => {
                if target.is_file()
                    && target.len() == len
                    && sha256(&request.source)? == sha256(&request.target)?
                {
                    return Ok((Plan::AlreadyPresent, String::new(), PathBuf::new(), len));
                }
                return Err(PlacementError::TargetConflict {
                    target: request.target.clone(),
                });
            }
            Err(error) if error.kind() == io::ErrorKind::NotFound => {}
            Err(error) => return Err(io_error(&request.target)(error)),
        }
        let (volume, anchor) = self.anchor_volume(&request.target)?;
        let source_volume = self
            .backend
            .volume_id(&request.source)
            .map_err(io_error(&request.source))?;
        let plan = if source_volume == volume {
            Plan::Link
        } else {
            Plan::Copy
        };
        Ok((plan, volume, anchor, len))
    }

    /// The volume id of `target`'s nearest existing ancestor, and that ancestor.
    fn anchor_volume(&self, target: &Path) -> Result<(String, PathBuf), PlacementError> {
        let mut anchor = target.parent().unwrap_or(target);
        while !anchor.exists() {
            match anchor.parent() {
                Some(parent) => anchor = parent,
                None => break,
            }
        }
        let volume = self.backend.volume_id(anchor).map_err(io_error(anchor))?;
        Ok((volume, anchor.to_path_buf()))
    }

    fn ensure_space(&self, anchor: &Path, required: u64) -> Result<(), PlacementError> {
        if required == 0 {
            return Ok(());
        }
        let available = self
            .backend
            .available_space(anchor)
            .map_err(io_error(anchor))?;
        if available < required {
            return Err(PlacementError::InsufficientSpace {
                volume: anchor.to_path_buf(),
                required,
                available,
            });
        }
        Ok(())
    }

    fn create_parents(&mut self, target: &Path) -> Result<(), PlacementError> {
        let mut missing = Vec::new();
        let mut directory = target.parent();
        while let Some(path) = directory {
            if path.as_os_str().is_empty() || path.exists() {
                break;
            }
            missing.push(path);
            directory = path.parent();
        }
        for path in missing.into_iter().rev() {
            match fs::create_dir(path) {
                Ok(()) => self.created.push(Created::Directory(path.to_path_buf())),
                Err(error) if error.kind() == io::ErrorKind::AlreadyExists => {}
                Err(error) => return Err(io_error(path)(error)),
            }
        }
        Ok(())
    }

    /// Copies into a partial file beside the target, then renames it into
    /// place, so the target path only ever holds complete bytes. A partial
    /// file left by an interrupted earlier attempt is overwritten.
    fn copy_into_place(&mut self, request: &PlacementRequest) -> Result<(), PlacementError> {
        let partial = partial_path(&request.target);
        if let Err(error) = self.backend.copy(&request.source, &partial) {
            let _ = fs::remove_file(&partial);
            return Err(io_error(&partial)(error));
        }
        // Planning saw no target; refuse to replace one that appeared since.
        if fs::symlink_metadata(&request.target).is_ok() {
            let _ = fs::remove_file(&partial);
            return Err(PlacementError::TargetConflict {
                target: request.target.clone(),
            });
        }
        if let Err(error) = fs::rename(&partial, &request.target) {
            let _ = fs::remove_file(&partial);
            return Err(io_error(&request.target)(error));
        }
        self.created.push(Created::File(request.target.clone()));
        Ok(())
    }
}

fn partial_path(target: &Path) -> PathBuf {
    let mut name = std::ffi::OsString::from(".");
    name.push(target.file_name().unwrap_or_default());
    name.push(".bpp-partial");
    target.with_file_name(name)
}

fn sha256(path: &Path) -> Result<[u8; 32], PlacementError> {
    let mut file = fs::File::open(path).map_err(io_error(path))?;
    let mut digest = Sha256::new();
    let mut buffer = [0; 64 * 1024];
    loop {
        let count = file.read(&mut buffer).map_err(io_error(path))?;
        if count == 0 {
            break;
        }
        digest.update(&buffer[..count]);
    }
    Ok(digest.finalize().into())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::cell::Cell;
    use std::io::Write;

    /// The real filesystem with optional injected volume layout, link
    /// support, free space and copy interruption.
    #[derive(Default)]
    struct TestBackend {
        separate_volumes: bool,
        refuse_links: bool,
        available: Option<u64>,
        interrupt_copy: bool,
        writes: Cell<usize>,
    }

    impl PlacementBackend for TestBackend {
        fn volume_id(&self, existing: &Path) -> io::Result<String> {
            if self.separate_volumes {
                // Sources live under `source/`, targets under `target/`.
                let side = existing
                    .components()
                    .any(|part| part.as_os_str() == "source");
                return Ok(if side { "source" } else { "target" }.to_string());
            }
            SystemBackend.volume_id(existing)
        }

        fn available_space(&self, existing: &Path) -> io::Result<u64> {
            match self.available {
                Some(available) => Ok(available),
                None => SystemBackend.available_space(existing),
            }
        }

        fn link(&self, source: &Path, target: &Path) -> io::Result<LinkKind> {
            if self.refuse_links {
                return Err(io::Error::new(io::ErrorKind::Unsupported, "no links here"));
            }
            self.writes.set(self.writes.get() + 1);
            SystemBackend.link(source, target)
        }

        fn copy(&self, source: &Path, partial: &Path) -> io::Result<()> {
            self.writes.set(self.writes.get() + 1);
            if self.interrupt_copy {
                let bytes = fs::read(source)?;
                let mut file = fs::File::create(partial)?;
                file.write_all(&bytes[..bytes.len() / 2])?;
                return Err(io::Error::new(io::ErrorKind::Interrupted, "copy cut short"));
            }
            SystemBackend.copy(source, partial)
        }
    }

    struct Fixture {
        _root: tempfile::TempDir,
        source: PathBuf,
        target: PathBuf,
    }

    fn fixture() -> Fixture {
        let root = tempfile::tempdir().unwrap();
        let source = root.path().join("source");
        let target = root.path().join("target");
        fs::create_dir_all(source.join("Screenshots")).unwrap();
        fs::create_dir_all(&target).unwrap();
        fs::write(source.join("a.bin"), vec![7u8; 4096]).unwrap();
        fs::write(source.join("Screenshots/run.png"), b"png bytes").unwrap();
        fs::write(target.join("keep.txt"), b"user data").unwrap();
        Fixture {
            source,
            target,
            _root: root,
        }
    }

    fn requests(fixture: &Fixture) -> Vec<PlacementRequest> {
        ["a.bin", "Screenshots/run.png"]
            .into_iter()
            .map(|relative| PlacementRequest {
                source: fixture.source.join(relative),
                target: fixture.target.join("nested").join(relative),
            })
            .collect()
    }

    /// Every directory and file under `root` with its bytes.
    fn snapshot(root: &Path) -> Vec<(PathBuf, Option<Vec<u8>>)> {
        fn walk(path: &Path, out: &mut Vec<(PathBuf, Option<Vec<u8>>)>) {
            for entry in fs::read_dir(path).unwrap() {
                let path = entry.unwrap().path();
                if path.is_dir() {
                    out.push((path.clone(), None));
                    walk(&path, out);
                } else {
                    out.push((path.clone(), Some(fs::read(&path).unwrap())));
                }
            }
        }
        let mut out = Vec::new();
        walk(root, &mut out);
        out.sort();
        out
    }

    fn assert_placed(fixture: &Fixture) {
        for request in requests(fixture) {
            assert_eq!(
                fs::read(&request.target).unwrap(),
                fs::read(&request.source).unwrap()
            );
        }
    }

    #[test]
    fn same_volume_shares_data_instead_of_copying() {
        let fixture = fixture();
        let mut placer = Placer::new(SystemBackend);

        let methods = placer.place(&requests(&fixture)).unwrap();

        #[cfg(not(target_os = "macos"))]
        assert_eq!(methods, [PlacementMethod::HardLink; 2]);
        #[cfg(target_os = "macos")]
        assert!(methods
            .iter()
            .all(|m| matches!(m, PlacementMethod::Clone | PlacementMethod::HardLink)));
        assert_placed(&fixture);
    }

    #[cfg(all(unix, not(target_os = "macos")))]
    #[test]
    fn hard_link_shares_the_source_inode() {
        use std::os::unix::fs::MetadataExt;
        let fixture = fixture();
        let request = &requests(&fixture)[..1];
        let mut placer = Placer::new(SystemBackend);

        assert_eq!(placer.place(request).unwrap(), [PlacementMethod::HardLink]);

        let source = fs::metadata(&request[0].source).unwrap();
        let target = fs::metadata(&request[0].target).unwrap();
        assert_eq!(source.ino(), target.ino());
    }

    #[test]
    fn refused_link_falls_back_to_copy() {
        let fixture = fixture();
        let mut placer = Placer::new(TestBackend {
            refuse_links: true,
            ..TestBackend::default()
        });

        let methods = placer.place(&requests(&fixture)).unwrap();

        assert_eq!(methods, [PlacementMethod::Copy; 2]);
        assert_placed(&fixture);
    }

    #[test]
    fn other_volume_copies() {
        let fixture = fixture();
        let mut placer = Placer::new(TestBackend {
            separate_volumes: true,
            ..TestBackend::default()
        });

        let methods = placer.place(&requests(&fixture)).unwrap();

        assert_eq!(methods, [PlacementMethod::Copy; 2]);
        assert_placed(&fixture);
    }

    #[test]
    fn interrupted_copy_leaves_no_target_and_a_retry_completes_it() {
        let fixture = fixture();
        let requests = requests(&fixture);
        let mut interrupted = Placer::new(TestBackend {
            separate_volumes: true,
            interrupt_copy: true,
            ..TestBackend::default()
        });

        assert!(matches!(
            interrupted.place(&requests),
            Err(PlacementError::Io { .. })
        ));
        assert!(!requests[0].target.exists());
        // A crash would also skip cleanup: leave a half-written partial file.
        fs::write(partial_path(&requests[0].target), [7u8; 10]).unwrap();

        let mut retry = Placer::new(TestBackend {
            separate_volumes: true,
            ..TestBackend::default()
        });
        let methods = retry.place(&requests).unwrap();

        assert_eq!(methods, [PlacementMethod::Copy; 2]);
        assert_placed(&fixture);
        assert!(!partial_path(&requests[0].target).exists());
    }

    #[test]
    fn identical_existing_target_is_skipped() {
        let fixture = fixture();
        let requests = requests(&fixture);
        fs::create_dir_all(requests[0].target.parent().unwrap()).unwrap();
        fs::copy(&requests[0].source, &requests[0].target).unwrap();
        let mut placer = Placer::new(TestBackend::default());

        let methods = placer.place(&requests).unwrap();

        assert_eq!(methods[0], PlacementMethod::AlreadyPresent);
        assert!(placer
            .created_paths()
            .all(|path| path != requests[0].target));
        assert_placed(&fixture);
    }

    #[test]
    fn different_existing_target_fails_before_writing() {
        let fixture = fixture();
        let requests = requests(&fixture);
        // Same size, different bytes: only the hash tells them apart.
        let target = &requests[1].target;
        fs::create_dir_all(target.parent().unwrap()).unwrap();
        fs::write(target, b"PNG BYTES").unwrap();
        let before = snapshot(&fixture.target);
        let backend = TestBackend::default();
        let mut placer = Placer::new(backend);

        let error = placer.place(&requests).unwrap_err();

        assert!(matches!(error, PlacementError::TargetConflict { target: t } if t == *target));
        assert_eq!(snapshot(&fixture.target), before);
        assert_eq!(placer.created_paths().count(), 0);
    }

    #[test]
    fn rollback_restores_the_tree_byte_for_byte() {
        for backend in [
            TestBackend::default(),
            TestBackend {
                separate_volumes: true,
                ..TestBackend::default()
            },
        ] {
            let fixture = fixture();
            let requests = requests(&fixture);
            // One target already holds identical bytes and must survive rollback.
            fs::create_dir_all(requests[0].target.parent().unwrap()).unwrap();
            fs::copy(&requests[0].source, &requests[0].target).unwrap();
            let before = snapshot(&fixture.target);
            let sources = snapshot(&fixture.source);
            let mut placer = Placer::new(backend);

            placer.place(&requests).unwrap();
            assert_ne!(snapshot(&fixture.target), before);
            placer.rollback().unwrap();

            assert_eq!(snapshot(&fixture.target), before);
            assert_eq!(snapshot(&fixture.source), sources);
            assert_eq!(placer.created_paths().count(), 0);
        }
    }

    #[test]
    fn insufficient_space_fails_before_writing() {
        let fixture = fixture();
        let before = snapshot(&fixture.target);
        let mut placer = Placer::new(TestBackend {
            separate_volumes: true,
            available: Some(4096),
            ..TestBackend::default()
        });

        let error = placer.place(&requests(&fixture)).unwrap_err();

        assert!(matches!(
            error,
            PlacementError::InsufficientSpace {
                required: 4105,
                available: 4096,
                ..
            }
        ));
        assert_eq!(placer.backend.writes.get(), 0);
        assert_eq!(snapshot(&fixture.target), before);
    }

    #[test]
    fn links_need_no_free_space() {
        let fixture = fixture();
        let mut placer = Placer::new(TestBackend {
            available: Some(0),
            ..TestBackend::default()
        });

        placer.place(&requests(&fixture)).unwrap();

        assert_placed(&fixture);
    }

    #[test]
    fn refused_link_rechecks_space_before_copying() {
        let fixture = fixture();
        let mut placer = Placer::new(TestBackend {
            refuse_links: true,
            available: Some(100),
            ..TestBackend::default()
        });

        let error = placer.place(&requests(&fixture)).unwrap_err();

        assert!(matches!(
            error,
            PlacementError::InsufficientSpace { required: 4096, .. }
        ));
        assert!(!requests(&fixture)[0].target.exists());
        placer.rollback().unwrap();
        assert!(!fixture.target.join("nested").exists());
    }

    #[test]
    fn real_volume_reports_free_space() {
        let root = tempfile::tempdir().unwrap();
        assert!(SystemBackend.available_space(root.path()).unwrap() > 0);
        assert_eq!(
            SystemBackend.volume_id(root.path()).unwrap(),
            SystemBackend.volume_id(&root.path().join(".")).unwrap()
        );
    }
}
