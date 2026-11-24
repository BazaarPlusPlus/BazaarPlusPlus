#nullable enable
using System.Globalization;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.HistoryPanel;

public static partial class HistoryPanelText
{
    public static class BppAccount
    {
        private static readonly LocalizedTextSet TitleText = new(
            "BPP Account",
            "BPP 账号",
            "BPP 帳號"
        );

        private static readonly LocalizedTextSet SignedOutText = new(
            "Sign in to The Bazaar to bind a BPP Account",
            "登录《The Bazaar》后即可绑定 BPP 账号",
            "登入《The Bazaar》後即可綁定 BPP 帳號"
        );

        private static readonly LocalizedTextSet NotBoundText = new(
            "Not bound · bind to view your records on bazaarplusplus.com",
            "尚未绑定 · 绑定后可在 bazaarplusplus.com 查看战绩",
            "尚未綁定 · 綁定後可在 bazaarplusplus.com 查看戰績"
        );

        private static readonly LocalizedTextSet BoundStatusText = new(
            "Bound to {0} ({1})",
            "已绑定 {0}（{1}）",
            "已綁定 {0}（{1}）"
        );

        private static readonly LocalizedTextSet BoundHintText = new(
            "View your records or unbind on bazaarplusplus.com",
            "前往 bazaarplusplus.com 查看战绩或解绑",
            "前往 bazaarplusplus.com 查看戰績或解綁"
        );

        private static readonly LocalizedTextSet BindButtonText = new("Bind", "绑定", "綁定");

        private static readonly LocalizedTextSet ActivateDeviceButtonText = new(
            "Activate this device",
            "在本设备启用",
            "在此裝置啟用"
        );

        private static readonly LocalizedTextSet CollapseText = new("Hide", "收起", "收起");

        private static readonly LocalizedTextSet BindCodePromptText = new(
            "Generate a bind code at bazaarplusplus.com/account",
            "在 bazaarplusplus.com/account 生成绑定码",
            "在 bazaarplusplus.com/account 產生綁定碼"
        );

        private static readonly LocalizedTextSet CredentialNoticeText = new(
            "BPP will send your current game login credential to the BPP server once solely to verify account ownership with the official server, then discard it immediately. The credential expires after about 24 hours.",
            "BPP 会把当前游戏登录凭证发到 BPP 服务器一次，只用于向官方确认账号归属，确认后立即丢弃；凭证约 24 小时后失效。",
            "BPP 會將目前遊戲登入憑證傳送到 BPP 伺服器一次，僅用於向官方確認帳號歸屬，確認後立即丟棄；憑證約 24 小時後失效。"
        );

        private static readonly LocalizedTextSet ActivatePromptText = new(
            "Activate your already-bound BPP Account on this device?",
            "此游戏账号已在其他设备绑定？确认后在本设备启用。",
            "此遊戲帳號已在其他裝置綁定？確認後在此裝置啟用。"
        );

        private static readonly LocalizedTextSet BindCodePlaceholderText = new(
            "Bind code",
            "绑定码",
            "綁定碼"
        );

        private static readonly LocalizedTextSet BindSubmitButtonText = new(
            "Agree & bind",
            "同意并绑定",
            "同意並綁定"
        );

        private static readonly LocalizedTextSet ConfirmActivateButtonText = new(
            "Confirm & activate",
            "确认并在本设备启用",
            "確認並在此裝置啟用"
        );

        private static readonly LocalizedTextSet WorkingText = new(
            "Working...",
            "处理中...",
            "處理中..."
        );

        private static readonly LocalizedTextSet BindSucceededText = new(
            "BPP Account bound",
            "BPP 账号绑定成功",
            "BPP 帳號綁定成功"
        );

        private static readonly LocalizedTextSet DeviceActivatedText = new(
            "Activated on this device",
            "已在本设备启用",
            "已在此裝置啟用"
        );

        private static readonly LocalizedTextSet EmptyBindCodeText = new(
            "Enter the bind code",
            "请输入绑定码",
            "請輸入綁定碼"
        );

        private static readonly LocalizedTextSet GameAuthUnavailableText = new(
            "Could not read the game login session - restart the game and retry",
            "无法读取当前游戏登录态，请重启游戏后再试",
            "無法讀取目前遊戲登入狀態，請重新啟動遊戲後再試"
        );

        private static readonly LocalizedTextSet AccountMismatchText = new(
            "Game account changed - restart the game and retry",
            "游戏账号已变化，请重启游戏后再试",
            "遊戲帳號已變更，請重新啟動遊戲後再試"
        );

        private static readonly LocalizedTextSet GameTokenRejectedText = new(
            "Game login session expired - restart the game and retry",
            "游戏登录态已失效，请重启游戏后再试",
            "遊戲登入狀態已失效，請重新啟動遊戲後再試"
        );

        private static readonly LocalizedTextSet GameApiUnavailableText = new(
            "Official game account service is unavailable - try again later",
            "官方账号服务暂时不可用，请稍后重试",
            "官方帳號服務暫時無法使用，請稍後重試"
        );

        private static readonly LocalizedTextSet AccountNotBoundText = new(
            "This game account is not bound yet",
            "当前游戏账号尚未绑定",
            "目前遊戲帳號尚未綁定"
        );

        private static readonly LocalizedTextSet GameAccountAlreadyBoundText = new(
            "This game account is bound to another BPP Account",
            "该游戏账号已绑定其他 BPP 账号",
            "此遊戲帳號已綁定其他 BPP 帳號"
        );

        private static readonly LocalizedTextSet InvalidBindCodeText = new(
            "Bind code is invalid or expired",
            "绑定码无效或已过期",
            "綁定碼無效或已過期"
        );

        private static readonly LocalizedTextSet RateLimitedText = new(
            "Too many requests - please try again later",
            "操作过于频繁，请稍后再试",
            "操作過於頻繁，請稍後再試"
        );

        private static readonly LocalizedTextSet ServerUnavailableText = new(
            "Could not reach BPP server - try again later",
            "暂时无法连接 BPP 服务器，请稍后重试",
            "暫時無法連線 BPP 伺服器，請稍後重試"
        );

        public static string Title() => Resolve(TitleText);

        public static string SignedOut() => Resolve(SignedOutText);

        public static string NotBound() => Resolve(NotBoundText);

        public static string BoundStatus(string emailMasked, string userName) =>
            string.Format(
                CultureInfo.InvariantCulture,
                Resolve(BoundStatusText),
                emailMasked,
                userName
            );

        public static string BoundHint() => Resolve(BoundHintText);

        public static string BindButton() => Resolve(BindButtonText);

        public static string ActivateDeviceButton() => Resolve(ActivateDeviceButtonText);

        public static string Collapse() => Resolve(CollapseText);

        public static string BindCodePrompt() => Resolve(BindCodePromptText);

        public static string CredentialNotice() => Resolve(CredentialNoticeText);

        public static string ActivatePrompt() => Resolve(ActivatePromptText);

        public static string BindCodePlaceholder() => Resolve(BindCodePlaceholderText);

        public static string BindSubmitButton() => Resolve(BindSubmitButtonText);

        public static string ConfirmActivateButton() => Resolve(ConfirmActivateButtonText);

        public static string Working() => Resolve(WorkingText);

        public static string BindSucceeded() => Resolve(BindSucceededText);

        public static string DeviceActivated() => Resolve(DeviceActivatedText);

        public static string EmptyBindCode() => Resolve(EmptyBindCodeText);

        public static string GameAuthUnavailable() => Resolve(GameAuthUnavailableText);

        public static string MapError(string? code) =>
            code switch
            {
                "account_mismatch" => Resolve(AccountMismatchText),
                "game_token_rejected" => Resolve(GameTokenRejectedText),
                "game_api_unavailable" => Resolve(GameApiUnavailableText),
                "account_not_bound" => Resolve(AccountNotBoundText),
                "game_account_already_bound" => Resolve(GameAccountAlreadyBoundText),
                "invalid_bind_code" => Resolve(InvalidBindCodeText),
                "rate_limited" => Resolve(RateLimitedText),
                _ => Resolve(ServerUnavailableText),
            };
    }
}
