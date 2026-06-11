use serde::{Deserialize, Serialize};
use tauri::AppHandle;

use crate::services::startup::InstallerContextState;

#[derive(Debug, Deserialize)]
struct AppBootstrapStatic {
    links: AppLinks,
    credits: Vec<AppCredit>,
    licenses: Vec<AppLicense>,
}

fn load_static_bootstrap() -> AppBootstrapStatic {
    const RAW: &str = include_str!("../../resources/app-bootstrap.json");
    serde_json::from_str(RAW).expect("app-bootstrap.json must parse")
}

#[derive(Clone, Debug, Serialize, ts_rs::TS)]
#[ts(export)]
pub struct AppBootstrap {
    pub app_version: String,
    pub bundled_bpp_version: Option<String>,
    pub links: AppLinks,
    pub credits: Vec<AppCredit>,
    pub licenses: Vec<AppLicense>,
}

#[derive(Clone, Debug, Serialize, Deserialize, ts_rs::TS)]
#[ts(export)]
pub struct AppLinks {
    pub github: String,
    pub x: String,
    pub bilibili_project: String,
    pub bilibili_author: String,
    pub xiaohongshu: String,
    pub kofi: String,
    pub supporter_list: String,
}

#[derive(Clone, Debug, Serialize, Deserialize, ts_rs::TS)]
#[ts(export)]
pub struct AppCredit {
    pub name: String,
    pub role: String,
}

#[derive(Clone, Debug, Serialize, Deserialize, ts_rs::TS)]
#[ts(export)]
pub struct AppLicense {
    pub name: String,
    pub license: String,
    pub category: String,
}

#[tauri::command(async)]
pub fn get_app_bootstrap(
    app: AppHandle,
    state: tauri::State<'_, InstallerContextState>,
) -> AppBootstrap {
    let startup = state.get_or_initialize(&app);
    let static_content = load_static_bootstrap();
    AppBootstrap {
        app_version: env!("CARGO_PKG_VERSION").to_string(),
        bundled_bpp_version: startup.bundled_bpp_version.clone(),
        links: static_content.links,
        credits: static_content.credits,
        licenses: static_content.licenses,
    }
}
