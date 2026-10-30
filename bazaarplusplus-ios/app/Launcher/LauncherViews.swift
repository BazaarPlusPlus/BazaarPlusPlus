import SwiftUI
import UIKit

enum Theme {
    static let background = Color(red: 0.055, green: 0.063, blue: 0.078)
    static let surface = Color(red: 0.098, green: 0.110, blue: 0.133)
    static let raised = Color(red: 0.137, green: 0.153, blue: 0.180)
    static let stroke = Color.white.opacity(0.08)
    static let secondary = Color(red: 0.62, green: 0.65, blue: 0.70)
    static let brand = Color(red: 0.91, green: 0.58, blue: 0.25)
    static let ok = Color(red: 0.36, green: 0.80, blue: 0.52)
    static let warn = Color(red: 0.96, green: 0.72, blue: 0.33)

    static func accent(_ hex: String) -> Color {
        guard hex.count == 6, let v = UInt32(hex, radix: 16) else { return brand }
        return Color(red: Double(v >> 16 & 0xFF) / 255, green: Double(v >> 8 & 0xFF) / 255, blue: Double(v & 0xFF) / 255)
    }

    static func configureAppearance() {
        let bar = UINavigationBarAppearance()
        bar.configureWithTransparentBackground()
        bar.titleTextAttributes = [.foregroundColor: UIColor.white]
        bar.largeTitleTextAttributes = [.foregroundColor: UIColor.white]
        UINavigationBar.appearance().standardAppearance = bar
        UINavigationBar.appearance().scrollEdgeAppearance = bar
        UINavigationBar.appearance().compactAppearance = bar
        UINavigationBar.appearance().tintColor = .white
    }
}

// MARK: - Root

struct LauncherRoot: View {
    @EnvironmentObject var model: LauncherModel

    var body: some View {
        NavigationView { LibraryView() }
            .navigationViewStyle(.stack)
            .preferredColorScheme(.dark)
            .tint(Theme.brand)
            .sheet(isPresented: $model.showingAccount) { AccountSheet().environmentObject(model).pageSizedSheet() }
            .fullScreenCover(isPresented: $model.showingOnboarding) { OnboardingView().environmentObject(model) }
            .task { await model.loadArtwork() }
            .task { await model.checkForUpdate() }
    }
}

// MARK: - Artwork

/// Remote store art with a designed fallback so the launcher never shows an empty box.
struct ArtworkImage<Placeholder: View>: View {
    @EnvironmentObject var model: LauncherModel
    let url: URL?
    let appId: UInt32?
    let maxPixel: CGFloat
    @ViewBuilder let placeholder: () -> Placeholder
    @State private var image: UIImage?

    var body: some View {
        // The container decides the size; a filled image must never push the layout wider.
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
            withAnimation(.easeOut(duration: 0.25)) { image = loaded }
        }
    }
}

struct MonogramArt: View {
    let game: ServiceSnapshot.Game
    var body: some View {
        let accent = Theme.accent(game.accent)
        ZStack {
            LinearGradient(colors: [accent.opacity(0.55), Theme.surface], startPoint: .topLeading, endPoint: .bottomTrailing)
            Text(game.monogram).font(.system(size: 96, weight: .black, design: .rounded)).foregroundColor(.white.opacity(0.18))
        }
    }
}

struct LogoOrTitle: View {
    @EnvironmentObject var model: LauncherModel
    let game: ServiceSnapshot.Game
    var height: CGFloat
    @State private var logo: UIImage?

    var body: some View {
        Group {
            if let logo {
                Image(uiImage: logo).resizable().aspectRatio(contentMode: .fit)
                    .frame(maxHeight: height, alignment: .bottomLeading)
                    .shadow(color: .black.opacity(0.6), radius: 12, y: 4)
            } else {
                Text(game.title).font(.system(size: height * 0.42, weight: .heavy)).foregroundColor(.white)
                    .shadow(color: .black.opacity(0.6), radius: 8, y: 2)
            }
        }
        .accessibilityLabel(game.title)
        .task(id: model.store(game)?.logo) {
            guard let url = model.store(game)?.logo, let id = model.appId(game) else { return }
            logo = await model.artwork.image(url, appId: id, maxPixel: 900)
        }
    }
}

// MARK: - Library

struct LibraryView: View {
    @EnvironmentObject var model: LauncherModel

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 28) {
                header
                if let update = model.update {
                    UpdateNotice(update: update)
                }
                if let failure = model.failure {
                    Notice(icon: "exclamationmark.triangle.fill", text: failure, color: Theme.warn)
                } else if model.snapshot.game == nil {
                    HStack(spacing: 12) { ProgressView(); Text(model.snapshot.message).foregroundColor(Theme.secondary) }
                }
                if let featured = model.snapshot.game {
                    NavigationLink(destination: GameDetailView()) { FeaturedCard(game: featured) }
                        .buttonStyle(PressableStyle())
                        .accessibilityIdentifier(featured.id)
                }
                Text("封面与介绍实时取自 Steam 商店。")
                    .font(.footnote).foregroundColor(Theme.secondary.opacity(0.8))
            }
            .padding(.horizontal, 20)
            .padding(.top, 8)
            .padding(.bottom, 120)
            .frame(maxWidth: 900)
            .frame(maxWidth: .infinity)
        }
        .background(Theme.background.ignoresSafeArea())
        .navigationBarHidden(true)
        .overlay(alignment: .bottom) { ActivityBar() }
    }

    private var header: some View {
        HStack(alignment: .center) {
            Text(model.appName).font(.system(size: 34, weight: .bold))
            Spacer()
            AccountButton()
        }
    }
}

struct AccountButton: View {
    @EnvironmentObject var model: LauncherModel
    var body: some View {
        Button { model.showingAccount = true } label: {
            HStack(spacing: 8) {
                ZStack(alignment: .bottomTrailing) {
                    Image(systemName: "person.crop.circle.fill").font(.system(size: 26)).foregroundColor(.white.opacity(0.9))
                    Circle().fill(model.snapshot.loggedIn ? Theme.ok : Theme.secondary.opacity(0.6))
                        .frame(width: 9, height: 9).overlay(Circle().stroke(Theme.raised, lineWidth: 2))
                }
                Group {
                    if model.snapshot.loggedIn { Text(model.snapshot.account.isEmpty ? "Steam 已连接" : model.snapshot.account) }
                    else if model.isSigningIn { Text("正在登录…") }
                    else { Text("登录 Steam") }
                }
                .font(.subheadline.weight(.semibold)).foregroundColor(.white).lineLimit(1)
            }
            .padding(.leading, 6).padding(.trailing, 14).padding(.vertical, 6)
            .background(Capsule().fill(Theme.raised))
            .overlay(Capsule().stroke(Theme.stroke))
        }
        .accessibilityIdentifier("account")
    }
}

struct FeaturedCard: View {
    @EnvironmentObject var model: LauncherModel
    let game: ServiceSnapshot.Game

    var body: some View {
        let store = model.store(game)
        ZStack(alignment: .bottomLeading) {
            ArtworkImage(url: store?.hero ?? store?.header, appId: model.appId(game), maxPixel: 1800) { MonogramArt(game: game) }
                .frame(height: 260).frame(maxWidth: .infinity).clipped()
            LinearGradient(colors: [.clear, .black.opacity(0.35), .black.opacity(0.88)], startPoint: .top, endPoint: .bottom)
            VStack(alignment: .leading, spacing: 12) {
                LogoOrTitle(game: game, height: 78)
                HStack(spacing: 8) {
                    StatusPill(text: game.compatibility.label, color: game.compatibility.launch ? Theme.ok : Theme.warn)
                    StatusPill(text: model.installLabel(game), color: .white)
                    Spacer()
                    HStack(spacing: 6) {
                        Text(primaryTitle(model.action(for: game))).font(.subheadline.weight(.bold))
                        Image(systemName: "chevron.right").font(.caption.weight(.bold))
                    }
                    .foregroundColor(.black)
                    .padding(.horizontal, 14).padding(.vertical, 9)
                    .background(Capsule().fill(Theme.accent(game.accent)))
                }
            }
            .padding(18)
        }
        .frame(height: 260)
        .clipShape(RoundedRectangle(cornerRadius: 22, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 22, style: .continuous).stroke(Theme.stroke))
        .shadow(color: .black.opacity(0.4), radius: 18, y: 10)
    }
}

struct StatusPill: View {
    let text: String
    let color: Color
    var body: some View {
        HStack(spacing: 5) {
            Circle().fill(color).frame(width: 6, height: 6)
            Text(text).font(.caption.weight(.semibold)).foregroundColor(.white)
        }
        .padding(.horizontal, 10).padding(.vertical, 6)
        .background(Capsule().fill(.ultraThinMaterial))
    }
}

/// The non-blocking reminder that a newer App is out; it links to the download when the manifest has one.
struct UpdateNotice: View {
    let update: LauncherModel.AppUpdate
    var body: some View {
        if let url = update.url {
            Link(destination: url) { Notice(icon: "arrow.down.circle.fill", text: "BazaarPlusPlus \(update.version) 已发布，点这里下载新版 App。", color: Theme.ok) }
        } else {
            Notice(icon: "arrow.down.circle.fill", text: "BazaarPlusPlus \(update.version) 已发布，请安装新版 App。", color: Theme.ok)
        }
    }
}

struct Notice: View {
    let icon: String
    let text: String
    let color: Color
    var body: some View {
        HStack(alignment: .top, spacing: 10) {
            Image(systemName: icon).foregroundColor(color)
            Text(text).font(.subheadline).foregroundColor(.white.opacity(0.9)).fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: 0)
        }
        .padding(14)
        .background(RoundedRectangle(cornerRadius: 14, style: .continuous).fill(color.opacity(0.12)))
        .overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).stroke(color.opacity(0.3)))
    }
}

/// Floating progress for long work so the library stays usable while it runs.
struct ActivityBar: View {
    @EnvironmentObject var model: LauncherModel
    var body: some View {
        let s = model.snapshot
        if s.busy && !model.isSigningIn {
            VStack(alignment: .leading, spacing: 8) {
                HStack {
                    Text(s.message).font(.footnote.weight(.semibold)).lineLimit(1)
                    Spacer()
                    if s.state == "downloading" { Text("\(Int(s.progress * 100))%").font(.footnote.monospacedDigit()).foregroundColor(Theme.secondary) }
                }
                if s.state == "downloading" { ProgressView(value: s.progress).tint(Theme.brand) } else { ProgressView().progressViewStyle(.linear).tint(Theme.brand) }
                if let detail = model.downloadDetail { Text(detail).font(.caption.monospacedDigit()).foregroundColor(Theme.secondary).lineLimit(1) }
            }
            .padding(14)
            .background(RoundedRectangle(cornerRadius: 16, style: .continuous).fill(.regularMaterial))
            .padding(.horizontal, 16).padding(.bottom, 12)
            .transition(.move(edge: .bottom).combined(with: .opacity))
            .accessibilityIdentifier("launcher-status")
        }
    }
}

struct PressableStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .scaleEffect(configuration.isPressed ? 0.97 : 1)
            .animation(.spring(response: 0.25, dampingFraction: 0.7), value: configuration.isPressed)
    }
}

func primaryTitle(_ action: GameAction) -> String {
    switch action {
    case .play: return "开始游戏"
    case .connect: return "连接 Steam"
    case .download: return "下载"
    case .prepare: return "校验并准备"
    case .working: return "进行中"
    case .blocked: return "查看"
    case .unavailable: return "查看"
    }
}

// MARK: - Game detail

struct GameDetailView: View {
    @EnvironmentObject var model: LauncherModel
    @State private var viewer: ScreenshotSelection?
    @Environment(\.verticalSizeClass) private var verticalSizeClass

    var body: some View {
        if let game = model.snapshot.game {
            content(game)
        } else {
            Theme.background.ignoresSafeArea()
        }
    }

    private func content(_ game: ServiceSnapshot.Game) -> some View {
        let store = model.store(game)
        let accent = Theme.accent(game.accent)
        return ScrollView {
            VStack(alignment: .leading, spacing: 0) {
                hero(game, store: store)
                VStack(alignment: .leading, spacing: 26) {
                    ActionPanel(game: game)
                    if !game.compatibility.reason.isEmpty && !game.compatibility.launch {
                        Notice(icon: "iphone.gen3.badge.exclamationmark", text: game.compatibility.reason, color: Theme.warn)
                    }
                    DetailSection(title: "关于游戏") {
                        VStack(alignment: .leading, spacing: 10) {
                            if !game.subtitle.isEmpty { Text(game.subtitle).font(.headline) }
                            Text(store?.summary ?? game.description).font(.body).foregroundColor(.white.opacity(0.82))
                                .lineSpacing(4).fixedSize(horizontal: false, vertical: true)
                        }
                    }
                    if let shots = store?.screenshots, !shots.isEmpty {
                        DetailSection(title: "截图") {
                            ScrollView(.horizontal, showsIndicators: false) {
                                LazyHStack(spacing: 12) {
                                    ForEach(Array(shots.enumerated()), id: \.offset) { index, url in
                                        Button { viewer = ScreenshotSelection(index: index) } label: {
                                            ArtworkImage(url: url, appId: model.appId(game), maxPixel: 900) { Theme.raised }
                                                .frame(width: 260, height: 146)
                                                .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
                                                .overlay(RoundedRectangle(cornerRadius: 12, style: .continuous).stroke(Theme.stroke))
                                        }
                                        .buttonStyle(PressableStyle())
                                    }
                                }
                                .padding(.horizontal, 20)
                            }
                            .padding(.horizontal, -20)
                        }
                    }
                    DetailSection(title: "信息") {
                        VStack(spacing: 0) {
                            InfoRow(label: "类型", value: game.genre)
                            if let dev = store?.developers, !dev.isEmpty { InfoRow(label: "开发商", value: dev.joined(separator: "、")) }
                            if let pub = store?.publishers, !pub.isEmpty { InfoRow(label: "发行商", value: pub.joined(separator: "、")) }
                            InfoRow(label: "适配版本", value: game.version)
                            if let id = model.appId(game) { InfoRow(label: "Steam App ID", value: String(id)) }
                            InfoRow(label: model.snapshot.platform == "device" ? "本机（设备）" : "本机（模拟器）",
                                    value: game.compatibility.label, last: true)
                        }
                        .background(RoundedRectangle(cornerRadius: 14, style: .continuous).fill(Theme.surface))
                        .overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).stroke(Theme.stroke))
                    }
                }
                .padding(.horizontal, 20)
                .padding(.top, 20)
                .padding(.bottom, 48)
                .frame(maxWidth: 900)
                .frame(maxWidth: .infinity)
            }
        }
        .background(Theme.background.ignoresSafeArea())
        .ignoresSafeArea(edges: .top)
        .navigationBarTitleDisplayMode(.inline)
        .tint(accent)
        .sheet(item: $viewer) { selection in
            ScreenshotViewer(urls: store?.screenshots ?? [], appId: model.appId(game), index: selection.index)
                .environmentObject(model)
        }
    }

    private func hero(_ game: ServiceSnapshot.Game, store: StoreArtwork?) -> some View {
        GeometryReader { proxy in
            let pull = max(0, proxy.frame(in: .global).minY)
            ZStack(alignment: .bottomLeading) {
                ArtworkImage(url: store?.hero ?? store?.header, appId: model.appId(game), maxPixel: 1800) { MonogramArt(game: game) }
                    .frame(width: proxy.size.width, height: proxy.size.height + pull)
                    .clipped()
                LinearGradient(stops: [
                    .init(color: Theme.background.opacity(0.0), location: 0.35),
                    .init(color: Theme.background.opacity(0.75), location: 0.75),
                    .init(color: Theme.background, location: 1),
                ], startPoint: .top, endPoint: .bottom)
                VStack(alignment: .leading, spacing: 10) {
                    LogoOrTitle(game: game, height: 96)
                    HStack(spacing: 8) {
                        StatusPill(text: game.compatibility.label, color: game.compatibility.launch ? Theme.ok : Theme.warn)
                        if !game.genre.isEmpty { StatusPill(text: game.genre, color: Theme.accent(game.accent)) }
                    }
                }
                .padding(.horizontal, 20)
                .padding(.bottom, 6)
                .frame(maxWidth: 900, alignment: .leading)
                .frame(maxWidth: .infinity, alignment: .leading)
            }
            .offset(y: -pull)
        }
        .frame(height: verticalSizeClass == .compact ? 240 : 340)
    }
}

struct ScreenshotSelection: Identifiable { let index: Int; var id: Int { index } }

struct DetailSection<Content: View>: View {
    let title: String
    @ViewBuilder let content: () -> Content
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(title).font(.title3.bold())
            content()
        }
    }
}

struct InfoRow: View {
    let label: String
    let value: String
    var last = false
    var body: some View {
        VStack(spacing: 0) {
            HStack(alignment: .firstTextBaseline) {
                Text(label).foregroundColor(Theme.secondary)
                Spacer(minLength: 16)
                Text(value.isEmpty ? "—" : value).multilineTextAlignment(.trailing).foregroundColor(.white)
            }
            .font(.subheadline)
            .padding(.horizontal, 16).padding(.vertical, 13)
            if !last { Divider().background(Theme.stroke).padding(.leading, 16) }
        }
    }
}

struct ActionPanel: View {
    @EnvironmentObject var model: LauncherModel
    let game: ServiceSnapshot.Game

    var body: some View {
        let action = model.action(for: game)
        let accent = Theme.accent(game.accent)
        VStack(alignment: .leading, spacing: 12) {
            switch action {
            case .working(let message, let progress):
                VStack(alignment: .leading, spacing: 10) {
                    HStack {
                        if progress == nil { ProgressView().tint(.white) }
                        Text(message).font(.subheadline.weight(.semibold)).lineLimit(2)
                        Spacer()
                        if let progress { Text("\(Int(progress * 100))%").font(.subheadline.monospacedDigit().weight(.bold)) }
                    }
                    if let progress { ProgressView(value: progress).tint(accent) }
                    if let detail = model.downloadDetail { Text(detail).font(.footnote.monospacedDigit()).foregroundColor(Theme.secondary).lineLimit(1) }
                    Button(role: .destructive) { model.cancel() } label: { Text(model.isSigningIn ? "取消登录" : "停止并断开 Steam").font(.footnote.weight(.semibold)) }
                        .accessibilityIdentifier("cancel-operation")
                }
                .padding(16)
                .background(RoundedRectangle(cornerRadius: 16, style: .continuous).fill(Theme.surface))
                .overlay(RoundedRectangle(cornerRadius: 16, style: .continuous).stroke(Theme.stroke))
            case .blocked(let reason):
                PrimaryButton(title: "暂不可启动", icon: "lock.fill", color: Theme.raised, foreground: Theme.secondary) {}
                    .disabled(true)
                    .accessibilityHint(reason)
            case .unavailable(let reason):
                PrimaryButton(title: reason, icon: "hourglass", color: Theme.raised, foreground: Theme.secondary) {}.disabled(true)
            default:
                PrimaryButton(title: title(action), icon: icon(action), color: accent, foreground: .black) { model.perform(game) }
                    .accessibilityIdentifier(identifier(action))
            }
            if !model.snapshot.busy, let note = note(action) {
                Text(note).font(.footnote).foregroundColor(model.snapshot.state == "error" ? Theme.warn : Theme.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            if action == .connect && !model.snapshot.savedAccount.isEmpty {
                Button { model.restore() } label: {
                    Label("继续以 \(model.snapshot.savedAccount) 登录", systemImage: "arrow.clockwise").font(.subheadline.weight(.semibold))
                }
                .foregroundColor(.white.opacity(0.85))
            }
        }
    }

    private func title(_ action: GameAction) -> String {
        switch action {
        case .play: return "开始游戏"
        case .connect: return "连接 Steam 以下载"
        case .download: return "下载游戏"
        case .prepare: return "校验并准备游戏"
        default: return ""
        }
    }
    private func icon(_ action: GameAction) -> String {
        switch action {
        case .play: return "play.fill"
        case .connect: return "person.badge.key.fill"
        case .download: return "arrow.down.circle.fill"
        default: return "checkmark.shield.fill"
        }
    }
    private func identifier(_ action: GameAction) -> String {
        switch action {
        case .play: return "play"
        case .connect: return "connect"
        default: return "download"
        }
    }
    private func note(_ action: GameAction) -> String? {
        let s = model.snapshot
        if s.state == "error" { return s.message }
        switch action {
        case .play: return "Steam 已连接 · 文件已校验"
        case .download: return "首次启动前会从 Steam 下载并逐块校验游戏文件。"
        case .prepare: return "会复核本地文件，缺失或损坏的部分从 Steam 补齐。"
        default: return nil
        }
    }
}

struct PrimaryButton: View {
    let title: String
    let icon: String
    let color: Color
    let foreground: Color
    let action: () -> Void
    var body: some View {
        Button(action: action) {
            HStack(spacing: 10) {
                Image(systemName: icon)
                Text(title)
            }
            .font(.headline)
            .foregroundColor(foreground)
            .frame(maxWidth: .infinity, minHeight: 54)
            .background(RoundedRectangle(cornerRadius: 16, style: .continuous).fill(color))
        }
        .buttonStyle(PressableStyle())
    }
}

struct ScreenshotViewer: View {
    @EnvironmentObject var model: LauncherModel
    @Environment(\.dismiss) private var dismiss
    let urls: [URL]
    let appId: UInt32?
    @State var index: Int

    var body: some View {
        ZStack(alignment: .topTrailing) {
            Color.black.ignoresSafeArea()
            TabView(selection: $index) {
                ForEach(Array(urls.enumerated()), id: \.offset) { i, url in
                    ArtworkImage(url: url, appId: appId, maxPixel: 2000) { ProgressView() }
                        .aspectRatio(16 / 9, contentMode: .fit)
                        .tag(i)
                }
            }
            .tabViewStyle(.page(indexDisplayMode: .always))
            Button { dismiss() } label: {
                Image(systemName: "xmark.circle.fill").font(.system(size: 30)).foregroundStyle(.white, .white.opacity(0.2))
            }
            .padding(20)
        }
        .preferredColorScheme(.dark)
    }
}

extension View {
    /// The default iPad form sheet is too short for the QR step; page size keeps its actions in view.
    @ViewBuilder func pageSizedSheet() -> some View {
        if #available(iOS 18.0, *) { presentationSizing(.page) } else { self }
    }
}
