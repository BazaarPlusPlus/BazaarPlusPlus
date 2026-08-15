use crate::services::game_process;

/// End a game process that outlived its window. Both History and Install stall
/// on that state — one cannot read the mod database, the other refuses to touch
/// installed files — so the recovery lives outside either page.
///
/// Returns whether a process was actually terminated: finding none means the
/// state the caller was recovering from is already gone.
#[tauri::command]
#[specta::specta]
pub fn end_game_process() -> Result<bool, String> {
    game_process::terminate_game()
}
