import Combine
import Foundation
import SwiftUI
import UIKit

/// Mirror of `service/Managed/SteamService.cs::Snapshot`. The service stays the single owner
/// of state; this layer only decodes it and sends commands back.
struct ServiceSnapshot: Decodable, Equatable {
    struct Compatibility: Decodable, Equatable {
        var launch = false
        var label = "待适配"
        var reason = ""
        init() {}
        init(from decoder: Decoder) throws {
            let c = try decoder.container(keyedBy: CodingKeys.self)
            launch = try c.decodeIfPresent(Bool.self, forKey: .launch) ?? false
            label = try c.decodeIfPresent(String.self, forKey: .label) ?? "待适配"
            reason = try c.decodeIfPresent(String.self, forKey: .reason) ?? ""
        }
        enum CodingKeys: String, CodingKey { case launch, label, reason }
    }
    struct Game: Decodable, Equatable, Identifiable {
        var id: String
        var title: String
        var subtitle: String
        var genre: String
        var description: String
        var accent: String
        var monogram: String
        var version: String
        var compatibility: Compatibility
        var downloaded: Bool
        init(from decoder: Decoder) throws {
            let c = try decoder.container(keyedBy: CodingKeys.self)
            id = try c.decode(String.self, forKey: .id)
            title = try c.decodeIfPresent(String.self, forKey: .title) ?? id
            subtitle = try c.decodeIfPresent(String.self, forKey: .subtitle) ?? ""
            genre = try c.decodeIfPresent(String.self, forKey: .genre) ?? ""
            description = try c.decodeIfPresent(String.self, forKey: .description) ?? ""
            accent = try c.decodeIfPresent(String.self, forKey: .accent) ?? "D99147"
            monogram = try c.decodeIfPresent(String.self, forKey: .monogram) ?? "G"
            version = try c.decodeIfPresent(String.self, forKey: .version) ?? ""
            compatibility = (try? c.decodeIfPresent(Compatibility.self, forKey: .compatibility)) ?? Compatibility()
            downloaded = try c.decodeIfPresent(Bool.self, forKey: .downloaded) ?? false
        }
        enum CodingKeys: String, CodingKey { case id, title, subtitle, genre, description, accent, monogram, version, compatibility, downloaded }
    }
    var state = "starting"
    var message = "正在初始化…"
    var progress = 0.0
    var busy = false
    var launched = false
    var loggedIn = false
    var canLaunch = false
    var platform = ""
    var account = ""
    var savedAccount = ""
    var errorCode = ""
    var verification = ""
    var qrUrl = ""
    var game: Game?
    init() {}
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        state = try c.decodeIfPresent(String.self, forKey: .state) ?? "error"
        message = try c.decodeIfPresent(String.self, forKey: .message) ?? ""
        progress = try c.decodeIfPresent(Double.self, forKey: .progress) ?? 0
        busy = try c.decodeIfPresent(Bool.self, forKey: .busy) ?? false
        launched = try c.decodeIfPresent(Bool.self, forKey: .launched) ?? false
        loggedIn = try c.decodeIfPresent(Bool.self, forKey: .loggedIn) ?? false
        canLaunch = try c.decodeIfPresent(Bool.self, forKey: .canLaunch) ?? false
        platform = try c.decodeIfPresent(String.self, forKey: .platform) ?? ""
        account = try c.decodeIfPresent(String.self, forKey: .account) ?? ""
        savedAccount = try c.decodeIfPresent(String.self, forKey: .savedAccount) ?? ""
        errorCode = try c.decodeIfPresent(String.self, forKey: .errorCode) ?? ""
        verification = try c.decodeIfPresent(String.self, forKey: .verification) ?? ""
        qrUrl = try c.decodeIfPresent(String.self, forKey: .qrUrl) ?? ""
        game = try c.decodeIfPresent(Game.self, forKey: .game)
    }
    enum CodingKeys: String, CodingKey {
        case state, message, progress, busy, launched, loggedIn, canLaunch, platform, account, savedAccount, errorCode, verification, qrUrl, game
    }
}

/// Where the Steam sign-in flow stands, derived from the service state.
enum LoginStep: Equatable {
    /// `viaApp`: Steam Guard mobile authenticator, so approving on the phone also works.
    case credentials, connecting(String), verify(String, viaApp: Bool), qr(String), signedIn
}

/// What the primary button on a game page does right now.
enum GameAction: Equatable {
    case play, connect, download, prepare, working(String, Double?), blocked(String), unavailable(String)
}

@MainActor
final class LauncherModel: ObservableObject {
    @Published private(set) var snapshot = ServiceSnapshot()
    @Published private(set) var failure: String?
    @Published var showingAccount = false
    @Published var showingOnboarding = false
    /// Display name from Info.plist; the one place the product name lives.
    let appName = Bundle.main.object(forInfoDictionaryKey: "CFBundleDisplayName") as? String ?? "BazaarPlusPlus"
    private var started = false
    private static let onboardingKey = "launcher.onboarding"
    let artwork = ArtworkStore()
    private let send: (NSDictionary) -> Void
    private let launchGame: () -> Void
    /// The Steam App ID comes from the packaged game definition; the snapshot carries only presentation fields.
    private let gameAppId: UInt32?
    private var forward: AnyCancellable?

    init(send: @escaping (NSDictionary) -> Void, launch: @escaping () -> Void) {
        self.send = send
        self.launchGame = launch
        if let data = try? Data(contentsOf: Bundle.main.bundleURL.appendingPathComponent("game.json")),
           let game = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
           let appId = (game["steam"] as? [String: Any])?["appId"] as? NSNumber {
            gameAppId = appId.uint32Value
        } else {
            gameAppId = nil
        }
        // Artwork arrives after first render; views read it through this model.
        forward = artwork.objectWillChange.sink { [weak self] _ in self?.objectWillChange.send() }
    }

    func apply(_ raw: NSDictionary) {
        guard let data = try? JSONSerialization.data(withJSONObject: raw),
              let next = try? JSONDecoder().decode(ServiceSnapshot.self, from: data) else { return }
        if next != snapshot { snapshot = next }
        if !started {
            started = true
            // A remembered account signs itself back in; only brand-new players see the intro.
            if !next.savedAccount.isEmpty && !next.loggedIn && !next.busy && next.state == "signedOut" { restore() }
            let firstRun = !next.loggedIn && next.savedAccount.isEmpty && !UserDefaults.standard.bool(forKey: Self.onboardingKey)
            // `-launcher.onboarding.force YES` previews the intro on an already signed-in device.
            if firstRun || UserDefaults.standard.bool(forKey: "launcher.onboarding.force") { showingOnboarding = true }
        }
    }

    func finishOnboarding(connect: Bool) {
        UserDefaults.standard.set(true, forKey: Self.onboardingKey)
        showingOnboarding = false
        if connect {
            // Present the sign-in sheet after the cover has gone.
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.45) { self.showingAccount = true }
        }
    }

    var loginStep: LoginStep {
        let s = snapshot
        if s.loggedIn { return .signedIn }
        switch s.state {
        case "code": return .verify(s.message, viaApp: s.verification != "email")
        case "qr" where !s.qrUrl.isEmpty: return .qr(s.qrUrl)
        case "connecting" where s.busy: return .connecting(s.message)
        default: return .credentials
        }
    }

    func fail(_ message: String) { failure = message }

    func appId(_ game: ServiceSnapshot.Game) -> UInt32? { gameAppId }
    func store(_ game: ServiceSnapshot.Game) -> StoreArtwork? { artwork.artwork(appId(game)) }
    /// A newer BazaarPlusPlus for iOS, from its Platform Release Manifest. Only a reminder: it never
    /// blocks the game, and a missing or unreadable manifest shows nothing.
    @Published private(set) var update: AppUpdate?

    struct AppUpdate: Equatable {
        let version: String
        let url: URL?
    }

    static let releaseManifest = URL(string: "https://cdn.bazaarplusplus.com/installer/latest/mod/ios-aarch64.json")!

    func checkForUpdate() async {
        let current = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? ""
        var request = URLRequest(url: Self.releaseManifest, timeoutInterval: 10)
        request.cachePolicy = .reloadIgnoringLocalCacheData
        guard let (data, response) = try? await URLSession.shared.data(for: request),
              (response as? HTTPURLResponse)?.statusCode == 200,
              let manifest = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let version = manifest["version"] as? String,
              Self.isNewer(version, than: current) else { return }
        let download = ((manifest["downloads"] as? [String: Any])?["ios-aarch64"] as? [String: Any])?["url"] as? String
        update = AppUpdate(version: version, url: download.flatMap(URL.init(string:)))
    }

    /// Dotted numeric comparison of Product Release versions ("6.0.1" > "6.0.0").
    static func isNewer(_ candidate: String, than current: String) -> Bool {
        let parts = { (v: String) in v.split(separator: ".").map { Int($0.prefix { $0.isNumber }) ?? 0 } }
        let a = parts(candidate), b = parts(current)
        for i in 0..<max(a.count, b.count) {
            let x = i < a.count ? a[i] : 0, y = i < b.count ? b[i] : 0
            if x != y { return x > y }
        }
        return false
    }

    func loadArtwork() async {
        await withTaskGroup(of: Void.self) { group in
            if let id = gameAppId { group.addTask { await self.artwork.load(id) } }
        }
    }

    var ready: Bool { failure == nil && snapshot.game != nil }
    var isSigningIn: Bool { ["connecting", "code", "qr"].contains(snapshot.state) && snapshot.busy }

    func action(for game: ServiceSnapshot.Game) -> GameAction {
        let s = snapshot
        if s.busy {
            if isSigningIn { return .working(s.message, nil) }
            return .working(s.message, s.state == "downloading" ? s.progress : nil)
        }
        if s.state == "outdated" { return .blocked(s.message) }
        if s.state == "ready" && s.canLaunch && s.loggedIn { return .play }
        if !s.loggedIn { return .connect }
        if s.state == "installed" || (game.downloaded && !game.compatibility.launch) {
            return .blocked(game.compatibility.reason.isEmpty ? "已下载，当前平台尚未开放启动" : game.compatibility.reason)
        }
        return game.downloaded ? .prepare : .download
    }

    /// One-line install state used on cards.
    func installLabel(_ game: ServiceSnapshot.Game) -> String {
        if snapshot.state == "outdated" { return "等待适配" }
        if snapshot.state == "ready" && snapshot.canLaunch { return "可以开始" }
        if snapshot.busy && !isSigningIn { return snapshot.state == "downloading" ? "下载中 \(Int(snapshot.progress * 100))%" : "准备中" }
        return game.downloaded ? "已下载" : "未安装"
    }

    // MARK: Commands

    func perform(_ game: ServiceSnapshot.Game) {
        switch action(for: game) {
        case .play: launchGame()
        case .connect: showingAccount = true
        case .download, .prepare: command(["action": "download"])
        default: break
        }
    }

    func login(username: String, password: String) {
        command(["action": "login", "username": username, "password": password])
    }
    func restore() { command(["action": "restore"]) }
    /// Sign in by scanning a code with the Steam app on another device (iPad, a second phone).
    func qrLogin() { command(["action": "qr"]) }
    func loginError() -> String? { snapshot.state == "error" && !snapshot.loggedIn ? snapshot.message : nil }
    func submit(code: String) { command(["action": "guard", "code": code]) }
    /// Stops the current login or download; the saved session stays available to restore.
    func cancel() { command(["action": "cancel"]) }
    func logout() { command(["action": "logout"]) }

    private func command(_ value: [String: Any]) {
        UIApplication.shared.sendAction(#selector(UIResponder.resignFirstResponder), to: nil, from: nil, for: nil)
        send(value as NSDictionary)
    }
}

/// Objective-C entry point for `app/Host/main.mm`, which keeps ownership of
/// the engine, the service bridge, the refresh timer and the game hand-off.
@objc(BPPLauncherHost)
public final class LauncherHost: NSObject {
    private let model: LauncherModel
    @objc public let viewController: UIViewController

    @MainActor
    @objc public init(command: @escaping (NSDictionary) -> Void, launch: @escaping () -> Void) {
        model = LauncherModel(send: command, launch: launch)
        let hosting = UIHostingController(rootView: LauncherRoot().environmentObject(model))
        hosting.view.backgroundColor = UIColor(Theme.background)
        viewController = hosting
        super.init()
        Theme.configureAppearance()
    }

    @MainActor @objc public func update(_ snapshot: NSDictionary) { model.apply(snapshot) }
    @MainActor @objc public func fail(_ message: String) { model.fail(message) }
    /// Called right before the engine takes over the window.
    @MainActor @objc public func releaseForGame() { model.artwork.releaseMemory() }
}
