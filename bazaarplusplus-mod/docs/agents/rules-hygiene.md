# Rules Hygiene

How a rule gets into `AGENTS.md`. Every agent session loads that file in full, so each line has to earn its place on every turn.

## Admission criteria

A new rule must meet all three:

1. **Non-obvious** — someone familiar with the codebase would still get it wrong without the rule.
2. **Repeatedly encountered** — it came up more than once. Multiple hits inside one session counts.
3. **Actionable** — a concrete instruction, not a principle.

Editing or clarifying an existing rule needs no ceremony; it is always welcome.

## Where a rule belongs

`AGENTS.md` holds rules that apply repo-wide. A rule scoped to one module or feature area belongs in that area's own rules file.

Rules are traps to avoid, not maps to follow. Anything that describes what a module *is* belongs in `docs/ARCHITECTURE.md`; anything that explains *why* a boundary was drawn belongs in `docs/adr/`.

## How a rule lands

Rules come from validated patterns, so they arrive through review rather than mid-task:

1. An agent notes a pattern during a session and proposes it under **"Suggested rule additions"** in the wrap-up summary or commit message.
2. The team validates the pattern in code review.
3. A dedicated commit adds the rule, with context on why it exists.

During ordinary feature or fix work, leave the rules file alone and let step 1 carry the proposal.
