use std::path::Path;

use keyvalues_parser::{Parser, Value};

use super::launch_options::*;
use super::parse::*;

#[cfg(target_os = "macos")]
use std::os::unix::fs::PermissionsExt;

fn fixture_vdf() -> &'static str {
    r#"
"UserLocalConfigStore"
{
    "Software"
    {
        "Valve"
        {
            "Steam"
            {
                "apps"
                {
                    "1617400"
                    {
                        "LastPlayed"    "1700000000"
                    }
                }
            }
        }
    }
}"#
}

#[test]
fn test_inject_launch_options_inserts_when_missing() {
    let result = inject_launch_options(fixture_vdf(), "MY_ARGS")
        .unwrap()
        .unwrap();
    assert!(result.contains("LaunchOptions"));
    assert!(result.contains("MY_ARGS"));
}

#[test]
fn test_inject_launch_options_replaces_existing() {
    let vdf_with_lo = fixture_vdf().replace(
        "\"LastPlayed\"",
        "\"LaunchOptions\"\t\t\"OLD_ARGS\"\n\t\t\t\t\t\"LastPlayed\"",
    );

    let result = inject_launch_options(&vdf_with_lo, "NEW_ARGS")
        .unwrap()
        .unwrap();
    assert!(result.contains("NEW_ARGS"));
    assert!(!result.contains("OLD_ARGS"));
}

#[test]
fn test_clear_launch_options_removes_existing_line() {
    let vdf_with_lo = fixture_vdf().replace(
        "\"LastPlayed\"",
        "\"LaunchOptions\"\t\t\"OLD_ARGS\"\n\t\t\t\t\t\"LastPlayed\"",
    );
    let result = clear_launch_options(&vdf_with_lo).unwrap().unwrap();
    assert!(!result.contains("LaunchOptions"));
    assert!(result.contains("LastPlayed"));
}

#[test]
fn test_inject_skips_missing_app_id() {
    let vdf = r#"
"UserLocalConfigStore"
{
    "Software"
    {
        "Valve"
        {
            "Steam"
            {
                "apps"
                {
                    "730"
                    {
                        "LastPlayed"    "1700000000"
                    }
                }
            }
        }
    }
}"#;
    let result = inject_launch_options(vdf, "args");
    assert_eq!(result.unwrap(), None);
}

#[test]
fn test_inject_launch_options_escapes_quoted_args() {
    let args = "\"/Applications/The Bazaar/run_bepinex.sh\" %command%";
    let rendered = inject_launch_options(fixture_vdf(), args).unwrap().unwrap();
    assert!(rendered.contains(
        "\"LaunchOptions\"\t\t\"\\\"/Applications/The Bazaar/run_bepinex.sh\\\" %command%\""
    ));
    assert!(!rendered.contains("\"LaunchOptions\"\t\t\"\"\n"));
    let parsed = Parser::new().parse(&rendered).unwrap();
    let root = parsed.value.get_obj().unwrap();
    let app = get_app_obj(root, THE_BAZAAR_APP_ID).unwrap();
    let launch_options = app
        .get(LAUNCH_OPTIONS_KEY)
        .and_then(|values| values.first())
        .and_then(Value::get_str)
        .unwrap();

    assert_eq!(launch_options, args);
    assert!(rendered.contains("\\\"/Applications/The Bazaar/run_bepinex.sh\\\" %command%"));
}

#[test]
fn test_inject_launch_options_removes_malformed_stray_fragments() {
    let args = "\"/Applications/The Bazaar/run_bepinex.sh\" %command%";
    let broken = format!(
            "{fixture}\n\t\"LaunchOptions\"\t\t\"\"\n\t\"/Applications/The\"\t\t\"Bazaar/run_bepinex.sh\"\n\t\"%command%\"\t\t\"\"\n",
            fixture = fixture_vdf()
        );

    let rendered = inject_launch_options(&broken, args).unwrap().unwrap();

    assert!(!rendered.contains("\"/Applications/The\""));
    assert!(!rendered.contains("\"%command%\"\t\t\"\""));
    assert!(rendered.contains(
        "\"LaunchOptions\"\t\t\"\\\"/Applications/The Bazaar/run_bepinex.sh\\\" %command%\""
    ));
}

#[test]
fn test_verify_launch_options_in_content_reports_mismatch() {
    let vdf_with_lo = fixture_vdf().replace(
        "\"LastPlayed\"",
        "\"LaunchOptions\"\t\t\"OLD_ARGS\"\n\t\t\t\t\t\"LastPlayed\"",
    );

    let verified = verify_launch_options_in_content(&vdf_with_lo, "EXPECTED_ARGS").unwrap();

    assert_eq!(verified, Some(false));
}

#[test]
fn test_find_localconfig_paths_returns_only_numeric_userdata_entries() {
    let tmp = tempfile::tempdir().unwrap();
    let valid = tmp.path().join("userdata/123456/config");
    let invalid = tmp.path().join("userdata/not-a-user/config");

    std::fs::create_dir_all(&valid).unwrap();
    std::fs::create_dir_all(&invalid).unwrap();
    std::fs::write(valid.join("localconfig.vdf"), fixture_vdf()).unwrap();
    std::fs::write(invalid.join("localconfig.vdf"), fixture_vdf()).unwrap();

    let paths = find_localconfig_paths(tmp.path());
    assert_eq!(paths, vec![valid.join("localconfig.vdf")]);
}

#[test]
fn test_patch_localconfigs_updates_matching_accounts_and_skips_others() {
    let tmp = tempfile::tempdir().unwrap();
    let with_app = tmp.path().join("userdata/123456/config");
    let without_app = tmp.path().join("userdata/234567/config");

    std::fs::create_dir_all(&with_app).unwrap();
    std::fs::create_dir_all(&without_app).unwrap();
    std::fs::write(with_app.join("localconfig.vdf"), fixture_vdf()).unwrap();
    std::fs::write(
        without_app.join("localconfig.vdf"),
        r#"
"UserLocalConfigStore"
{
    "Software"
    {
        "Valve"
        {
            "Steam"
            {
                "apps"
                {
                    "730"
                    {
                        "LastPlayed"    "1700000000"
                    }
                }
            }
        }
    }
}"#,
    )
    .unwrap();

    let updated = patch_localconfigs(tmp.path(), "MY_ARGS").unwrap();

    assert_eq!(updated, 1);
    let patched = std::fs::read_to_string(with_app.join("localconfig.vdf")).unwrap();
    assert!(patched.contains("LaunchOptions"));
    let untouched = std::fs::read_to_string(without_app.join("localconfig.vdf")).unwrap();
    assert!(!untouched.contains("LaunchOptions"));
}

#[test]
fn test_clear_launch_options_for_steam_ignores_missing_steam_directory() {
    let tmp = tempfile::tempdir().unwrap();
    let missing = tmp.path().join("missing-steam");

    let result = clear_launch_options_for_steam(&missing);

    assert!(result.is_ok());
}

#[cfg(target_os = "macos")]
#[test]
fn test_launch_options_args_uses_run_script_with_quoted_game_path() {
    let args = launch_options_args(Path::new("/Applications/The Bazaar"));
    assert_eq!(
        args,
        "\"/Applications/The Bazaar/run_bepinex.sh\" %command%"
    );
}

#[cfg(target_os = "macos")]
#[test]
fn test_ensure_launcher_executable_sets_execute_bits() {
    let tmp = tempfile::tempdir().unwrap();
    let script = tmp.path().join("run_bepinex.sh");
    std::fs::write(&script, "#!/bin/sh\n").unwrap();
    std::fs::set_permissions(&script, std::fs::Permissions::from_mode(0o644)).unwrap();

    ensure_launcher_executable(&script).unwrap();

    let mode = std::fs::metadata(&script).unwrap().permissions().mode();
    assert_eq!(mode & 0o111, 0o111);
}

#[cfg(target_os = "windows")]
#[test]
fn test_launch_options_args_is_empty_on_windows() {
    let args = launch_options_args(Path::new("C:\\Games\\The Bazaar"));
    assert!(args.is_empty());
}
