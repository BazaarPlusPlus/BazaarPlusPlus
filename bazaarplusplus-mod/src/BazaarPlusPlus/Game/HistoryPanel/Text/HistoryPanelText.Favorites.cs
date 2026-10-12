#nullable enable

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static partial class HistoryPanelText
{
    internal static string FavoritesTab() => FormatSimple("Favorites", "收藏", "收藏");

    internal static string FavoriteBattle() => FormatSimple("Favorite", "收藏此战", "收藏此戰");

    internal static string FavoritedBattle() => FormatSimple("Favorited", "已收藏", "已收藏");

    internal static string EditFavoriteNote() => FormatSimple("Edit note", "编辑备注", "編輯備註");

    internal static string RemoveFavorite() => FormatSimple("Remove", "移除收藏", "移除收藏");

    internal static string RemoveFavoriteConfirm() =>
        FormatSimple("Confirm remove", "确认移除", "確認移除");

    internal static string RemoveFavoriteConfirmPrompt() =>
        FormatSimple(
            "Press Remove again to delete this favorite, its note and its replay copy.",
            "再点一次“确认移除”会删除这条收藏、备注和回放副本。",
            "再點一次「確認移除」會刪除這條收藏、備註和回放副本。"
        );

    internal static string FavoriteRemoved() =>
        FormatSimple("Favorite removed.", "已移除收藏。", "已移除收藏。");

    internal static string SavingFavorite() =>
        FormatSimple("Saving favorite…", "正在收藏…", "正在收藏…");

    internal static string FavoriteSaved() =>
        FormatSimple(
            "Saved to Favorites with its replay.",
            "已收藏，回放一并保存。",
            "已收藏，回放一併保存。"
        );

    internal static string FavoriteSavedWithoutReplay() =>
        FormatSimple(
            "Saved to Favorites. Its replay was not available, so only the boards were kept.",
            "已收藏。这场的回放已不可用，只保存了阵容。",
            "已收藏。這場的回放已不可用，只保存了陣容。"
        );

    internal static string FavoriteBusy() =>
        FormatSimple(
            "Replay files are busy right now. Try again in a moment.",
            "回放文件正在使用中，请稍后再试。",
            "回放檔案正在使用中，請稍後再試。"
        );

    internal static string FavoriteFailed(string error) =>
        FormatSimple(
            $"Could not save the favorite: {error}",
            $"收藏失败：{error}",
            $"收藏失敗：{error}"
        );

    internal static string FavoriteBattleNotFound() =>
        FormatSimple(
            "this battle is no longer in History",
            "这场战斗已不在战绩里",
            "這場戰鬥已不在戰績裡"
        );

    internal static string FavoriteReplayUnavailable() =>
        FormatSimple(
            "This favorite was saved without a replay.",
            "这条收藏没有保存回放。",
            "這條收藏沒有保存回放。"
        );

    internal static string FavoriteBoardsUnavailable() =>
        FormatSimple(
            "This favorite has no saved boards.",
            "这条收藏没有保存阵容。",
            "這條收藏沒有保存陣容。"
        );

    internal static string NoFavorites() =>
        FormatSimple(
            "No favorites yet. Select a battle under Runs or Ghost and press Favorite.",
            "还没有收藏。在“对局”或“幽灵”里选中一场战斗，点“收藏此战”。",
            "還沒有收藏。在「對局」或「幽靈」裡選中一場戰鬥，點「收藏此戰」。"
        );

    internal static string FavoritesReadFailed() =>
        FormatSimple("Favorites could not be read.", "收藏读取失败。", "收藏讀取失敗。");

    internal static string FavoriteNoteTitle() => FormatSimple("Note", "备注", "備註");

    internal static string FavoriteNotePlaceholder() =>
        FormatSimple(
            "Why this battle is worth keeping…",
            "写点什么，比如这场为什么值得收藏…",
            "寫點什麼，比如這場為什麼值得收藏…"
        );

    internal static string SaveNote() => FormatSimple("Save", "保存", "保存");

    internal static string Cancel() => FormatSimple("Cancel", "取消", "取消");

    internal static string FavoriteNoteSaved() =>
        FormatSimple("Note saved.", "备注已保存。", "備註已保存。");

    internal static string FavoriteNoteSaveFailed(string error) =>
        FormatSimple(
            $"Could not save the note: {error}",
            $"备注保存失败：{error}",
            $"備註保存失敗：{error}"
        );

    internal static string FavoriteNoteCount(int length, int maximum) => $"{length}/{maximum}";

    internal static string FavoritedOn(string timestamp) =>
        FormatSimple($"Favorited {timestamp}", $"收藏于 {timestamp}", $"收藏於 {timestamp}");
}
