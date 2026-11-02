import SwiftUI
import UIKit

// The launcher (design/components/Launcher): one page with the app bar, banners, the game card,
// actions and info. Below pane-breakpoint it is one column; at or above it, two equal panes,
// so the iPhone Duo unfolded (and iPad, and landscape) gets the two-pane layout and the folded
// phone gets one column. Accessibility text sizes keep one column.

// MARK: - Root

struct LauncherRoot: View {
    @EnvironmentObject var model: LauncherModel

    var body: some View {
        LauncherPage()
            .preferredColorScheme(.dark)
            .tint(BppColor.accent)
            .sheet(isPresented: $model.showingAccount) { AccountSheet().environmentObject(model).pageSizedSheet() }
            .fullScreenCover(isPresented: $model.showingOnboarding) { OnboardingView().environmentObject(model) }
            .task { await model.loadArtwork() }
            .task { await model.checkForUpdate() }
    }
}

struct LauncherPage: View {
    @EnvironmentObject var model: LauncherModel
    @Environment(\.dynamicTypeSize) private var typeSize

    var body: some View {
        GeometryReader { proxy in
            let panes = proxy.size.width >= BppSize.paneBreakpoint && !typeSize.isAccessibilitySize
            ScrollView {
                VStack(alignment: .leading, spacing: BppSpace.space16) {
                    AppBar()
                    Banners()
                    if panes {
                        // Equal panes, centred: a book-style fold falls in the 24pt gutter.
                        HStack(alignment: .top, spacing: BppSpace.space24) {
                            GameCard().frame(maxWidth: .infinity)
                            VStack(alignment: .leading, spacing: BppSpace.space16) {
                                ActionPanel()
                                InfoPanel()
                            }
                            .frame(maxWidth: .infinity)
                        }
                    } else {
                        GameCard()
                        ActionPanel()
                        InfoPanel()
                    }
                }
                .frame(maxWidth: BppSize.launcherMax)
                .padding(.horizontal, panes ? BppSpace.space24 : BppSpace.space16)
                .padding(.bottom, BppSpace.space24)
                .frame(maxWidth: .infinity)
            }
        }
        .background(BppColor.bgCanvas.ignoresSafeArea())
    }
}

// MARK: - App bar

struct AppBar: View {
    @EnvironmentObject var model: LauncherModel

    var body: some View {
        HStack(spacing: BppSpace.space8) {
            BppMark(size: 24)
            Text(model.appName).bppFont(BppType.bodyStrong).foregroundColor(BppColor.text1)
                .accessibilityAddTraits(.isHeader)
            Spacer(minLength: BppSpace.space8)
            AccountChip()
        }
        .frame(minHeight: BppSize.appbarHeight)
    }
}

/// The Steam account: a `control-md` pill with a status dot, its hit area `touch-target`.
struct AccountChip: View {
    @EnvironmentObject var model: LauncherModel

    var body: some View {
        Button { model.showingAccount = true } label: {
            HStack(spacing: BppSpace.space8) {
                Circle().fill(model.snapshot.loggedIn ? BppColor.success : BppColor.text3)
                    .frame(width: BppSpace.space8, height: BppSpace.space8)
                Text(label).bppFont(BppType.small).foregroundColor(BppColor.text1).lineLimit(1)
            }
            .padding(.horizontal, BppSpace.space12)
            .frame(minHeight: BppSize.controlMd)
            .background(Capsule().fill(BppColor.bgPanel))
            .overlay(Capsule().strokeBorder(BppColor.line))
            .frame(minHeight: BppSize.touchTarget)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityIdentifier("account")
    }

    private var label: String {
        let s = model.snapshot
        if s.loggedIn { return s.account.isEmpty ? "Steam 已连接" : s.account }
        return model.isSigningIn ? "正在登录…" : "登录 Steam"
    }
}

// MARK: - Banners

/// The App update first, then a failure.
struct Banners: View {
    @EnvironmentObject var model: LauncherModel

    var body: some View {
        if let update = model.update {
            if let url = update.url {
                BppBanner(tone: .info, message: "BazaarPlusPlus v\(update.version) 已发布。", actionTitle: "下载") {
                    UIApplication.shared.open(url)
                }
            } else {
                BppBanner(tone: .info, message: "BazaarPlusPlus v\(update.version) 已发布。")
            }
        }
        if let failure = model.failure {
            BppBanner(tone: .error, message: failure)
        } else if model.snapshot.state == "error", !model.snapshot.message.isEmpty {
            BppBanner(tone: .error, message: model.snapshot.message)
        }
    }
}

// MARK: - Game card

struct GameCard: View {
    @EnvironmentObject var model: LauncherModel

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            cover
            VStack(alignment: .leading, spacing: BppSpace.space8) {
                Text(model.snapshot.game?.title ?? "The Bazaar").bppFont(BppType.title).foregroundColor(BppColor.text1)
                if let status = model.status {
                    HStack {
                        BppBadge(word: status.word, tone: status.tone).accessibilityIdentifier("launcher-status")
                        Spacer(minLength: BppSpace.space8)
                        if let percent = model.downloadPercent {
                            Text("\(percent)%").bppFont(BppType.caption).monospacedDigit().foregroundColor(BppColor.text2)
                        }
                    }
                }
            }
            .padding(BppSpace.space16)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .bppPanel(radius: BppRadius.rDialog)
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier(model.snapshot.game?.id ?? "game")
    }

    /// The Steam store's library hero, centre-cropped to 2:1; plain bg-hover until it loads.
    private var cover: some View {
        let store = model.snapshot.game.flatMap { model.store($0) }
        return ArtworkImage(url: store?.hero, appId: model.snapshot.game.flatMap { model.appId($0) }, maxPixel: 1800) {
            BppColor.bgHover
        }
        .aspectRatio(2, contentMode: .fit)
        .frame(maxWidth: .infinity)
        .background(BppColor.bgHover)
        .accessibilityHidden(true)
    }
}

/// Remote store art that never pushes the layout: the container decides the size.
struct ArtworkImage<Placeholder: View>: View {
    @EnvironmentObject var model: LauncherModel
    let url: URL?
    let appId: UInt32?
    let maxPixel: CGFloat
    @ViewBuilder let placeholder: () -> Placeholder
    @State private var image: UIImage?

    var body: some View {
        Color.clear
            .overlay {
                if let image {
                    Image(uiImage: image).resizable().aspectRatio(contentMode: .fill).transition(.opacity)
                } else {
                    placeholder()
                }
            }
            .clipped()
            .task(id: url) {
                guard let url, let appId else { image = nil; return }
                let loaded = await model.artwork.image(url, appId: appId, maxPixel: maxPixel)
                withAnimation(.easeOut(duration: 0.16)) { image = loaded }
            }
    }
}

// MARK: - Actions

struct ActionPanel: View {
    @EnvironmentObject var model: LauncherModel

    var body: some View {
        if let game = model.snapshot.game, model.failure == nil {
            let action = model.action(for: game)
            VStack(alignment: .leading, spacing: BppSpace.space8) {
                switch action {
                case .working:
                    progress
                    Button("取消") { model.cancel() }
                        .buttonStyle(BppButtonStyle(kind: .secondary))
                        .accessibilityIdentifier("cancel-operation")
                case .blocked(let reason), .unavailable(let reason):
                    note(reason)
                default:
                    Button(primaryTitle(action)) { model.perform(game) }
                        .buttonStyle(BppButtonStyle(kind: .primary))
                        .accessibilityIdentifier(identifier(action))
                    if action == .connect && !model.snapshot.savedAccount.isEmpty {
                        Button("继续以 \(model.snapshot.savedAccount) 登录") { model.restore() }
                            .buttonStyle(BppButtonStyle(kind: .secondary))
                            .accessibilityIdentifier("restore")
                    }
                }
            }
        }
    }

    /// The download's track and its numbers; other work shows only the busy badge.
    @ViewBuilder private var progress: some View {
        if model.snapshot.state == "downloading" {
            GeometryReader { proxy in
                ZStack(alignment: .leading) {
                    Capsule().fill(BppColor.bgHover)
                    Capsule().fill(BppColor.accent)
                        .frame(width: proxy.size.width * min(1, max(0, model.snapshot.progress)))
                }
            }
            .frame(height: BppSize.progressHeight)
            .accessibilityElement()
            .accessibilityLabel("下载进度")
            .accessibilityValue("\(model.downloadPercent ?? 0)%")
            if let detail = model.downloadDetail {
                Text(detail).bppFont(BppType.caption).monospacedDigit().foregroundColor(BppColor.text2)
                    .padding(.bottom, BppSpace.space8)
            }
        }
    }

    private func note(_ text: String) -> some View {
        Text(text).bppFont(BppType.caption).foregroundColor(BppColor.text2)
            .fixedSize(horizontal: false, vertical: true)
    }

    private func primaryTitle(_ action: GameAction) -> String {
        switch action {
        case .play: return "开始游戏"
        case .connect: return "连接 Steam"
        case .download: return "下载游戏"
        default: return "校验并准备游戏"
        }
    }

    private func identifier(_ action: GameAction) -> String {
        switch action {
        case .play: return "play"
        case .connect: return "connect"
        default: return "download"
        }
    }
}

// MARK: - Info

struct InfoPanel: View {
    @EnvironmentObject var model: LauncherModel

    var body: some View {
        VStack(spacing: 0) {
            BppKeyValueRow(key: "适配版本", value: model.snapshot.game?.shortVersion ?? "")
            BppKeyValueRow(key: "BazaarPlusPlus", value: model.appVersion, last: true)
        }
        .bppPanel()
    }
}

extension View {
    /// The default iPad form sheet is too short for the QR step; page size keeps its actions in view.
    @ViewBuilder func pageSizedSheet() -> some View {
        if #available(iOS 18.0, *) { presentationSizing(.page) } else { self }
    }
}
