import SwiftUI
import UIKit

/// Objective-C entry point for `app/Host/main.mm`: while the game runs, folding an iPhone Duo
/// pauses it and covers it with a prompt to unfold; unfolding removes the prompt and resumes.
/// Only a closed hinge counts as folded. The prompt has its own window above the engine's, which
/// replaces its root view controller after start-up. Unity resumes itself on becoming active and
/// can drop a pause sent before it finishes starting, so the pause is sent again while folded.
@objc(BPPFoldGuard)
public final class FoldGuard: NSObject {
    private let scene: UIWindowScene
    private let pause: (Bool) -> Void
    private var prompt: UIWindow?
    private var repeatPause: Timer?

    @MainActor
    @objc public init(scene: UIWindowScene, hingeView: UIView, pause: @escaping (Bool) -> Void) {
        self.scene = scene
        self.pause = pause
        super.init()
        if #available(iOS 27.1, *) {
            // The handler also runs with the current state, so starting the game folded prompts at once.
            hingeView.addInteraction(UIHingeInteraction { [weak self] _, update in
                self?.setFolded(update.hinge?.status == .closed)
            })
        }
    }

    @MainActor private func setFolded(_ folded: Bool) {
        guard folded != (prompt != nil) else { return }
        fputs("STEAM_APP_HINGE folded=\(folded ? 1 : 0)\n", stderr)
        repeatPause?.invalidate()
        repeatPause = nil
        if folded {
            let window = UIWindow(windowScene: scene)
            window.windowLevel = .alert
            let screen = UIHostingController(rootView: UnfoldPrompt())
            screen.view.backgroundColor = UIColor(BppColor.bgCanvas)
            window.rootViewController = screen
            window.isHidden = false
            prompt = window
            pause(true)
            repeatPause = Timer.scheduledTimer(timeInterval: 0.5, target: self, selector: #selector(pauseAgain), userInfo: nil, repeats: true)
        } else {
            prompt?.isHidden = true
            prompt = nil
            pause(false)
        }
    }

    @MainActor @objc private func pauseAgain() { pause(true) }
}

private struct UnfoldPrompt: View {
    var body: some View {
        VStack(spacing: BppSpace.space16) {
            BppMark(size: BppSize.touchTarget)
            Text("展开手机后继续游戏").bppFont(BppType.title).foregroundColor(BppColor.text1)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(BppColor.bgCanvas)
        .ignoresSafeArea()
    }
}
