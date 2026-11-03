// BazaarPlusPlus design tokens, generated from design/tokens.json by
// `just design::build`. Do not edit: change design/tokens.json and rebuild.
import SwiftUI

/// The product colours (design/README.md, Colour).
enum BppColor {
    /// Window and page ground: the darkest step of the warm near-black ladder. Input and select fills, code/diagnostic wells and dialog notes also sit on it.
    static let bgCanvas = Color(.sRGB, red: 17.0 / 255, green: 16.0 / 255, blue: 15.0 / 255, opacity: 1)
    /// Title bar (40px header) and the 72px icon rail.
    static let bgSidebar = Color(.sRGB, red: 23.0 / 255, green: 22.0 / 255, blue: 21.0 / 255, opacity: 1)
    /// Panels, dialog cards and secondary buttons. Elevation is this fill step plus a 1px `line`, never a shadow.
    static let bgPanel = Color(.sRGB, red: 29.0 / 255, green: 28.0 / 255, blue: 26.0 / 255, opacity: 1)
    /// Hover fill for rows, rail items, menu items and ghost/secondary buttons; also the neutral badge fill and the dialog icon tile.
    static let bgHover = Color(.sRGB, red: 38.0 / 255, green: 36.0 / 255, blue: 34.0 / 255, opacity: 1)
    /// Selected rail item and segmented option; the fill of floating layers (menus, popovers, toasts).
    static let bgSelected = Color(.sRGB, red: 44.0 / 255, green: 42.0 / 255, blue: 39.0 / 255, opacity: 1)
    /// 1px dividers: panel borders, row separators, header and rail edges, dialog footer rule. Decorative separation only (1.16:1 on bg-panel).
    static let line = Color(.sRGB, red: 42.0 / 255, green: 40.0 / 255, blue: 38.0 / 255, opacity: 1)
    /// Borders of buttons and floating layers: secondary buttons, dialog cards, toasts, popovers; also the scrollbar thumb. Labelled controls only. Form fields use line-control.
    static let lineStrong = Color(.sRGB, red: 58.0 / 255, green: 55.0 / 255, blue: 51.0 / 255, opacity: 1)
    /// Border of inputs and selects, whose outline is the only thing that shows the field. Fields sit only on bg-canvas and bg-panel, where it holds the 3:1 non-text floor (3.3:1 and 3.0:1).
    static let lineControl = Color(.sRGB, red: 108.0 / 255, green: 102.0 / 255, blue: 95.0 / 255, opacity: 1)
    /// Primary text and titles. Reads 15.9:1 on bg-canvas, 15.1:1 on bg-sidebar, 14.3:1 on bg-panel, 12.9:1 on bg-hover, 12.0:1 on bg-selected.
    static let text1 = Color(.sRGB, red: 237.0 / 255, green: 235.0 / 255, blue: 231.0 / 255, opacity: 1)
    /// Secondary text: dialog descriptions, ghost buttons, field labels, neutral badges, the misfortune pill, idle icons. Reads 7.3:1 on bg-canvas, 7.0:1 on bg-sidebar, 6.6:1 on bg-panel, 6.0:1 on bg-hover, 5.5:1 on bg-selected.
    static let text2 = Color(.sRGB, red: 165.0 / 255, green: 161.0 / 255, blue: 154.0 / 255, opacity: 1)
    /// Tertiary text: meta, keys, hints, section titles, empty-state and select-chevron icons. Reads 6.0:1 on bg-canvas, 5.7:1 on bg-sidebar, 5.3:1 on bg-panel, 4.8:1 on bg-hover, 4.5:1 on bg-selected.
    static let text3 = Color(.sRGB, red: 149.0 / 255, green: 144.0 / 255, blue: 138.0 / 255, opacity: 1)
    /// The amber of the brand mark's gem: primary button fill, the active rail icon, focus rings, input focus border, checkbox accent. As text (links, key numbers, toast actions) it reads 6.9:1 on bg-panel, 6.2:1 on bg-hover, 5.8:1 on bg-selected; on its own accent-subtle tint 5.5:1 over bg-panel, 5.0:1 over bg-hover. Never decoration.
    static let accent = Color(.sRGB, red: 238.0 / 255, green: 142.0 / 255, blue: 31.0 / 255, opacity: 1)
    /// Hover state of the primary button, toast actions and dialog links.
    static let accentHover = Color(.sRGB, red: 245.0 / 255, green: 161.0 / 255, blue: 66.0 / 255, opacity: 1)
    /// Tinted fill behind accent text: busy and accent badges, the accent dialog icon tile, the input focus halo, the selected hero chip. Every -subtle fill is the hue at 14%.
    static let accentSubtle = Color(.sRGB, red: 238.0 / 255, green: 142.0 / 255, blue: 31.0 / 255, opacity: 0.14)
    /// Accent outline: the selected hero chip border, and the text selection highlight. Every -line is the hue at 32%.
    static let accentLine = Color(.sRGB, red: 238.0 / 255, green: 142.0 / 255, blue: 31.0 / 255, opacity: 0.32)
    /// Label on accent fills (primary button): 7.5:1 on accent, 8.9:1 on accent-hover.
    static let onAccent = Color(.sRGB, red: 26.0 / 255, green: 18.0 / 255, blue: 6.0 / 255, opacity: 1)
    /// Status only: ok badge, success banner and toast icon, active run outcome, rating up. As text it reads 6.3:1 on bg-panel, 5.7:1 on bg-hover; on success-subtle 5.0:1 over bg-panel, 4.6:1 over bg-hover.
    static let success = Color(.sRGB, red: 69.0 / 255, green: 178.0 / 255, blue: 107.0 / 255, opacity: 1)
    /// Fill of ok badges, active-run pills and success banners.
    static let successSubtle = Color(.sRGB, red: 69.0 / 255, green: 178.0 / 255, blue: 107.0 / 255, opacity: 0.14)
    /// Border of success banners. Every -line is the hue at 32%.
    static let successLine = Color(.sRGB, red: 69.0 / 255, green: 178.0 / 255, blue: 107.0 / 255, opacity: 0.32)
    /// Status only: warn badge, warning banner, toast and confirm-note icon. As text it reads 7.5:1 on bg-panel, 6.8:1 on bg-hover; on warning-subtle 5.8:1 over bg-panel, 5.2:1 over bg-hover.
    static let warning = Color(.sRGB, red: 217.0 / 255, green: 163.0 / 255, blue: 59.0 / 255, opacity: 1)
    /// Fill of warn badges, warning banners and warning notes.
    static let warningSubtle = Color(.sRGB, red: 217.0 / 255, green: 163.0 / 255, blue: 59.0 / 255, opacity: 0.14)
    /// Border of warning banners and warning notes.
    static let warningLine = Color(.sRGB, red: 217.0 / 255, green: 163.0 / 255, blue: 59.0 / 255, opacity: 0.32)
    /// Status only, for icons and dots: the error banner, toast and dialog icons, the danger confirm-note icon, rating down. Icon contrast 4.7:1 against bg-panel and 3.9:1 against bg-selected (icons need 3:1). Words over a danger fill use danger-text; danger itself falls under 4.5:1 over its own tint.
    static let danger = Color(.sRGB, red: 229.0 / 255, green: 88.0 / 255, blue: 78.0 / 255, opacity: 1)
    /// Fill of danger buttons, bad badges, error banners and danger notes.
    static let dangerSubtle = Color(.sRGB, red: 229.0 / 255, green: 88.0 / 255, blue: 78.0 / 255, opacity: 0.14)
    /// Border of danger buttons, error banners and danger notes, and the danger button's hover fill.
    static let dangerLine = Color(.sRGB, red: 229.0 / 255, green: 88.0 / 255, blue: 78.0 / 255, opacity: 0.32)
    /// The label in the danger button and the word in the bad badge, both painted over danger-subtle. Through that tint it reads 6.3:1 over bg-panel and 5.7:1 over bg-hover; through danger-line (the button's hover) 4.8:1 over bg-panel. Bare it reads 7.4:1 on bg-panel and 6.7:1 on bg-hover. Never put it on a solid danger fill.
    static let dangerText = Color(.sRGB, red: 255.0 / 255, green: 138.0 / 255, blue: 128.0 / 255, opacity: 1)
    /// Bronze run-outcome pill: text on a 14% tint of itself. Reads 6.4:1 on bg-panel, 5.8:1 on bg-hover; on its tint 5.1:1 over bg-panel, 4.6:1 over bg-hover.
    static let tierBronze = Color(.sRGB, red: 202.0 / 255, green: 148.0 / 255, blue: 100.0 / 255, opacity: 1)
    /// Silver run-outcome pill. Reads 9.6:1 on bg-panel, 8.7:1 on bg-hover; on its tint 7.1:1 over bg-panel, 6.3:1 over bg-hover.
    static let tierSilver = Color(.sRGB, red: 191.0 / 255, green: 195.0 / 255, blue: 202.0 / 255, opacity: 1)
    /// Diamond run-outcome pill. Reads 9.0:1 on bg-panel, 8.2:1 on bg-hover; on its tint 6.7:1 over bg-panel, 6.1:1 over bg-hover. Gold uses accent, misfortune text-2, an active run success.
    static let tierDiamond = Color(.sRGB, red: 124.0 / 255, green: 198.0 / 255, blue: 238.0 / 255, opacity: 1)
    /// Modal backdrop behind dialogs; dims without blur.
    static let backdrop = Color(.sRGB, red: 0.0 / 255, green: 0.0 / 255, blue: 0.0 / 255, opacity: 0.55)
    /// Hover fill of the Windows close button (the platform's own red, with white). Windows only; never elsewhere.
    static let windowClose = Color(.sRGB, red: 196.0 / 255, green: 43.0 / 255, blue: 28.0 / 255, opacity: 1)
}

/// The 4pt spacing scale.
enum BppSpace {
    /// Insides of a control: segmented-control inset and gap, header icon group gap.
    static let space2: CGFloat = 2
    /// Rail item gap, menu padding, key-value action gap.
    static let space4: CGFloat = 4
    /// Icon to label inside buttons and badges, label to field.
    static let space6: CGFloat = 6
    /// Between sibling controls: button groups, dialog footer, toasts, field rows.
    static let space8: CGFloat = 8
    /// Banner, toast and note padding, row vertical padding, dialog body gap, button md side padding.
    static let space12: CGFloat = 12
    /// Panel and dialog padding, row side padding, gap between page sections.
    static let space16: CGFloat = 16
    /// Page padding around the installer's main column (40px at the foot).
    static let space24: CGFloat = 24
}

/// Corner radii by nesting level.
enum BppRadius {
    /// An element nested inside a control: the checked segment.
    static let rInner: CGFloat = 4
    /// Anything you click or type into: buttons, inputs, selects, segmented control, menu items, code wells.
    static let rControl: CGFloat = 6
    /// Containers: panels, banners, notes, toasts, popovers, rail items, dialog icon tiles.
    static let rPanel: CGFloat = 8
    /// Dialog cards, and in the App launchers the panel's top corners over the cover and the Android dialogs.
    static let rDialog: CGFloat = 12
    /// The App launchers' touch surfaces: the source row, and the iOS App's full-width buttons. Android buttons are r-pill.
    static let rTouch: CGFloat = 12
    /// Badges, hero chips and outcome pills: every 24px label is a full pill.
    static let rPill: CGFloat = 999
}

/// Fixed dimensions: control heights, touch targets and widths.
enum BppSize {
    /// Default button, icon button, input and select height; page-header row; dialog icon tile.
    static let controlMd: CGFloat = 36
    /// Dense controls: sm and icon-sm buttons, title-bar icon controls, toast close.
    static let controlSm: CGFloat = 30
    /// Badges, hero chips and outcome pills.
    static let badgeHeight: CGFloat = 24
    /// Every list row: settings rows, key-value rows, disclosure summaries. Content centres vertically, so rows in neighbouring panels line up.
    static let rowHeight: CGFloat = 52
    /// Icons in sm and icon-sm buttons and inside badges or selects.
    static let iconSm: CGFloat = 14
    /// Icons in md buttons, banners, toasts, menus and dialog tiles.
    static let iconMd: CGFloat = 16
    /// Rail icons and StatusPanel marks.
    static let iconLg: CGFloat = 22
    /// The app's own title bar; macOS reserves 78px on its left for the traffic lights.
    static let titlebarHeight: CGFloat = 44
    /// Icon rail column; items are 60 × 56px.
    static let railWidth: CGFloat = 72
    /// Max width of the installer's main column.
    static let contentMax: CGFloat = 1080
    /// Minimum hit area of everything tappable in the iOS App and the Android App, and the height of their full-width action buttons. Smaller boxes (banner actions, the More button) extend their hit area to it. 48pt on iOS (above Apple's 44pt), 48dp on Android.
    static let touchTarget: CGFloat = 48
    /// Minimum height of the App launchers' source row: the Steam account on iOS, the official game on Android. It grows with the system text size.
    static let sourceRowHeight: CGFloat = 56
    /// The download progress track in the App launchers, the one place the system shows a progress bar.
    static let progressHeight: CGFloat = 4
    /// Window width at which the App launchers change from one column to the cover on top of two equal panes: iPhone Duo unfolded, unfolded foldables, tablets. Android's compact/medium window-size boundary. Accessibility text sizes keep one column.
    static let paneBreakpoint: CGFloat = 600
    /// Window width from which the App launchers put the cover in the start half and the content in the end half, if the window is wider than tall. Android's medium/expanded window-size boundary.
    static let coverPaneBreakpoint: CGFloat = 840
}

/// One text style: size in points, line height as a multiple, weight.
struct BppTextStyle {
    let size: CGFloat
    let lineHeight: CGFloat
    let weight: Font.Weight
    let monospaced: Bool
}

/// The type scale (design/README.md, Typography).
enum BppType {
    static let display = BppTextStyle(size: 24, lineHeight: 1.3, weight: .semibold, monospaced: false)
    static let title = BppTextStyle(size: 18, lineHeight: 1.4, weight: .semibold, monospaced: false)
    static let body = BppTextStyle(size: 15, lineHeight: 1.6, weight: .regular, monospaced: false)
    static let bodyMedium = BppTextStyle(size: 15, lineHeight: 1.6, weight: .medium, monospaced: false)
    static let bodyStrong = BppTextStyle(size: 15, lineHeight: 1.6, weight: .semibold, monospaced: false)
    static let small = BppTextStyle(size: 14, lineHeight: 1.5, weight: .medium, monospaced: false)
    static let caption = BppTextStyle(size: 14, lineHeight: 1.5, weight: .regular, monospaced: false)
    static let micro = BppTextStyle(size: 13, lineHeight: 1.2, weight: .medium, monospaced: false)
    static let code = BppTextStyle(size: 14, lineHeight: 1.5, weight: .regular, monospaced: true)
    static let codeSmall = BppTextStyle(size: 13, lineHeight: 1.5, weight: .regular, monospaced: true)
}
