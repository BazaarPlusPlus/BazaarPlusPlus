// Prevents additional console window on Windows in release, DO NOT REMOVE!!
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

fn main() {
    if let Some(status) = bppinstaller_lib::run_headless(std::env::args_os().skip(1)) {
        std::process::exit(status);
    }
    bppinstaller_lib::run()
}
