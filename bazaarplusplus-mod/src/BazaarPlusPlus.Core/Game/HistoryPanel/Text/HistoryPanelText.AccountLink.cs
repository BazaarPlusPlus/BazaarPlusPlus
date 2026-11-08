#nullable enable

using System.Globalization;
using BazaarPlusPlus.Game.Screenshots;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.HistoryPanel;

public static partial class HistoryPanelText
{
    public static class AccountLink
    {
        private static readonly LocalizedTextSet UploadsDisabledText = new(
            "Turn on “{0}” in the BazaarPlusPlus settings to link.",
            "请先在 BazaarPlusPlus 设置中打开『{0}』，再绑定账号。",
            "請先在 BazaarPlusPlus 設定中開啟『{0}』，再綁定帳號。"
        );

        private static readonly LocalizedTextSet PtrUnavailableText = new(
            "PTR and staging builds do not support BazaarDB uploads or account linking.",
            "PTR 和 staging 测试版本不支持 BazaarDB 上传和账号绑定。"
        );

        private static readonly LocalizedTextSet BindingPersistsText = new(
            "The link follows your game account. No need to re-link on another device.",
            "绑定跟随游戏账号，换设备无需重绑。",
            "綁定跟隨遊戲帳號，換裝置無需重綁。"
        );

        private static readonly LocalizedTextSet BindingOnceText = new(
            "Link once per game account.\nAlready linked this account? Choose “Already linked”.",
            "绑定跟随游戏账号，只需一次。\n已绑定过？点击「已经绑定」。",
            "綁定跟隨遊戲帳號，只需一次。\n已綁定過？點選「已經綁定」。"
        );

        private static readonly LocalizedTextSet TitleText = new(
            "BazaarDB account",
            "BazaarDB 账号",
            "BazaarDB 帳號"
        );

        private static readonly LocalizedTextSet WhyText = new(
            "See your runs and stats on bazaardb.gg",
            "绑定后在 bazaardb.gg 查看你的对局与战绩",
            "綁定後在 bazaardb.gg 查看你的對局與戰績"
        );

        private static readonly LocalizedTextSet SignedOutText = new(
            "Sign in to The Bazaar to link",
            "登录《The Bazaar》后即可绑定",
            "登入《The Bazaar》後即可綁定"
        );

        private static readonly LocalizedTextSet HintText = new(
            "Get a code at bazaardb.gg · valid 10 min · case-sensitive",
            "前往 bazaardb.gg 获取绑定码 · 10 分钟有效 · 区分大小写",
            "前往 bazaardb.gg 取得綁定碼 · 10 分鐘有效 · 區分大小寫"
        );

        private static readonly LocalizedTextSet ButtonText = new(
            "Confirm link",
            "确认绑定",
            "確認綁定"
        );

        private static readonly LocalizedTextSet AlreadyLinkedElsewhereButtonText = new(
            "Already linked",
            "已经绑定",
            "已經綁定"
        );

        private static readonly LocalizedTextSet LinkingText = new(
            "Linking...",
            "绑定中...",
            "綁定中..."
        );

        private static readonly LocalizedTextSet LinkedText = new("Linked", "已绑定", "已綁定");

        private static readonly LocalizedTextSet NotLinkedText = new(
            "Link not confirmed",
            "尚未确认绑定",
            "尚未確認綁定"
        );

        private static readonly LocalizedTextSet RelinkText = new(
            "Re-link",
            "重新绑定",
            "重新綁定"
        );

        private static readonly LocalizedTextSet RowBindText = new(
            "Enter code",
            "输入绑定码",
            "輸入綁定碼"
        );

        private static readonly LocalizedTextSet CollapseText = new("Hide", "收起", "收起");

        private static readonly LocalizedTextSet EmptyCodeText = new(
            "Enter your link code",
            "请输入绑定码",
            "請輸入綁定碼"
        );

        private static readonly LocalizedTextSet AlreadyRunningText = new(
            "Linking is already in progress.",
            "绑定正在进行中。",
            "綁定正在進行中。"
        );

        private static readonly LocalizedTextSet InvalidOrExpiredText = new(
            "Code invalid or expired - generate a new one",
            "绑定码无效或已过期，请重新生成",
            "綁定碼無效或已過期，請重新產生"
        );

        private static readonly LocalizedTextSet AlreadyLinkedText = new(
            "This game account is already linked to another BazaarDB user",
            "该游戏账号已绑定到其他 BazaarDB 用户",
            "該遊戲帳號已綁定到其他 BazaarDB 使用者"
        );

        private static readonly LocalizedTextSet ServerBusyText = new(
            "BazaarDB is unavailable - try again",
            "BazaarDB 暂时不可用，请稍后重试",
            "BazaarDB 暫時無法使用，請稍後重試"
        );

        private static readonly LocalizedTextSet OfflineText = new(
            "Could not reach BazaarDB - check your connection",
            "无法连接 BazaarDB，请检查网络",
            "無法連線 BazaarDB，請檢查網路"
        );

        public static string Title() => Resolve(TitleText);

        public static string UploadsDisabled() =>
            string.Format(
                CultureInfo.InvariantCulture,
                Resolve(UploadsDisabledText),
                BazaarDbBundleSettingsMenuLabel.Resolve(L.CurrentLanguageCode)
            );

        public static string PtrUnavailable() => Resolve(PtrUnavailableText);

        public static string BindingPersists() => Resolve(BindingPersistsText);

        public static string BindingOnce() => Resolve(BindingOnceText);

        public static string Why() => Resolve(WhyText);

        public static string SignedOut() => Resolve(SignedOutText);

        public static string Hint() => Resolve(HintText);

        public static string Button() => Resolve(ButtonText);

        public static string AlreadyLinkedElsewhereButton() =>
            Resolve(AlreadyLinkedElsewhereButtonText);

        public static string Linking() => Resolve(LinkingText);

        public static string Linked() => Resolve(LinkedText);

        public static string NotLinked() => Resolve(NotLinkedText);

        public static string Relink() => Resolve(RelinkText);

        public static string RowBind() => Resolve(RowBindText);

        public static string Collapse() => Resolve(CollapseText);

        public static string EmptyCode() => Resolve(EmptyCodeText);

        public static string AlreadyRunning() => Resolve(AlreadyRunningText);

        public static string InvalidOrExpired() => Resolve(InvalidOrExpiredText);

        public static string AlreadyLinked() => Resolve(AlreadyLinkedText);

        public static string ServerBusy() => Resolve(ServerBusyText);

        public static string Offline() => Resolve(OfflineText);
    }
}
