pub mod bepinex;
pub mod detect;
pub mod game_path;
pub mod game_process;
pub mod history;
pub mod install;
pub mod path;
pub mod paths;
pub mod process_snapshot;
pub mod selected_game_installation;
pub mod startup;
pub mod steam;
pub mod vdf;

macro_rules! debug_log {
    ($($arg:tt)*) => {
        #[cfg(debug_assertions)]
        println!($($arg)*);
    };
}

macro_rules! debug_error {
    ($($arg:tt)*) => {
        #[cfg(debug_assertions)]
        eprintln!($($arg)*);
    };
}

pub(crate) use debug_error;
pub(crate) use debug_log;
