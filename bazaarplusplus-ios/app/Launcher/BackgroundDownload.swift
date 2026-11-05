import BackgroundTasks
import UIKit

/// Keeps the App running while a download runs and the player is elsewhere. Only the App's process
/// downloads (SteamKit, in the service), so what it needs from iOS is running time:
/// - iOS 26 and later, a continued processing task, which the system shows with its own progress
///   while the App is not on screen. It must start while the App is on screen.
/// - any iOS, the seconds a background task grants, enough to answer a message and come back.
/// When the time runs out iOS suspends the App; the service carries on where it stopped when it
/// returns (docs/architecture.md). The launcher drives this from the snapshot, every half second.
@MainActor
final class BackgroundDownload {
    private var continued: AnyObject?
    private var grace = UIBackgroundTaskIdentifier.invalid
    private var observers: [NSObjectProtocol] = []
    private var running = false
    /// Whether iOS is keeping, or may keep, the download going off screen.
    private(set) var continuesInBackground = false

    init() {
        if #available(iOS 26.0, *) {
            continued = ContinuedTask()
            continuesInBackground = true
        }
        let center = NotificationCenter.default
        observers.append(center.addObserver(forName: UIApplication.didEnterBackgroundNotification, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.requestGrace() }
        })
        observers.append(center.addObserver(forName: UIApplication.willEnterForegroundNotification, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.endGrace() }
        })
    }

    func update(_ s: ServiceSnapshot, title: String, detail: String) {
        // The prepare after the download is part of the same wait.
        let now = s.busy && ["downloading", "verifying", "preparing"].contains(s.state)
        if #available(iOS 26.0, *), let task = continued as? ContinuedTask {
            if now {
                task.run(title: "正在下载 \(title)", detail: detail, fraction: s.state == "preparing" ? 1 : s.progress)
                continuesInBackground = task.available
            } else if running {
                task.finish(success: s.state == "ready" || s.state == "installed")
            }
        }
        running = now
        if !now { endGrace() }
    }

    private func requestGrace() {
        guard running, grace == .invalid else { return }
        grace = UIApplication.shared.beginBackgroundTask(withName: "download") { [weak self] in
            MainActor.assumeIsolated { self?.endGrace() }
        }
    }

    private func endGrace() {
        guard grace != .invalid else { return }
        UIApplication.shared.endBackgroundTask(grace)
        grace = .invalid
    }
}

@available(iOS 26.0, *)
@MainActor
private final class ContinuedTask {
    // One static handler (the Info.plist entry is its wildcard family): a second registration of an id kills the App.
    private static let identifier = (Bundle.main.bundleIdentifier ?? "") + ".download.game"
    private static var registered = false
    private static weak var current: ContinuedTask?

    private var task: BGContinuedProcessingTask?
    private var submitted = false
    private var lastDetail = ""
    private var nextAttempt = Date.distantPast
    private var title = ""
    private var detail = ""
    private var fraction = 0.0
    /// False once iOS has refused the task, until it accepts one.
    private(set) var available = true

    func run(title: String, detail: String, fraction: Double) {
        self.title = title; self.detail = detail; self.fraction = fraction
        if let task {
            report(to: task)
        } else if !submitted, Date() >= nextAttempt, UIApplication.shared.applicationState == .active {
            submit()
        }
    }

    func finish(success: Bool) {
        task?.setTaskCompleted(success: success)
        task = nil
        submitted = false
    }

    private func submit() {
        Self.current = self
        if !Self.registered {
            Self.registered = BGTaskScheduler.shared.register(forTaskWithIdentifier: Self.identifier, using: .main) { task in
                MainActor.assumeIsolated {
                    guard let task = task as? BGContinuedProcessingTask else { return }
                    guard let owner = ContinuedTask.current, owner.submitted else { task.setTaskCompleted(success: false); return }
                    owner.started(task)
                }
            }
        }
        // Refused only when the system cannot run it now (or at all, as in the Simulator): ask again later, on screen.
        nextAttempt = Date().addingTimeInterval(30)
        let request = BGContinuedProcessingTaskRequest(identifier: Self.identifier, title: title, subtitle: detail)
        request.strategy = .fail
        do {
            try BGTaskScheduler.shared.submit(request)
            submitted = true
            available = true
        } catch {
            available = false
            fputs("STEAM_APP_BACKGROUND_REFUSED \(error)\n", stderr)
        }
    }

    private func started(_ task: BGContinuedProcessingTask) {
        self.task = task
        task.progress.totalUnitCount = 10_000
        // Sent both for the system's own reasons and when the player stops it in the system's UI; iOS does
        // not say which. The download keeps its place either way, and 取消 in the App is the real stop.
        task.expirationHandler = { [weak self] in
            DispatchQueue.main.async {
                self?.task = nil
                self?.submitted = false
                task.setTaskCompleted(success: false)
            }
        }
        report(to: task)
    }

    private func report(to task: BGContinuedProcessingTask) {
        task.progress.completedUnitCount = Int64(min(1, max(0, fraction)) * 10_000)
        if detail != lastDetail {
            lastDetail = detail
            task.updateTitle(title, subtitle: detail)
        }
    }
}
