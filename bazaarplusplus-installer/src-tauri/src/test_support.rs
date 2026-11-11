//! Helpers shared by unit tests across modules.

/// Root ignores file permission bits, so a test that makes a file unreadable
/// or unwritable cannot observe the failure when the suite runs as root, as
/// it does in cloud containers. Such tests return early when this is true.
pub(crate) fn permission_bits_are_bypassed() -> bool {
    #[cfg(unix)]
    {
        // SAFETY: geteuid takes no arguments and cannot fail.
        unsafe { libc::geteuid() == 0 }
    }
    #[cfg(not(unix))]
    {
        false
    }
}

/// One stored relative path from the input table of issue #989, which every
/// History caller that resolves a path the mod stored is pinned against.
pub(crate) struct StoredPathInput {
    /// The input's name in the table; `a` stands for the directory segment.
    pub(crate) label: &'static str,
    pub(crate) raw: String,
}

/// The #989 input table. `dir` is the directory segment the table calls `a`:
/// the orphan scan reads only past-dated folders, so its test passes a date.
/// `absolute` is an absolute path outside every root. `C:x.png` is a Windows
/// drive-relative path; the table carries it on every platform, since the
/// stored-path rule compares text the same way everywhere.
pub(crate) fn stored_path_inputs(dir: &str, absolute: &std::path::Path) -> Vec<StoredPathInput> {
    [
        ("a/b.png", format!("{dir}/b.png")),
        (r"a\b.png", format!(r"{dir}\b.png")),
        ("a/../b.png", format!("{dir}/../b.png")),
        ("..", "..".to_string()),
        (".", ".".to_string()),
        ("empty", String::new()),
        ("blank", "   ".to_string()),
        ("absolute", absolute.to_string_lossy().into_owned()),
        ("UNC", r"\\server\share\b.png".to_string()),
        ("C:x.png", "C:x.png".to_string()),
    ]
    .into_iter()
    .map(|(label, raw)| StoredPathInput { label, raw })
    .collect()
}

/// The files a stored path can name inside `root`, by the label a test shows
/// for each: `a/b.png` (with `dir` for `a`), `b.png`, and `server/share/b.png`,
/// where a UNC path lands when `\` is only a separator. Each is a PNG of its
/// own colour, so a served image tells which one a caller resolved.
pub(crate) fn write_stored_path_targets(
    root: &std::path::Path,
    dir: &str,
) -> Vec<(&'static str, std::path::PathBuf)> {
    [
        ("a/b.png", root.join(dir).join("b.png"), [200, 0, 0, 255]),
        ("b.png", root.join("b.png"), [0, 200, 0, 255]),
        (
            "server/share/b.png",
            root.join("server").join("share").join("b.png"),
            [0, 0, 200, 255],
        ),
    ]
    .into_iter()
    .map(|(label, path, color)| {
        std::fs::create_dir_all(path.parent().unwrap()).unwrap();
        image::RgbaImage::from_pixel(64, 32, image::Rgba(color))
            .save(&path)
            .unwrap();
        (label, path)
    })
    .collect()
}

/// The target label whose colour `pixel` is, for a served image.
pub(crate) fn stored_path_target_of_pixel(pixel: [u8; 4]) -> &'static str {
    match pixel {
        [200, 0, 0, 255] => "a/b.png",
        [0, 200, 0, 255] => "b.png",
        [0, 0, 200, 255] => "server/share/b.png",
        other => panic!("no stored path target has the colour {other:?}"),
    }
}

/// `path` relative to `root` as `<root>/x/y`, `/`-separated on every
/// platform, or as written when it is not under `root`.
pub(crate) fn describe_path_under(root: &std::path::Path, path: &std::path::Path) -> String {
    match path.strip_prefix(root) {
        Ok(rest) => std::iter::once("<root>".to_string())
            .chain(
                rest.components()
                    .map(|part| part.as_os_str().to_string_lossy().into_owned()),
            )
            .collect::<Vec<_>>()
            .join("/"),
        Err(_) => path.display().to_string(),
    }
}

/// Runs `outcome` for every input and compares it with `expected`, which names
/// every input exactly once. An expectation `pinned by <test>` names the
/// existing test that already pins this caller for that input; the input is
/// not run again. Reports every mismatch at once.
pub(crate) fn assert_stored_path_table(
    caller: &str,
    inputs: &[StoredPathInput],
    expected: &[(&str, &str)],
    mut outcome: impl FnMut(&StoredPathInput) -> String,
) {
    let mut labels = inputs.iter().map(|input| input.label).collect::<Vec<_>>();
    let mut expected_labels = expected.iter().map(|(label, _)| *label).collect::<Vec<_>>();
    labels.sort_unstable();
    expected_labels.sort_unstable();
    assert_eq!(
        expected_labels, labels,
        "{caller}: the table must name every input once"
    );

    let mismatches = inputs
        .iter()
        .filter_map(|input| {
            let (_, want) = expected
                .iter()
                .find(|(label, _)| *label == input.label)
                .unwrap();
            if want.starts_with("pinned by ") {
                return None;
            }
            let got = outcome(input);
            (got != *want).then(|| format!("  {}: expected `{want}`, got `{got}`", input.label))
        })
        .collect::<Vec<_>>();
    assert!(
        mismatches.is_empty(),
        "{caller}:\n{}",
        mismatches.join("\n")
    );
}
