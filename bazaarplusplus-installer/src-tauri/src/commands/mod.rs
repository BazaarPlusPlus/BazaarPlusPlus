pub mod bepinex;
pub mod detect;
pub mod game_process;
pub mod startup;
pub mod steam;
pub mod stream;
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

pub(super) use debug_error;
pub(super) use debug_log;
