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
            "Not bound · bind an email to view your records on bazaarplusplus.com",
            "尚未绑定 · 绑定邮箱后可在 bazaarplusplus.com 查看战绩",
            "尚未綁定 · 綁定信箱後可在 bazaarplusplus.com 查看戰績"
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

        private static readonly LocalizedTextSet BindButtonText = new(
            "Bind email",
            "绑定邮箱",
            "綁定信箱"
        );

        private static readonly LocalizedTextSet ActivateDeviceButtonText = new(
            "Activate this device",
            "在本设备启用",
            "在此裝置啟用"
        );

        private static readonly LocalizedTextSet CollapseText = new("Hide", "收起", "收起");

        private static readonly LocalizedTextSet Step1PromptText = new(
            "Bind current game account \"{0}\" to this email",
            "绑定当前游戏账号「{0}」到这个邮箱",
            "綁定目前遊戲帳號「{0}」到此信箱"
        );

        private static readonly LocalizedTextSet CredentialNoticeText = new(
            "BPP will send your current game login credential to the BPP server once solely to verify account ownership with the official server, then discard it immediately. The credential expires after about 24 hours.",
            "BPP 会把当前游戏登录凭证发到 BPP 服务器一次，只用于向官方确认账号归属，确认后立即丢弃；凭证约 24 小时后失效。",
            "BPP 會將目前遊戲登入憑證傳送到 BPP 伺服器一次，僅用於向官方確認帳號歸屬，確認後立即丟棄；憑證約 24 小時後失效。"
        );

        private static readonly LocalizedTextSet Step2PromptText = new(
            "Verification code sent to {0}. Confirming will bind \"{1}\".",
            "验证码已发到 {0}，将绑定「{1}」",
            "驗證碼已寄到 {0}，將綁定「{1}」"
        );

        private static readonly LocalizedTextSet ActivatePromptWithEmailText = new(
            "This account is already bound to {0}. Activate on this device?",
            "此账号已绑定 {0}，是否在本设备启用？",
            "此帳號已綁定 {0}，是否在此裝置啟用？"
        );

        private static readonly LocalizedTextSet ActivatePromptGenericText = new(
            "Activate your already-bound BPP Account on this device?",
            "此游戏账号已在其他设备绑定？确认后在本设备启用。",
            "此遊戲帳號已在其他裝置綁定？確認後在此裝置啟用。"
        );

        private static readonly LocalizedTextSet EmailPlaceholderText = new(
            "Enter email address",
            "请输入邮箱地址",
            "請輸入信箱地址"
        );

        private static readonly LocalizedTextSet CodePlaceholderText = new(
            "Enter 6-digit code",
            "请输入 6 位验证码",
            "請輸入 6 位驗證碼"
        );

        private static readonly LocalizedTextSet SendCodeButtonText = new(
            "Agree & send code",
            "同意并发送验证码",
            "同意並發送驗證碼"
        );

        private static readonly LocalizedTextSet ConfirmCodeButtonText = new(
            "Confirm bind",
            "确认绑定",
            "確認綁定"
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

        private static readonly LocalizedTextSet EmptyEmailText = new(
            "Enter a valid email address",
            "请输入有效的邮箱地址",
            "請輸入有效的信箱地址"
        );

        private static readonly LocalizedTextSet EmptyCodeText = new(
            "Enter the 6-digit verification code",
            "请输入 6 位数字验证码",
            "請輸入 6 位數字驗證碼"
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
            "This game account is not bound to an email yet",
            "当前游戏账号尚未绑定邮箱",
            "目前遊戲帳號尚未綁定信箱"
        );

        private static readonly LocalizedTextSet EmailAlreadyBoundText = new(
            "This email is already bound to another game account - unbind on bazaarplusplus.com first",
            "该邮箱已绑定其他游戏账号，请先前往 bazaarplusplus.com 解绑",
            "此信箱已綁定其他遊戲帳號，請先前往 bazaarplusplus.com 解綁"
        );

        private static readonly LocalizedTextSet InvalidCodeText = new(
            "Verification code is invalid, expired, or tried too many times",
            "验证码无效、已过期或错误次数过多",
            "驗證碼無效、已過期或錯誤次數過多"
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

        public static string Step1Prompt(string displayName) =>
            string.Format(CultureInfo.InvariantCulture, Resolve(Step1PromptText), displayName);

        public static string CredentialNotice() => Resolve(CredentialNoticeText);

        public static string Step2Prompt(string emailMasked, string userName) =>
            string.Format(
                CultureInfo.InvariantCulture,
                Resolve(Step2PromptText),
                emailMasked,
                userName
            );

        public static string ActivatePrompt(string? emailMasked) =>
            string.IsNullOrWhiteSpace(emailMasked)
                ? Resolve(ActivatePromptGenericText)
                : string.Format(
                    CultureInfo.InvariantCulture,
                    Resolve(ActivatePromptWithEmailText),
                    emailMasked
                );

        public static string EmailPlaceholder() => Resolve(EmailPlaceholderText);

        public static string CodePlaceholder() => Resolve(CodePlaceholderText);

        public static string SendCodeButton() => Resolve(SendCodeButtonText);

        public static string ConfirmCodeButton() => Resolve(ConfirmCodeButtonText);

        public static string ConfirmActivateButton() => Resolve(ConfirmActivateButtonText);

        public static string Working() => Resolve(WorkingText);

        public static string BindSucceeded() => Resolve(BindSucceededText);

        public static string DeviceActivated() => Resolve(DeviceActivatedText);

        public static string EmptyEmail() => Resolve(EmptyEmailText);

        public static string EmptyCode() => Resolve(EmptyCodeText);

        public static string GameAuthUnavailable() => Resolve(GameAuthUnavailableText);

        public static string MapError(string? code) =>
            code switch
            {
                "account_mismatch" => Resolve(AccountMismatchText),
                "game_token_rejected" => Resolve(GameTokenRejectedText),
                "game_api_unavailable" => Resolve(GameApiUnavailableText),
                "account_not_bound" => Resolve(AccountNotBoundText),
                "email_already_bound" => Resolve(EmailAlreadyBoundText),
                "invalid_verification_code" => Resolve(InvalidCodeText),
                "rate_limited" => Resolve(RateLimitedText),
                "invalid_body" => Resolve(EmptyEmailText),
                _ => Resolve(ServerUnavailableText),
            };
    }
}
