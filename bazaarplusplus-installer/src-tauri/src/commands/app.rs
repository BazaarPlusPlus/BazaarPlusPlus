use serde::{Deserialize, Serialize};
use tauri::{AppHandle, Manager};

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

#[derive(Clone, Debug, Serialize, specta::Type)]
pub struct AppBootstrap {
    pub app_version: String,
    pub bundled_bpp_version: Option<String>,
    pub links: AppLinks,
    pub credits: Vec<AppCredit>,
    pub licenses: Vec<AppLicense>,
}

#[derive(Clone, Debug, Serialize, Deserialize, specta::Type)]
pub struct AppLinks {
    pub github: String,
    pub x: String,
    pub bilibili_project: String,
    pub bilibili_author: String,
    pub bilibili_core_dev: String,
    pub xiaohongshu: String,
    pub kofi: String,
    pub supporter_list: String,
}

#[derive(Clone, Debug, Serialize, Deserialize, specta::Type)]
pub struct AppCredit {
    pub name: String,
    pub role: String,
    pub href: Option<String>,
    pub group: String,
}

#[derive(Clone, Debug, Serialize, Deserialize, specta::Type)]
pub struct AppLicense {
    pub name: String,
    pub license: String,
    pub category: String,
}

#[tauri::command]
#[specta::specta]
pub async fn get_app_bootstrap(app: AppHandle) -> AppBootstrap {
    // The first call can be the one that detects Steam and reads the bundled
    // payload, or waits for the startup task doing so.
    tauri::async_runtime::spawn_blocking(move || {
        let startup = app.state::<InstallerContextState>().get_or_initialize(&app);
        let static_content = load_static_bootstrap();
        AppBootstrap {
            app_version: env!("CARGO_PKG_VERSION").to_string(),
            bundled_bpp_version: startup.bundled_bpp_version.clone(),
            links: static_content.links,
            credits: static_content.credits,
            licenses: static_content.licenses,
        }
    })
    .await
    .expect("app bootstrap task panicked")
}
