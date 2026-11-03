import SwiftUI
import UIKit

// The launcher (design/components/Launcher): the cover full bleed under the status bar, a panel
// rising over its bottom edge with the title line, the stats, the Steam account row and the
// primary pinned at the bottom, and one floating More button. Below pane-breakpoint it is one
// column; up to cover-pane-breakpoint the panel splits into two equal panes under the cover (the
// iPhone Duo unfolded); wider, the cover and the content sit side by side (iPad). Accessibility
// text sizes keep one column. The launcher is portrait only (app/Host/main.mm).

// MARK: - Root

struct LauncherRoot: View {
    @EnvironmentObject var model: LauncherModel

    var body: some View {
        LauncherPage()
            .preferredColorScheme(.dark)
            .tint(BppColor.accent)
            .sheet(isPresented: $model.showingAccount) { AccountSheet().environmentObject(model).pageSizedSheet() }
            .fullScreenCover(isPresented: $model.showingOnboarding) { OnboardingView().environmentObject(model) }
            .task { await model.checkForUpdate() }
    }
}

struct LauncherPage: View {
    @EnvironmentObject var model: LauncherModel
    @Environment(\.dynamicTypeSize) private var typeSize
    @State private var showingAbout = false

    var body: some View {
        GeometryReader { proxy in
            // The window's width: the iPhone Duo folded keeps its status bar in a side inset.
            let width = proxy.size.width + proxy.safeAreaInsets.leading + proxy.safeAreaInsets.trailing
            let large = typeSize.isAccessibilitySize
            let wide = !large && width >= BppSize.coverPaneBreakpoint
            ZStack(alignment: .topTrailing) {
                if wide {
                    WideLayout(insets: proxy.safeAreaInsets, height: proxy.size.height)
                } else {
                    StackedLayout(insets: proxy.safeAreaInsets, width: width,
                                  height: proxy.size.height, panes: !large && width >= BppSize.paneBreakpoint)
                }
                MoreButton(bare: wide, showingAbout: $showingAbout)
                    .padding(.top, BppSpace.space8)
                    .padding(.trailing, BppSpace.space8)
            }
        }
        .background(BppColor.bgCanvas.ignoresSafeArea())
        .alert(model.appName, isPresented: $showingAbout) {
            Button("打开网站") { UIApplication.shared.open(LauncherModel.website) }
            Button("关闭", role: .cancel) {}
        } message: {
            Text("版本 \(model.appVersion.dropFirst()) · bazaarplusplus.com")
        }
    }
}

/// One column, or the cover over two panes: the panel takes the height it needs and the cover the
/// rest, at least half its width. When the panel needs more, the page scrolls as one.
private struct StackedLayout: View {
    let insets: EdgeInsets
    let width: CGFloat
    let height: CGFloat
    let panes: Bool

    private var gutter: CGFloat { panes ? BppSpace.space24 : BppSpace.space16 }

    var body: some View {
        ScrollView {
            VStack(spacing: -BppSpace.space24) {
                Cover()
                    .frame(maxWidth: .infinity, minHeight: width / 2, maxHeight: .infinity)
                Panel(panes: panes)
                    .padding(.leading, insets.leading + gutter)
                    .padding(.trailing, insets.trailing + gutter)
                    .padding(.top, BppSpace.space24)
                    .padding(.bottom, insets.bottom + BppSpace.space16)
                    .frame(maxWidth: .infinity)
                    .background(BppTopRoundedRectangle(radius: BppRadius.rDialog).fill(BppColor.bgCanvas))
            }
            .frame(minHeight: height + insets.top + insets.bottom)
        }
        .ignoresSafeArea(.container)
    }
}

/// From cover-pane-breakpoint: the cover fills the start half at full height, the content the end
/// half, vertically centred.
private struct WideLayout: View {
    let insets: EdgeInsets
    let height: CGFloat

    var body: some View {
        HStack(spacing: 0) {
            Cover()
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .ignoresSafeArea()
            ScrollView {
                WidePanel()
                    .padding(BppSpace.space24)
                    .frame(maxWidth: .infinity, minHeight: height)
            }
            .frame(maxWidth: .infinity)
        }
    }
}

// MARK: - Cover and More

/// A wide cover (short, when the panel takes most of the window) shows the library hero cropped
/// around the hunter's face; any other cover (phones, the iPhone Duo, the iPad) shows the
/// promotional hero capsule from its top, logo in view. Plain bg-hover until the art decodes.
private struct Cover: View {
    /// Width over height from which a cover counts as wide.
    static let wide: CGFloat = 1.2
    /// The hunter's face in the current library hero.
    static let heroFocus = UnitPoint(x: 0.70, y: 0.30)
    /// The top of the hero capsule, at the centre of its logo.
    static let promoFocus = UnitPoint(x: 0.53, y: 0)

    var body: some View {
        GeometryReader { proxy in
            let wide = proxy.size.width >= proxy.size.height * Self.wide
            ArtworkImage(artwork: wide ? .hero : .promo, maxPixel: ArtworkStore.coverPixels,
                         focus: wide ? Self.heroFocus : Self.promoFocus) {
                BppColor.bgHover
            }
        }
        .background(BppColor.bgHover)
        .accessibilityHidden(true)
    }
}

/// The one floating button: a `control-md` circle over the cover (bare over the content pane),
/// its hit area `touch-target`, opening a pull-down menu.
private struct MoreButton: View {
    let bare: Bool
    @Binding var showingAbout: Bool

    var body: some View {
        Menu {
            Button { showingAbout = true } label: { Label("关于", systemImage: "info.circle") }
        } label: {
            Image(systemName: "ellipsis")
                .font(.system(size: BppSize.iconMd, weight: .semibold))
                .foregroundColor(BppColor.text1)
                .frame(width: BppSize.controlMd, height: BppSize.controlMd)
                .background(Circle().fill(bare ? Color.clear : BppColor.bgPanel))
                .overlay(Circle().strokeBorder(bare ? Color.clear : BppColor.lineStrong))
                .frame(width: BppSize.touchTarget, height: BppSize.touchTarget)
                .contentShape(Rectangle())
        }
        .accessibilityLabel("更多")
        .accessibilityIdentifier("more")
    }
}

// MARK: - Panel

/// The panel's parts in one column, or in two equal panes: the title line and the stats as rows
/// in the start pane, the rest in the end pane with the primary pinned to its bottom.
private struct Panel: View {
    @Environment(\.dynamicTypeSize) private var typeSize
    let panes: Bool

    var body: some View {
        if panes {
            HStack(alignment: .top, spacing: BppSpace.space24) {
                VStack(alignment: .leading, spacing: BppSpace.space16) {
                    TitleLine(stacked: false)
                    StatRows(dividers: false)
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
                VStack(alignment: .leading, spacing: BppSpace.space16) {
                    Messages()
                    SourceRow()
                    Spacer(minLength: 0)
                    PrimaryArea()
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            }
            .fixedSize(horizontal: false, vertical: true)
        } else {
            VStack(alignment: .leading, spacing: BppSpace.space16) {
                TitleLine(stacked: false)
                if typeSize.isAccessibilitySize { StatRows(dividers: true) } else { StatColumns() }
                Messages()
                SourceRow()
                PrimaryArea()
            }
        }
    }
}

/// The content pane from cover-pane-breakpoint.
private struct WidePanel: View {
    var body: some View {
        VStack(alignment: .leading, spacing: BppSpace.space16) {
            TitleLine(stacked: true)
            StatRows(dividers: true)
            Messages()
            PrimaryArea()
            SourceRow()
        }
    }
}

/// "The Bazaar" in `display` with the status badge at the line's end (or under it, stacked).
private struct TitleLine: View {
    @EnvironmentObject var model: LauncherModel
    let stacked: Bool

    var body: some View {
        let title = Text(model.snapshot.game?.title ?? "The Bazaar").bppFont(BppType.display)
            .foregroundColor(BppColor.text1).accessibilityAddTraits(.isHeader)
        if stacked {
            VStack(alignment: .leading, spacing: BppSpace.space8) {
                title
                badge
            }
        } else {
            HStack(spacing: BppSpace.space8) {
                title.frame(maxWidth: .infinity, alignment: .leading)
                badge
            }
        }
    }

    @ViewBuilder private var badge: some View {
        if let status = model.status {
            let word = model.downloadPercent.map { "\(status.word) \($0)%" } ?? status.word
            BppBadge(word: word, tone: status.tone).accessibilityIdentifier("launcher-status")
        }
    }
}

// MARK: - Stats

private struct Stat: Identifiable {
    let key: String
    let value: String
    var id: String { key }
}

private extension LauncherModel {
    var stats: [Stat] {
        let game = snapshot.game
        return [
            Stat(key: "游戏版本", value: game?.downloaded == true ? game?.shortVersion ?? "" : ""),
            Stat(key: "适配版本", value: game?.shortVersion ?? ""),
            Stat(key: "BazaarPlusPlus", value: appVersion),
        ]
    }
}

/// Three equal columns: the key in `caption` / `text-3` over the value in `body-strong`.
private struct StatColumns: View {
    @EnvironmentObject var model: LauncherModel

    var body: some View {
        HStack(alignment: .top, spacing: BppSpace.space16) {
            ForEach(model.stats) { stat in
                VStack(alignment: .leading, spacing: 0) {
                    Text(stat.key).bppFont(BppType.caption).foregroundColor(BppColor.text3)
                    Text(stat.value.isEmpty ? "—" : stat.value).bppFont(BppType.bodyStrong).monospacedDigit()
                        .foregroundColor(BppColor.text1)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .accessibilityElement(children: .combine)
            }
        }
    }
}

/// The stats as key-value rows: in panes, and at accessibility text sizes.
private struct StatRows: View {
    @EnvironmentObject var model: LauncherModel
    let dividers: Bool

    var body: some View {
        VStack(spacing: 0) {
            let stats = model.stats
            ForEach(stats) { stat in
                BppKeyValueRow(key: stat.key, value: stat.value, last: !dividers || stat.id == stats.last?.id, inset: 0)
            }
        }
    }
}

// MARK: - Messages

/// The App update and a failure as banners, then why the primary is missing as a note.
private struct Messages: View {
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
        if let game = model.snapshot.game, model.failure == nil {
            switch model.action(for: game) {
            case .blocked(let reason), .unavailable(let reason):
                Text(reason).bppFont(BppType.caption).foregroundColor(BppColor.text2)
                    .fixedSize(horizontal: false, vertical: true)
            default:
                EmptyView()
            }
        }
    }
}

// MARK: - Source row

/// The Steam account: opens the sign-in sheet, which shows the account and 退出登录 once signed in.
private struct SourceRow: View {
    @EnvironmentObject var model: LauncherModel

    var body: some View {
        Button { model.showingAccount = true } label: {
            HStack(spacing: BppSpace.space12) {
                Image(systemName: "person.fill")
                    .font(.system(size: BppSize.iconMd))
                    .foregroundColor(BppColor.text2)
                    .frame(width: 32, height: 32)
                    .background(Circle().fill(BppColor.bgSelected))
                Text(label).bppFont(BppType.bodyMedium).foregroundColor(BppColor.text1).lineLimit(1)
                    .frame(maxWidth: .infinity, alignment: .leading)
                Text("Steam").bppFont(BppType.caption).foregroundColor(BppColor.text3)
                Image(systemName: "chevron.right")
                    .font(.system(size: BppSize.iconSm, weight: .semibold))
                    .foregroundColor(BppColor.text3)
            }
            .padding(.horizontal, BppSpace.space12)
            .padding(.vertical, BppSpace.space8)
            .frame(maxWidth: .infinity, minHeight: BppSize.sourceRowHeight)
            .background(RoundedRectangle(cornerRadius: BppRadius.rTouch, style: .continuous).fill(BppColor.bgPanel))
            .contentShape(RoundedRectangle(cornerRadius: BppRadius.rTouch, style: .continuous))
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

// MARK: - Primary

/// The download's progress and the one primary; 取消 while signing in or downloading.
private struct PrimaryArea: View {
    @EnvironmentObject var model: LauncherModel

    var body: some View {
        if let game = model.snapshot.game, model.failure == nil {
            let action = model.action(for: game)
            VStack(alignment: .leading, spacing: BppSpace.space12) {
                switch action {
                case .working:
                    progress
                    Button("取消") { model.cancel() }
                        .buttonStyle(BppButtonStyle(kind: .secondary))
                        .accessibilityIdentifier("cancel-operation")
                case .blocked, .unavailable:
                    EmptyView()
                default:
                    Button(primaryTitle(action)) { model.perform(game) }
                        .buttonStyle(BppButtonStyle(kind: .primary))
                        .accessibilityIdentifier(identifier(action))
                }
            }
        }
    }

    /// The download's track and its numbers; other work shows only the busy badge.
    @ViewBuilder private var progress: some View {
        if model.snapshot.state == "downloading" {
            VStack(alignment: .leading, spacing: BppSpace.space6) {
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
                }
            }
        }
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

/// Bundled art that never pushes the layout: the container decides the size.
struct ArtworkImage<Placeholder: View>: View {
    @EnvironmentObject var model: LauncherModel
    let artwork: Artwork
    let maxPixel: CGFloat
    /// The point of the art kept nearest the centre when the container crops it.
    var focus: UnitPoint = .center
    @ViewBuilder let placeholder: () -> Placeholder
    @State private var image: UIImage?

    var body: some View {
        GeometryReader { proxy in
            if let image {
                let size = proxy.size
                let scale = max(size.width / image.size.width, size.height / image.size.height)
                let width = image.size.width * scale, height = image.size.height * scale
                Image(uiImage: image).resizable()
                    .frame(width: width, height: height)
                    .offset(x: min(0, max(size.width - width, size.width / 2 - focus.x * width)),
                            y: min(0, max(size.height - height, size.height / 2 - focus.y * height)))
                    .transition(.opacity)
            } else {
                placeholder()
            }
        }
        .clipped()
            .task(id: artwork) {
                let loaded = await model.artwork.image(artwork, maxPixel: maxPixel)
                withAnimation(.easeOut(duration: 0.16)) { image = loaded }
            }
    }
}

extension View {
    /// The default iPad form sheet is too short for the QR step; page size keeps its actions in view.
    @ViewBuilder func pageSizedSheet() -> some View {
        if #available(iOS 18.0, *) { presentationSizing(.page) } else { self }
    }
}
