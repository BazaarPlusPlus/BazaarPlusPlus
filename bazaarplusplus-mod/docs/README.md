# BazaarPlusPlus Docs

The documentation inventory, and the mod-specific rules for changing it. `AGENTS.md`'s header table decides when to open each document; the repo-wide documentation policy is in `../AGENTS.md`.

## Inventory

- [MEMORY.md](MEMORY.md) — invariants, settled decisions, and traps that fail silently.
- [ARCHITECTURE.md](ARCHITECTURE.md) — how the plugin is assembled and what seams are shared, with per-feature detail under [architecture/](architecture/).
- [../CONTEXT.md](../CONTEXT.md) — the glossary.
- [adr/](adr/) — decision records.
- [contracts/](contracts/) — wire and payload formats that outlive any one implementation.
- [../README.md](../README.md) — the human entry point: features, install, quick start.

## Decision records

Every record in [adr/](adr/) is accepted; filenames carry the topic, and [MEMORY.md](MEMORY.md)'s "Architecture decisions" section is the one-line index. An ADR keeps the decision, its load-bearing rationale, guardrails, and current code evidence, and may be corrected or compressed when the code drifts. A record whose decision has become plain system description is retired: its residual value moves into ARCHITECTURE or MEMORY and the file is deleted. Numbers are never reused, so the sequence may have gaps; retired records live in git history.

## Document lifecycle

Each layer owns one thing, and links rather than restating: `AGENTS.md` owns process, `MEMORY.md` knowledge, `ARCHITECTURE.md` structure, `adr/` rationale, GitHub issues the work.

**Work goes to GitHub issues** ([issue tracker](../../docs/agents/issue-tracker.md)). Task plans, feature requests, bugs, acceptance checklists, implementation orders, and "pending confirmation" notes are issues. A document here describes how the system is, not what someone intends to do next.

**Knowledge lands in its destination directly; there is no drafts buffer.** A design record, root-cause analysis, or decision analysis produced mid-session distills straight into MEMORY, an ADR, or ARCHITECTURE in the same session; remaining work goes to a GitHub issue; nothing is parked in the tree awaiting a later sweep.

**Edit policy.** `ARCHITECTURE.md`, `architecture/`, `contracts/`, and `adr/` may be corrected the moment the code drifts. `MEMORY.md` and this file take dense one-line entries directly; the byte budgets below are what keep them curated. Point at the test that pins a number instead of restating the number; a count in prose drifts silently.

**Budgets are in bytes, not lines.** `AGENTS.md` and `MEMORY.md` carry dense one-line entries, so a line count says nothing about what they cost an agent. The enforced ceilings live in `tests/Architecture.Tests/DocsHygieneTests.cs`; keeping under them means merging entries, not appending. `MEMORY.md`'s Gotchas section is exempt: that section is the reason the file exists, and compressing it to hit a budget defeats the budget.

## Historical material

Retired documents live only in git history. The last complete `docs/archive/` tree is recoverable at commit `82412f0c` (`git show 82412f0c:docs/archive/<path>`). Recheck any historical claim against current code before acting on it.
