# ADR-0009: Tests assert behavior or compiled artifacts, never source text

Status: Accepted

## Context

Architecture and capsule tests read `.cs`, scripts, and headers as strings and asserted with `Contains`. Those checks broke on formatting, missed target-typed `new(...)`, and could not tell a real call from a comment. The replacement has to stay buildable under locked restore and `TreatWarningsAsErrors`.

## Decision

1. Prefer a behavior test. When the fact is structural, read the assemblies produced by the same build (metadata / IL / PDB). MSBuild and JSON are parsed as documents. Scripts and targets are executed. Native sources are checked with clang on a toolchain lane. S2 adds a Debug+Release IL scan so `#else` bodies in a Release build stay visible. Roslyn is not a test dependency.
2. Literal rules apply only to runtime data (SQL, header tokens, path segments) and match whole words. A rule's scope and its forbidden target must both resolve.
3. RS0030 (`tests/BannedSymbols.txt`) forbids test code from reading a file as text. The only exemptions are `{src,build}/**` Compile-Includes, `tests/Shared/TestFiles/**`, and `tests/CombatImpact.Corpus/**`. The debt list in `tests/.editorconfig` only shrinks. `File.OpenRead` plus `StreamReader(Stream)` stays open for loopback and zip; review those.

Accepted gaps: IL does not show which parameter slot received a value; a field's declaring partial file is not a runtime fact (methods that touch it still map through the PDB); Windows native compiler flags and hosts without a native toolchain stay platform-gated.

## Guardrails

- Do not add `Microsoft.CodeAnalysis.CSharp` to satisfy an architecture rule.
- Do not grow the RS0030 debt list. A new file-to-text read fails the build.
- Enumerate architecture sweeps from `src/` and `tests/`, never the repo root.

## Evidence

- Ban and `TestInputs`: `tests/BannedSymbols.txt`, `tests/Shared/TestFiles/TestInputs.cs`, `tests/Architecture.Tests/SourceTextBanTests.cs`
- Debt list: `tests/.editorconfig`
