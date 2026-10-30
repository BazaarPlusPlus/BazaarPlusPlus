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
