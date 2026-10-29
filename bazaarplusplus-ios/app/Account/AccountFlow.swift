import CoreImage.CIFilterBuiltins
import SwiftUI
import UIKit

// MARK: - First-run introduction

struct OnboardingView: View {
    @EnvironmentObject var model: LauncherModel
    @State private var page = 0

    private struct Page {
        let icon: String
        let title: String
        let body: String
        let rows: [(String, String, String)]
    }

    private var pages: [Page] {
        let device = UIDevice.current.model
        return [
            Page(icon: "gamecontroller.fill", title: "把 Steam 游戏带到 \(device)",
                 body: "下载你在 Steam 上已拥有的游戏，直接在这台 \(device) 上运行，不需要电脑。", rows: []),
            Page(icon: "checklist", title: "开始前准备", body: "登录只需要一次，之后会自动保持。", rows: [
                ("person.crop.circle.badge.checkmark", "拥有游戏的 Steam 账号", "只有已购买 The Bazaar 的账号可以下载游戏。"),
                ("iphone", "Steam 手机 App", "登录时在手机上批准，或从 Steam 令牌查看验证码。"),
                ("wifi", "稳定的 Wi‑Fi 与存储空间", "首次下载是完整游戏，体积较大。"),
            ]),
            Page(icon: "lock.shield.fill", title: "你的账号安全", body: "登录直接与 Steam 进行。", rows: [
                ("key.fill", "密码不保存", "密码只发送给 Steam，用完即清除。"),
                ("lock.fill", "凭据存于系统钥匙串", "下次打开会自动登录，可随时退出。"),
                ("checkmark.seal.fill", "游戏认证在本机完成", "启动游戏时由本机向 Steam 获取认证。"),
            ]),
        ]
    }

    /// The game in front, with blank cards behind it.
    private var showcase: some View {
        let game = model.snapshot.game
        return ZStack {
            ForEach([-1.0, 1.0], id: \.self) { side in
                RoundedRectangle(cornerRadius: 18, style: .continuous)
                    .fill(Theme.surface)
                    .overlay(RoundedRectangle(cornerRadius: 18, style: .continuous).stroke(Theme.stroke))
                    .overlay(Image(systemName: "sparkles").font(.title2).foregroundColor(Theme.secondary.opacity(0.5)))
                    .frame(width: 150, height: 225)
                    .rotationEffect(.degrees(side * 9))
                    .offset(x: side * 82, y: 14)
            }
            if let game {
                ArtworkImage(url: model.store(game)?.capsule, appId: model.appId(game), maxPixel: 700) { MonogramArt(game: game) }
                    .frame(width: 180, height: 270)
                    .clipShape(RoundedRectangle(cornerRadius: 20, style: .continuous))
                    .overlay(RoundedRectangle(cornerRadius: 20, style: .continuous).stroke(Color.white.opacity(0.15)))
                    .shadow(color: Theme.brand.opacity(0.35), radius: 30, y: 12)
            }
        }
        .frame(height: 300)
    }

    var body: some View {
        let hero = model.snapshot.game
        ZStack {
            Theme.background.ignoresSafeArea()
            if let hero, let store = model.store(hero) {
                ArtworkImage(url: store.hero ?? store.header, appId: model.appId(hero), maxPixel: 1800) { Color.clear }
                    .ignoresSafeArea()
                    .opacity(0.45)
                    .overlay(LinearGradient(colors: [Theme.background.opacity(0.2), Theme.background.opacity(0.85), Theme.background],
                                            startPoint: .top, endPoint: .bottom).ignoresSafeArea())
            }
            VStack(spacing: 0) {
                HStack {
                    Spacer()
                    if page < pages.count - 1 {
                        Button("跳过") { model.finishOnboarding(connect: false) }
                            .font(.subheadline.weight(.semibold)).foregroundColor(Theme.secondary)
                            .accessibilityIdentifier("onboarding-skip")
                    }
                }
                .frame(height: 44)
                .padding(.horizontal, 20)
                TabView(selection: $page) {
                    ForEach(pages.indices, id: \.self) { index in
                        pageView(pages[index]).tag(index)
                    }
                }
                .tabViewStyle(.page(indexDisplayMode: .never))
                VStack(spacing: 18) {
                    HStack(spacing: 8) {
                        ForEach(pages.indices, id: \.self) { index in
                            Capsule().fill(index == page ? Theme.brand : Color.white.opacity(0.25))
                                .frame(width: index == page ? 22 : 8, height: 8)
                        }
                    }
                    .animation(.spring(response: 0.3), value: page)
                    if page < pages.count - 1 {
                        PrimaryButton(title: "继续", icon: "arrow.right", color: Theme.brand, foreground: .black) {
                            withAnimation { page += 1 }
                        }
                        .accessibilityIdentifier("onboarding-next")
                    } else {
                        PrimaryButton(title: "连接 Steam", icon: "person.badge.key.fill", color: Theme.brand, foreground: .black) {
                            model.finishOnboarding(connect: true)
                        }
                        .accessibilityIdentifier("onboarding-connect")
                        Button("稍后登录") { model.finishOnboarding(connect: false) }
                            .font(.subheadline.weight(.semibold)).foregroundColor(.white.opacity(0.85))
                    }
                }
                .padding(.horizontal, 24)
                .padding(.bottom, 24)
                .frame(maxWidth: 560)
            }
        }
        .preferredColorScheme(.dark)
    }

    private func pageView(_ page: Page) -> some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 22) {
                Spacer(minLength: 24)
                if page.rows.isEmpty {
                    showcase.frame(maxWidth: .infinity)
                } else {
                    ZStack {
                        RoundedRectangle(cornerRadius: 22, style: .continuous).fill(Theme.brand.opacity(0.16)).frame(width: 76, height: 76)
                        Image(systemName: page.icon).font(.system(size: 34, weight: .semibold)).foregroundColor(Theme.brand)
                    }
                }
                VStack(alignment: .leading, spacing: 10) {
                    Text(page.title).font(.system(size: 30, weight: .bold)).fixedSize(horizontal: false, vertical: true)
                    Text(page.body).font(.body).foregroundColor(.white.opacity(0.78)).fixedSize(horizontal: false, vertical: true)
                }
                if !page.rows.isEmpty {
                    VStack(spacing: 0) {
                        ForEach(Array(page.rows.enumerated()), id: \.offset) { index, row in
                            HStack(alignment: .top, spacing: 14) {
                                Image(systemName: row.0).font(.system(size: 19)).foregroundColor(Theme.brand).frame(width: 28)
                                VStack(alignment: .leading, spacing: 3) {
                                    Text(row.1).font(.subheadline.weight(.semibold))
                                    Text(row.2).font(.footnote).foregroundColor(Theme.secondary).fixedSize(horizontal: false, vertical: true)
                                }
                                Spacer(minLength: 0)
                            }
                            .padding(16)
                            if index < page.rows.count - 1 { Divider().background(Theme.stroke).padding(.leading, 58) }
                        }
                    }
                    .background(RoundedRectangle(cornerRadius: 16, style: .continuous).fill(Theme.surface.opacity(0.92)))
                    .overlay(RoundedRectangle(cornerRadius: 16, style: .continuous).stroke(Theme.stroke))
                }
            }
            .padding(.horizontal, 24)
            .frame(maxWidth: 560, alignment: .leading)
            .frame(maxWidth: .infinity)
        }
    }
}

// MARK: - Sign-in sheet

struct AccountSheet: View {
    @EnvironmentObject var model: LauncherModel
    @Environment(\.dismiss) private var dismiss
    @State private var verified = false
    // A phone usually runs the Steam app itself and cannot scan its own screen.
    @State private var method: SignInMethod = UIDevice.current.userInterfaceIdiom == .pad ? .qr : .password

    var body: some View {
        let step = model.loginStep
        NavigationView {
            ScrollView {
                VStack(alignment: .leading, spacing: 24) {
                    if step != .signedIn { StepIndicator(index: index(step)) }
                    Group {
                        switch step {
                        case .signedIn: SignedInStep { dismiss() }
                        case .credentials: CredentialsStep(method: $method)
                        case .qr(let url): QRStep(url: url, method: $method)
                        case .connecting(let message): WaitingStep(icon: "network", title: verified ? "正在完成登录" : "正在连接 Steam", message: message)
                        case .verify(let message, let viaApp): VerifyStep(message: message, viaApp: viaApp)
                        }
                    }
                    .transition(.asymmetric(insertion: .move(edge: .trailing).combined(with: .opacity), removal: .opacity))
                }
                .padding(20)
                .frame(maxWidth: 560)
                .frame(maxWidth: .infinity)
                .animation(.easeInOut(duration: 0.25), value: step)
            }
            .background(Theme.surface.ignoresSafeArea())
            .navigationTitle(step == .signedIn ? "Steam 账户" : "连接 Steam")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("关闭") { dismiss() }.foregroundColor(Theme.secondary)
                }
            }
        }
        .navigationViewStyle(.stack)
        .preferredColorScheme(.dark)
        .onChange(of: step) { next in
            switch next {
            case .verify: verified = true
            case .credentials, .signedIn: verified = false
            default: break
            }
        }
    }

    private func index(_ step: LoginStep) -> Int {
        switch step {
        case .credentials, .qr: return 0
        case .connecting: return verified ? 2 : 0
        case .verify: return 1
        case .signedIn: return 2
        }
    }
}

struct StepIndicator: View {
    let index: Int
    private let titles = ["账号", "验证", "完成"]
    var body: some View {
        HStack(spacing: 8) {
            ForEach(titles.indices, id: \.self) { i in
                VStack(alignment: .leading, spacing: 6) {
                    Capsule().fill(i <= index ? Theme.brand : Color.white.opacity(0.14)).frame(height: 4)
                    Text(titles[i]).font(.caption.weight(i == index ? .bold : .regular))
                        .foregroundColor(i <= index ? .white : Theme.secondary)
                }
            }
        }
        .animation(.easeInOut, value: index)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("第 \(index + 1) 步，共 3 步：\(titles[index])")
    }
}

private struct StepHeader: View {
    let icon: String
    let title: String
    let message: String
    var tint = Theme.brand
    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            ZStack {
                Circle().fill(tint.opacity(0.16)).frame(width: 64, height: 64)
                Image(systemName: icon).font(.system(size: 27, weight: .semibold)).foregroundColor(tint)
            }
            Text(title).font(.title2.bold())
            if !message.isEmpty {
                Text(message).font(.subheadline).foregroundColor(Theme.secondary).fixedSize(horizontal: false, vertical: true)
                    .accessibilityIdentifier("account-status")
            }
        }
    }
}

enum SignInMethod: Hashable { case password, qr }

struct CredentialsStep: View {
    @EnvironmentObject var model: LauncherModel
    @Binding var method: SignInMethod
    @State private var username = ""
    @State private var password = ""
    @FocusState private var focus: Field?
    enum Field { case username, password }

    var body: some View {
        let s = model.snapshot
        VStack(alignment: .leading, spacing: 20) {
            StepHeader(icon: "person.fill", title: "登录你的 Steam 账号",
                       message: "用于下载你已拥有的游戏，并在启动时完成游戏认证。")
            if let error = model.loginError() {
                Notice(icon: "exclamationmark.triangle.fill", text: error, color: Theme.warn)
            }
            if !s.savedAccount.isEmpty {
                VStack(alignment: .leading, spacing: 12) {
                    PrimaryButton(title: "继续以 \(s.savedAccount) 登录", icon: "arrow.clockwise", color: Theme.brand, foreground: .black) {
                        model.restore()
                    }
                    .accessibilityIdentifier("restore")
                    HStack {
                        Rectangle().fill(Theme.stroke).frame(height: 1)
                        Text("或使用其他账号").font(.caption).foregroundColor(Theme.secondary).fixedSize()
                        Rectangle().fill(Theme.stroke).frame(height: 1)
                    }
                }
            }
            Picker("登录方式", selection: $method) {
                Text("账号密码").tag(SignInMethod.password)
                Text("扫码登录").tag(SignInMethod.qr)
            }
            .pickerStyle(.segmented)
            .accessibilityIdentifier("sign-in-method")
            if method == .qr { QRStart() } else { passwordForm }
        }
    }

    private var passwordForm: some View {
        VStack(alignment: .leading, spacing: 20) {
            VStack(spacing: 12) {
                InputField(title: "Steam 账号名", text: $username, secure: false, focused: focus == .username)
                    .focused($focus, equals: .username)
                    .textContentType(.username)
                    .submitLabel(.next)
                    .onSubmit { focus = .password }
                InputField(title: "密码", text: $password, secure: true, focused: focus == .password)
                    .focused($focus, equals: .password)
                    .textContentType(.password)
                    .submitLabel(.go)
                    .onSubmit(submit)
                Text("账号名是登录 Steam 时用的名字，不是个人资料里的昵称。")
                    .font(.caption).foregroundColor(Theme.secondary).frame(maxWidth: .infinity, alignment: .leading)
            }
            PrimaryButton(title: "登录", icon: "arrow.right.circle.fill", color: Theme.brand, foreground: .black, action: submit)
                .disabled(!canSubmit)
                .opacity(canSubmit ? 1 : 0.5)
                .accessibilityIdentifier("login")
            Label("密码只发送给 Steam，不会保存。登录凭据保存在系统钥匙串，下次打开自动登录。", systemImage: "lock.fill")
                .font(.caption).foregroundColor(Theme.secondary)
        }
    }

    private var canSubmit: Bool { !username.isEmpty && !password.isEmpty && !model.snapshot.busy }

    private func submit() {
        guard canSubmit else { return }
        model.login(username: username.trimmingCharacters(in: .whitespaces), password: password)
        password = ""
    }
}

/// Before a QR session exists: explain, then start one on request so an abandoned
/// screen never keeps a Steam auth session open.
struct QRStart: View {
    @EnvironmentObject var model: LauncherModel
    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            QRInstructions()
            PrimaryButton(title: "显示二维码", icon: "qrcode", color: Theme.brand, foreground: .black) { model.qrLogin() }
                .disabled(model.snapshot.busy)
                .accessibilityIdentifier("show-qr")
            if UIDevice.current.userInterfaceIdiom != .pad {
                Text("需要另一台已登录 Steam 手机 App 的设备来扫描；本机上的 Steam App 无法扫描本机屏幕。")
                    .font(.caption).foregroundColor(Theme.secondary).fixedSize(horizontal: false, vertical: true)
            }
        }
    }
}

struct QRInstructions: View {
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            QRInstruction(number: 1, text: "在手机上打开 Steam App，点底部的盾牌图标（Steam 令牌）")
            QRInstruction(number: 2, text: "选择“扫描二维码”，对准这里的二维码")
            QRInstruction(number: 3, text: "核对设备名和地点后批准")
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 16, style: .continuous).fill(Theme.raised))
    }
}

struct QRInstruction: View {
    let number: Int
    let text: String
    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Text("\(number)").font(.caption.weight(.bold)).foregroundColor(.black)
                .frame(width: 22, height: 22).background(Circle().fill(Theme.brand))
            Text(text).font(.subheadline).fixedSize(horizontal: false, vertical: true)
        }
    }
}

struct QRStep: View {
    @EnvironmentObject var model: LauncherModel
    let url: String
    @Binding var method: SignInMethod

    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            VStack(alignment: .leading, spacing: 8) {
                Text("扫码登录").font(.title2.bold())
                Text("二维码会定期刷新，扫描批准后自动继续。").font(.subheadline).foregroundColor(Theme.secondary)
                    .accessibilityIdentifier("account-status")
            }
            HStack {
                Spacer()
                QRCodeImage(text: url)
                    .frame(width: 240, height: 240)
                    .padding(18)
                    .background(RoundedRectangle(cornerRadius: 22, style: .continuous).fill(Color.white))
                    .accessibilityLabel("Steam 登录二维码")
                    .accessibilityIdentifier("qr-code")
                Spacer()
            }
            QRInstructions()
            HStack(spacing: 10) { ProgressView(); Text("等待扫描…").font(.footnote).foregroundColor(Theme.secondary) }
            Button {
                method = .password
                model.cancel()
            } label: {
                Text("改用账号密码").frame(maxWidth: .infinity, minHeight: 44)
            }
            .foregroundColor(.white)
            CancelLoginButton()
        }
    }
}

struct QRCodeImage: View {
    let text: String
    var body: some View {
        if let image = Self.render(text) {
            Image(uiImage: image).interpolation(.none).resizable().scaledToFit()
        } else {
            Color.white
        }
    }

    static func render(_ text: String) -> UIImage? {
        let filter = CIFilter.qrCodeGenerator()
        filter.message = Data(text.utf8)
        filter.correctionLevel = "M"
        guard let output = filter.outputImage?.transformed(by: CGAffineTransform(scaleX: 10, y: 10)),
              let cg = CIContext().createCGImage(output, from: output.extent) else { return nil }
        return UIImage(cgImage: cg)
    }
}

struct InputField: View {
    let title: String
    @Binding var text: String
    let secure: Bool
    let focused: Bool
    var body: some View {
        Group {
            if secure { SecureField(title, text: $text) } else { TextField(title, text: $text) }
        }
        .textInputAutocapitalization(.never)
        .autocorrectionDisabled()
        .padding(.horizontal, 16)
        .frame(height: 52)
        .background(RoundedRectangle(cornerRadius: 14, style: .continuous).fill(Theme.raised))
        .overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).stroke(focused ? Theme.brand.opacity(0.7) : Theme.stroke))
        .accessibilityIdentifier(title)
    }
}

struct WaitingStep: View {
    @EnvironmentObject var model: LauncherModel
    let icon: String
    let title: String
    let message: String
    var body: some View {
        VStack(alignment: .leading, spacing: 22) {
            StepHeader(icon: icon, title: title, message: message)
            HStack(spacing: 10) { ProgressView(); Text("请稍候…").font(.subheadline).foregroundColor(Theme.secondary) }
            CancelLoginButton()
        }
    }
}

/// Steam Guard, the way Steam's own clients present it: approve on the phone or type
/// the code, whichever comes first. The service accepts both without restarting.
struct VerifyStep: View {
    @EnvironmentObject var model: LauncherModel
    let message: String
    let viaApp: Bool
    @State private var code = ""
    @State private var pulse = false
    @FocusState private var focused: Bool
    private let length = 5

    var body: some View {
        VStack(alignment: .leading, spacing: 22) {
            ZStack {
                if viaApp {
                    ForEach(0..<2) { ring in
                        Circle().stroke(Theme.brand.opacity(0.35), lineWidth: 2)
                            .frame(width: 64, height: 64)
                            .scaleEffect(pulse ? 1.9 : 1)
                            .opacity(pulse ? 0 : 1)
                            .animation(.easeOut(duration: 1.8).repeatForever(autoreverses: false).delay(Double(ring) * 0.9), value: pulse)
                    }
                }
                Circle().fill(Theme.brand.opacity(0.16)).frame(width: 64, height: 64)
                Image(systemName: viaApp ? "iphone.radiowaves.left.and.right" : "envelope.fill")
                    .font(.system(size: 27, weight: .semibold)).foregroundColor(Theme.brand)
            }
            .frame(width: 120, height: 80, alignment: .leading)
            .onAppear { pulse = true }
            VStack(alignment: .leading, spacing: 8) {
                Text(viaApp ? "确认是你本人" : "输入邮件验证码").font(.title2.bold())
                Text(message).font(.subheadline).foregroundColor(Theme.secondary).fixedSize(horizontal: false, vertical: true)
                    .accessibilityIdentifier("account-status")
            }
            if viaApp {
                HStack(spacing: 10) {
                    ProgressView()
                    Text("在手机上批准后会自动继续").font(.footnote).foregroundColor(Theme.secondary)
                }
                HStack {
                    Rectangle().fill(Theme.stroke).frame(height: 1)
                    Text("或输入验证码").font(.caption).foregroundColor(Theme.secondary).fixedSize()
                    Rectangle().fill(Theme.stroke).frame(height: 1)
                }
            }
            codeBoxes
            PrimaryButton(title: "提交验证码", icon: "checkmark.shield.fill", color: Theme.brand, foreground: .black, action: submit)
                .disabled(code.count < length).opacity(code.count < length ? 0.5 : 1)
            if viaApp {
                Notice(icon: "exclamationmark.shield.fill",
                       text: "手机上会显示设备名和大致地点。如果提示地点可疑，或这不是你本人发起的登录，请拒绝。", color: Theme.secondary)
            }
            CancelLoginButton()
        }
    }

    private var codeBoxes: some View {
        ZStack {
            TextField("", text: $code)
                .keyboardType(.asciiCapable)
                .textContentType(.oneTimeCode)
                .textInputAutocapitalization(.characters)
                .autocorrectionDisabled()
                .focused($focused)
                .opacity(0.02)
                .accessibilityIdentifier("Steam Guard 验证码")
                .onChange(of: code) { value in
                    let cleaned = String(value.uppercased().filter { $0.isLetter || $0.isNumber }.prefix(length))
                    if cleaned != value { code = cleaned }
                    if cleaned.count == length { submit() }
                }
            HStack(spacing: 10) {
                ForEach(0..<length, id: \.self) { i in
                    let chars = Array(code)
                    let current = i == chars.count && focused
                    Text(i < chars.count ? String(chars[i]) : "")
                        .font(.system(size: 26, weight: .bold, design: .monospaced))
                        .frame(maxWidth: .infinity, minHeight: 60)
                        .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(Theme.raised))
                        .overlay(RoundedRectangle(cornerRadius: 12, style: .continuous)
                            .stroke(current ? Theme.brand : Theme.stroke, lineWidth: current ? 2 : 1))
                }
            }
            .allowsHitTesting(false)
        }
        .contentShape(Rectangle())
        .onTapGesture { focused = true }
    }

    private func submit() {
        guard code.count == length else { return }
        model.submit(code: code)
        code = ""
    }
}

struct CancelLoginButton: View {
    @EnvironmentObject var model: LauncherModel
    var body: some View {
        Button(role: .cancel) { model.cancel() } label: {
            Text("取消登录").frame(maxWidth: .infinity, minHeight: 44)
        }
        .foregroundColor(Theme.secondary)
        .accessibilityIdentifier("cancel-login")
    }
}

struct SignedInStep: View {
    @EnvironmentObject var model: LauncherModel
    let done: () -> Void
    @State private var confirmingLogout = false

    var body: some View {
        let account = model.snapshot.account
        VStack(alignment: .leading, spacing: 22) {
            HStack(spacing: 16) {
                ZStack {
                    Circle().fill(LinearGradient(colors: [Theme.brand, Theme.brand.opacity(0.55)], startPoint: .topLeading, endPoint: .bottomTrailing))
                        .frame(width: 64, height: 64)
                    Text(String(account.prefix(1)).uppercased()).font(.title.bold()).foregroundColor(.black)
                }
                VStack(alignment: .leading, spacing: 4) {
                    Text(account.isEmpty ? "Steam 账号" : account).font(.title3.bold())
                    Label("已连接", systemImage: "checkmark.circle.fill").font(.subheadline).foregroundColor(Theme.ok)
                        .accessibilityIdentifier("account-status")
                }
            }
            VStack(spacing: 0) {
                InfoRow(label: "自动登录", value: "已开启")
                InfoRow(label: "登录凭据", value: "系统钥匙串")
                InfoRow(label: "游戏认证", value: "本机向 Steam 获取", last: true)
            }
            .background(RoundedRectangle(cornerRadius: 14, style: .continuous).fill(Theme.raised))
            PrimaryButton(title: "完成", icon: "checkmark", color: Theme.brand, foreground: .black, action: done)
            Button(role: .destructive) { confirmingLogout = true } label: {
                Text("退出登录").frame(maxWidth: .infinity, minHeight: 44)
            }
            .accessibilityIdentifier("logout")
            .confirmationDialog("退出 Steam 登录？", isPresented: $confirmingLogout, titleVisibility: .visible) {
                Button("退出登录", role: .destructive) { model.logout() }
            } message: {
                Text("会清除本机保存的登录凭据。已下载的游戏会保留，下次需要重新输入密码并在手机上批准。")
            }
        }
    }
}
