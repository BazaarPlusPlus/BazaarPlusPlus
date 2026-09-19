# macOS Launch Trampoline

## Context

Steam must launch The Bazaar's application bundle directly, while BepInEx needs Doorstop environment and JIT entitlements before Unity starts. Steam LaunchOptions are user-editable, account-scoped strings whose provenance cannot be classified safely.

## Decision

Use one in-bundle Mach-O trampoline on every supported macOS version. Preserve the Unity executable as `.orig`, install the bundled stub as the bundle executable, sign and verify the resulting bundle, and require the installed stub to have the same Mach-O build UUID as the current bundled stub. Code signing may change the file bytes without changing that build identity.

Require empty The Bazaar LaunchOptions across every Steam account. Any non-empty direct value is dirty; unreadable configuration blocks mutation. Repair converges residue, payload, trampoline, and LaunchOptions to one final state. Current mechanics are specified in [Install And Reset](../install-reset.md).

## Rejected Alternatives

- Bootstrap through Steam LaunchOptions. Correctness would depend on Steam executing a mutable external command prefix.
- Support multiple bootstrap modes. That multiplies state, recovery, UI, and payload branches without a second supported product behavior.
- Preserve selected LaunchOptions fragments. Arbitrary user content has no reliable ownership boundary.
- Accept any structurally valid trampoline. Repair must converge to the exact stub shipped by the installer.
- Mutate when Steam configuration cannot be inspected. A partial install would make readiness unverifiable.

## Consequences

The UI exposes no compatibility mode. Steam Verify, game updates, manual LaunchOptions edits, and stub updates make readiness false and route the existing primary action to Repair.
