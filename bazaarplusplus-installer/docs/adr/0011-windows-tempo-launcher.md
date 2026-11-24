# Windows Tempo Launcher

Tempo Launcher 1.0.19 starts the Windows Mono client through its own Play action. The official launcher manages authentication and restores BepInEx after replacing the game build. This makes a direct handoff to the official launcher sufficient to reopen the Windows part of [ADR-0003](0003-steam-only-launch.md).

Install the Windows Payload in the selected game build. Keep the Data Root beside a Tempo build so that a launcher redownload preserves history and staged updates. Detect missing Windows native recorder files during Repair because the launcher only preserves the bootstrap and BepInEx. macOS retains the Steam launch entry and trampoline described in [ADR-0002](0002-macos-launch-trampoline.md).
