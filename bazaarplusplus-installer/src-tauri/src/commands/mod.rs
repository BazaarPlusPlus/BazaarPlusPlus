pub mod bepinex;
pub mod detect;
pub mod game;
pub mod steam;
pub mod supporters;
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
