use super::launch_options::*;
use super::parse::*;

fn fixture_vdf(launch_options: Option<&str>) -> String {
    let launch_line = launch_options
        .map(|value| format!("                        \"LaunchOptions\"    \"{value}\"\n"))
        .unwrap_or_default();
    format!(
        r#"
"UserLocalConfigStore"
{{
    "Software"
    {{
        "Valve"
        {{
            "Steam"
            {{
                "apps"
                {{
                    "1617400"
                    {{
{launch_line}                        "LastPlayed"    "1700000000"
                    }}
                }}
            }}
        }}
    }}
}}"#
    )
}

fn write_localconfig(root: &std::path::Path, user: &str, content: &str) {
    let config = root.join("userdata").join(user).join("config");
    std::fs::create_dir_all(&config).unwrap();
    std::fs::write(config.join("localconfig.vdf"), content).unwrap();
}

#[test]
fn launch_options_absent_or_empty_is_clean() {
    assert_eq!(
        launch_options_empty_in_content(&fixture_vdf(None)).unwrap(),
        Some(true)
    );
    assert_eq!(
        launch_options_empty_in_content(&fixture_vdf(Some(""))).unwrap(),
        Some(true)
    );
}

#[test]
fn any_non_empty_launch_options_are_dirty_without_content_matching() {
    for value in [
        "--developer-mode",
        "\\\"/some/path/run_bepinex.sh\\\" %command%",
        "%command% --custom",
    ] {
        assert_eq!(
            launch_options_empty_in_content(&fixture_vdf(Some(value))).unwrap(),
            Some(false),
            "{value}"
        );
    }
}

#[test]
fn missing_app_is_logically_empty() {
    let other_app = fixture_vdf(None).replace("\"1617400\"", "\"730\"");
    assert_eq!(launch_options_empty_in_content(&other_app).unwrap(), None);
}

#[test]
fn nested_launch_options_are_not_the_bazaar_property() {
    let nested = fixture_vdf(None).replace(
        "                        \"LastPlayed\"",
        "                        \"Cloud\"\n                        {\n                            \"LaunchOptions\"    \"nested\"\n                        }\n                        \"LastPlayed\"",
    );
    assert_eq!(
        launch_options_empty_in_content(&nested).unwrap(),
        Some(true)
    );
}

#[test]
fn clear_removes_every_direct_launch_options_entry() {
    let duplicate = fixture_vdf(Some("FIRST")).replace(
        "                        \"LastPlayed\"",
        "                        \"LaunchOptions\"    \"SECOND\"\n                        \"LastPlayed\"",
    );
    let cleared = clear_launch_options(&duplicate).unwrap().unwrap();
    assert!(!cleared.contains("\"LaunchOptions\""));
    assert_eq!(
        launch_options_empty_in_content(&cleared).unwrap(),
        Some(true)
    );
}

#[test]
fn steam_inspection_checks_every_numeric_account() {
    let tmp = tempfile::tempdir().unwrap();
    write_localconfig(tmp.path(), "100", &fixture_vdf(None));
    write_localconfig(tmp.path(), "200", &fixture_vdf(Some("CUSTOM")));
    write_localconfig(tmp.path(), "not-a-user", &fixture_vdf(Some("IGNORED")));

    assert_eq!(
        inspect_launch_options_for_steam(tmp.path()),
        SteamLaunchOptionsState::NonEmpty
    );
}

#[test]
fn clear_updates_all_accounts_and_verifies_the_result() {
    let tmp = tempfile::tempdir().unwrap();
    write_localconfig(tmp.path(), "100", &fixture_vdf(Some("FIRST")));
    write_localconfig(tmp.path(), "200", &fixture_vdf(Some("SECOND")));

    clear_launch_options_for_steam(tmp.path()).unwrap();

    assert_eq!(
        inspect_launch_options_for_steam(tmp.path()),
        SteamLaunchOptionsState::Empty
    );
    for path in find_localconfig_paths(tmp.path()) {
        assert!(!std::fs::read_to_string(path)
            .unwrap()
            .contains("\"LaunchOptions\""));
    }
}

#[test]
fn unavailable_localconfig_is_not_treated_as_clean() {
    let tmp = tempfile::tempdir().unwrap();
    assert_eq!(
        inspect_launch_options_for_steam(tmp.path()),
        SteamLaunchOptionsState::Unavailable
    );
    assert!(clear_launch_options_for_steam(tmp.path()).is_err());
}

#[test]
fn malformed_vdf_is_unavailable() {
    let tmp = tempfile::tempdir().unwrap();
    write_localconfig(tmp.path(), "100", "not a Steam VDF");
    assert_eq!(
        inspect_launch_options_for_steam(tmp.path()),
        SteamLaunchOptionsState::Unavailable
    );
}
