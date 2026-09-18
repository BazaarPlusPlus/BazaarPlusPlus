# Steam-Only Launch

## Context

The installer once supported the native Tempo Launcher through process capture, temporary payload removal, backup recovery, cancellation, and platform-specific relaunch logic. That path was large and fragile relative to its user segment, and Tempo support no longer matched product direction.

## Decision

Support Steam installations only. Detection resolves Steam copies, and launch opens the fixed Steam game entry. Keep no one-variant launch abstraction or dormant non-Steam detection path. Current behavior is specified in [Install And Reset](../install-reset.md).

## Rejected Alternatives

- Repair only the fragile parts of Tempo capture. The lifecycle itself was the maintenance burden.
- Keep a single-variant launch abstraction. It adds speculative state with no current branch.
- Detect Tempo but block launch. That preserves a half-supported installation path.

## Consequences

Native Tempo users are deliberately outside the supported product. Launch has no process-capture, backup, or cancellation surface, and downstream code may rely on a Steam installation layout.
