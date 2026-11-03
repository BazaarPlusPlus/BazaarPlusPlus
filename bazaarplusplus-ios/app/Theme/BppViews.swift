import SwiftUI
import UIKit

// The design system's components (design/components/) for the iOS App, over the generated
// tokens in BppTokens.g.swift. Views never write a colour, size or radius literal; they use
// these and the Bpp* token enums.

// MARK: - Type

extension View {
    /// A type style at the current Dynamic Type size: every style scales relative to body.
    func bppFont(_ style: BppTextStyle) -> some View { modifier(BppFontModifier(style: style)) }
}

private struct BppFontModifier: ViewModifier {
    @Environment(\.dynamicTypeSize) private var typeSize
    let style: BppTextStyle

    func body(content: Content) -> some View {
        let traits = UITraitCollection(preferredContentSizeCategory: typeSize.contentSizeCategory)
        let points = UIFontMetrics(forTextStyle: .body).scaledValue(for: style.size, compatibleWith: traits)
        // The system font's own leading is about 1.2 of its size; the token's line height adds the rest.
        return content
            .font(.system(size: points, weight: style.weight, design: style.monospaced ? .monospaced : .default))
            .lineSpacing(max(0, points * (style.lineHeight - 1.2)))
    }
}

extension DynamicTypeSize {
    var contentSizeCategory: UIContentSizeCategory {
        switch self {
        case .xSmall: return .extraSmall
        case .small: return .small
        case .medium: return .medium
        case .large: return .large
        case .xLarge: return .extraLarge
        case .xxLarge: return .extraExtraLarge
        case .xxxLarge: return .extraExtraExtraLarge
        case .accessibility1: return .accessibilityMedium
        case .accessibility2: return .accessibilityLarge
        case .accessibility3: return .accessibilityExtraLarge
        case .accessibility4: return .accessibilityExtraExtraLarge
        case .accessibility5: return .accessibilityExtraExtraExtraLarge
        @unknown default: return .large
        }
    }
}

// MARK: - Panel

extension View {
    /// Panel: `bg-panel` framed by a 1px `line`, at `r-panel` unless a container asks for more.
    func bppPanel(radius: CGFloat = BppRadius.rPanel) -> some View {
        background(RoundedRectangle(cornerRadius: radius, style: .continuous).fill(BppColor.bgPanel))
            .overlay(RoundedRectangle(cornerRadius: radius, style: .continuous).strokeBorder(BppColor.line))
            .clipShape(RoundedRectangle(cornerRadius: radius, style: .continuous))
    }
}

// MARK: - Button

/// Button (design/components/Button) at the Apps' touch size: full width, `touch-target` high, `r-touch`.
struct BppButtonStyle: ButtonStyle {
    enum Kind { case primary, secondary, ghost }
    var kind: Kind
    var fullWidth = true

    func makeBody(configuration: Configuration) -> some View {
        BppButtonBody(configuration: configuration, kind: kind, fullWidth: fullWidth)
    }
}

private struct BppButtonBody: View {
    @Environment(\.isEnabled) private var enabled
    let configuration: ButtonStyle.Configuration
    let kind: BppButtonStyle.Kind
    let fullWidth: Bool

    var body: some View {
        let pressed = configuration.isPressed
        let shape = RoundedRectangle(cornerRadius: BppRadius.rTouch, style: .continuous)
        configuration.label
            .bppFont(kind == .primary ? BppType.bodyStrong : BppType.bodyMedium)
            .foregroundColor(foreground(pressed))
            .multilineTextAlignment(.center)
            .padding(.horizontal, BppSpace.space12)
            .padding(.vertical, BppSpace.space8)
            .frame(maxWidth: fullWidth ? .infinity : nil, minHeight: BppSize.touchTarget)
            .background(shape.fill(fill(pressed)))
            .overlay(kind == .secondary ? shape.strokeBorder(BppColor.lineStrong) : nil)
            .contentShape(shape)
            .opacity(enabled ? 1 : 0.45)
            .animation(.easeOut(duration: 0.1), value: pressed)
    }

    private func fill(_ pressed: Bool) -> Color {
        switch kind {
        case .primary: return pressed ? BppColor.accentHover : BppColor.accent
        case .secondary: return pressed ? BppColor.bgHover : BppColor.bgPanel
        case .ghost: return pressed ? BppColor.bgHover : .clear
        }
    }

    private func foreground(_ pressed: Bool) -> Color {
        switch kind {
        case .primary: return BppColor.onAccent
        case .secondary: return BppColor.text1
        case .ghost: return pressed ? BppColor.text1 : BppColor.text2
        }
    }
}

/// The one primary action of a view, with an optional leading SF Symbol.
struct BppPrimaryButton: View {
    let title: String
    var icon: String?
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: BppSpace.space6) {
                if let icon { Image(systemName: icon) }
                Text(title)
            }
        }
        .buttonStyle(BppButtonStyle(kind: .primary))
    }
}

// MARK: - Badge

/// Badge (design/components/Badge): dot + word, never a colour alone.
struct BppBadge: View {
    enum Tone { case ok, warn, bad, busy, neutral }
    let word: String
    let tone: Tone
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var dim = false

    var body: some View {
        HStack(spacing: BppSpace.space6) {
            Circle().fill(color).frame(width: BppSpace.space6, height: BppSpace.space6)
                .opacity(dim ? 0.3 : 1)
            Text(word).bppFont(BppType.small).foregroundColor(color)
        }
        .padding(.horizontal, BppSpace.space8)
        .frame(minHeight: BppSize.badgeHeight)
        .background(Capsule().fill(fill))
        .onAppear(perform: pulse)
        .onChange(of: tone) { _ in pulse() }
    }

    /// A busy dot pulses at 1.2s, unless Reduce Motion is on.
    private func pulse() {
        guard tone == .busy, !reduceMotion else {
            withAnimation(nil) { dim = false }
            return
        }
        withAnimation(.easeInOut(duration: 0.6).repeatForever(autoreverses: true)) { dim = true }
    }

    private var color: Color {
        switch tone {
        case .ok: return BppColor.success
        case .warn: return BppColor.warning
        case .bad: return BppColor.dangerText
        case .busy: return BppColor.accent
        case .neutral: return BppColor.text2
        }
    }

    private var fill: Color {
        switch tone {
        case .ok: return BppColor.successSubtle
        case .warn: return BppColor.warningSubtle
        case .bad: return BppColor.dangerSubtle
        case .busy: return BppColor.accentSubtle
        case .neutral: return BppColor.bgHover
        }
    }
}

// MARK: - StatusBanner

/// StatusBanner (design/components/StatusBanner): tone icon, one sentence, an optional `sm` action
/// whose hit area extends to `touch-target`. The icons are the SF Symbols that match the Lucide set.
struct BppBanner: View {
    enum Tone { case info, warning, error }
    let tone: Tone
    let message: String
    var actionTitle: String?
    var action: (() -> Void)?

    var body: some View {
        HStack(alignment: .center, spacing: BppSpace.space8) {
            Image(systemName: icon)
                .font(.system(size: BppSize.iconMd))
                .foregroundColor(iconColor)
                .accessibilityHidden(true)
            Text(message)
                .bppFont(BppType.body)
                .foregroundColor(BppColor.text1)
                .fixedSize(horizontal: false, vertical: true)
                .textSelection(.enabled)
                .frame(maxWidth: .infinity, alignment: .leading)
            if let actionTitle, let action {
                Button(action: action) {
                    Text(actionTitle)
                        .bppFont(BppType.small)
                        .foregroundColor(BppColor.text1)
                        .padding(.horizontal, BppSpace.space8)
                        .frame(minHeight: BppSize.controlSm)
                        .background(RoundedRectangle(cornerRadius: BppRadius.rControl, style: .continuous).fill(BppColor.bgPanel))
                        .overlay(RoundedRectangle(cornerRadius: BppRadius.rControl, style: .continuous).strokeBorder(BppColor.lineStrong))
                        .frame(minHeight: BppSize.touchTarget)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            }
        }
        .padding(.horizontal, BppSpace.space12)
        .padding(.vertical, BppSpace.space4)
        .frame(minHeight: BppSize.touchTarget)
        .background(RoundedRectangle(cornerRadius: BppRadius.rPanel, style: .continuous).fill(fill))
        .overlay(RoundedRectangle(cornerRadius: BppRadius.rPanel, style: .continuous).strokeBorder(border))
        .accessibilityElement(children: .combine)
    }

    private var icon: String {
        switch tone {
        case .info: return "info.circle"
        case .warning: return "exclamationmark.triangle"
        case .error: return "xmark.circle"
        }
    }

    private var iconColor: Color {
        switch tone {
        case .info: return BppColor.text2
        case .warning: return BppColor.warning
        case .error: return BppColor.danger
        }
    }

    private var fill: Color {
        switch tone {
        case .info: return BppColor.bgPanel
        case .warning: return BppColor.warningSubtle
        case .error: return BppColor.dangerSubtle
        }
    }

    private var border: Color {
        switch tone {
        case .info: return BppColor.line
        case .warning: return BppColor.warningLine
        case .error: return BppColor.dangerLine
        }
    }
}

// MARK: - Key-value row

/// One key-value row: at least `row-height`, the key in `text-3`, the value end-aligned in tabular figures.
/// `inset` is the row's side padding inside a Panel; rows on the page ground sit flush.
struct BppKeyValueRow: View {
    let key: String
    let value: String
    var last = false
    var inset = BppSpace.space16

    var body: some View {
        VStack(spacing: 0) {
            HStack(alignment: .firstTextBaseline, spacing: BppSpace.space16) {
                Text(key).bppFont(BppType.body).foregroundColor(BppColor.text3)
                Spacer(minLength: 0)
                Text(value.isEmpty ? "—" : value)
                    .bppFont(BppType.body)
                    .monospacedDigit()
                    .foregroundColor(BppColor.text1)
                    .multilineTextAlignment(.trailing)
            }
            .padding(.horizontal, inset)
            .frame(minHeight: BppSize.rowHeight)
            if !last { Rectangle().fill(BppColor.line).frame(height: 1) }
        }
    }
}

// MARK: - Shapes

/// A rectangle with only its top corners rounded: the launcher's panel over the cover (iOS 15 has no
/// UnevenRoundedRectangle).
struct BppTopRoundedRectangle: Shape {
    var radius: CGFloat

    func path(in rect: CGRect) -> Path {
        Path(UIBezierPath(roundedRect: rect, byRoundingCorners: [.topLeft, .topRight],
                          cornerRadii: CGSize(width: radius, height: radius)).cgPath)
    }
}

// MARK: - UIKit chrome

/// The UIKit bars SwiftUI still draws (the sign-in sheet's navigation bar): transparent, `text-1`.
enum BppAppearance {
    static func configure() {
        let bar = UINavigationBarAppearance()
        bar.configureWithTransparentBackground()
        bar.titleTextAttributes = [.foregroundColor: UIColor(BppColor.text1)]
        bar.largeTitleTextAttributes = [.foregroundColor: UIColor(BppColor.text1)]
        UINavigationBar.appearance().standardAppearance = bar
        UINavigationBar.appearance().scrollEdgeAppearance = bar
        UINavigationBar.appearance().compactAppearance = bar
        UINavigationBar.appearance().tintColor = UIColor(BppColor.text1)
    }
}
