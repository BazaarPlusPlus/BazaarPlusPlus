# Domain Docs

How the engineering skills consume this repo's domain documentation. The layout is multi-context: a root glossary for cross-project terms and one per project.

## Before exploring, read these

- **`GLOSSARY.md`** at the repo root: cross-project terms (Product Release, Payload, Release Manifest, Bundle).
- **`<project>/GLOSSARY.md`** for each project the topic touches (`bazaarplusplus-mod`, `-installer`, `-server`, `-analyzer`, `-site`).
- **`docs/adr/`** for repo-wide decisions, and **`<project>/docs/adr/`** for project-scoped ones. Read those touching the area you are about to work in.

A missing file means nothing has been resolved there yet; proceed silently. `/domain-modeling` creates entries when a term or decision is actually settled.

## Use the glossary's vocabulary

Name domain concepts in issue titles, proposals, hypotheses, and test names with the terms the relevant `GLOSSARY.md` defines. A concept the glossary lacks is either invented language (reconsider) or a real gap (add a `GLOSSARY.md` entry in the same change).

## Flag ADR conflicts

When your output contradicts an ADR, say so explicitly rather than overriding it silently:

> _Contradicts ADR-0003 (per-platform release promotion), but worth reopening because…_
