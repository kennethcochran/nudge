# Nudge build-pipeline integration plan

**Status:** approved for implementation (2026-10-09)
**Goal:** every `dotnet build` that processes the repository's
`Directory.Build.rsp` automatically embellishes diagnostics with nudge coaching
guides, and models can pull a guide on demand. (Builds that skip response-file
processing — `/noautoresponse`, or Visual Studio's in-process IDE builds —
don't load the logger; the on-demand lookup covers those.)

## Background (decisions from 2026-10-09 design discussion)

- Agent harnesses — even IDE-integrated ones — build via the command line, not
  IDE build APIs. Intercepting the MSBuild pipeline covers ~99% of an agent's
  compiler interactions.
- The interception mechanism is an MSBuild `ILogger` auto-loaded through
  **`Directory.Build.rsp`** (MSBuild 15.6+; discovered by walking up from the
  project). Correction to an earlier claim: the autoresponse `MSBuild.rsp`
  must sit next to `MSBuild.exe` — it is *not* per-repo. `%MSBuildThisFileDirectory%`
  in the `.rsp` solves the logger-DLL path problem.
- Caveat: Visual Studio's in-process IDE builds ignore `.rsp` files entirely.
  External-process builds (Rider, `dotnet build`, CI) honor them.
- The IDE *live-diagnostics* loop (Roslyn → LSP `publishDiagnostics`) never
  touches MSBuild loggers and carries only code+message+range. Roslyn analyzers
  cannot see each other's diagnostics, so coaching cannot be injected there.
  Coverage for that loop is **retrieval, not interception**: a queryable nudge
  tool over the same rule catalog.
- Architecture: **two surfaces, one catalog** — the logger for build-time, a
  lookup tool for the live loop.

## Slice 1 — `nudge explain <ruleId>` (vertical)

New user-facing command: `nudge explain <ruleId> [--rules <dir>]` prints the
Why / Do this / AVOID guide for one rule, or the fallback guide plus the
"which ruleset provides this" hint when no guide exists.

Work:
- Extract catalog initialization into a reusable loader (the minimal seam this
  feature genuinely needs).
- `Reporter.RenderGuide(RuleGuide)` — single-guide rendering in the same shape
  as the report sections.
- `Program.RunExplainCommand` dispatch (alongside `ruleset`/`skill`), usage text.
- Tests: `RenderGuide` unit tests (sections present, fallback labeled honestly);
  existing suite stays green (golden behavior on the main path unchanged).

Acceptance: `nudge explain S1118` prints the guide from the installed ruleset;
`nudge explain NOPE9999` prints the fallback guide + ruleset hint; exit 0 both
ways (fallback is honest output, not a failure), exit 2 on usage errors.

## Slice 2 — ILogger interception (vertical)

`nudge init` becomes the on-ramp: it copies the logger DLL to `.nudge/` and
writes `Directory.Build.rsp` at the repo root:

```
/logger:NudgeLogger,%MSBuildThisFileDirectory%.nudge/Nudge.Logger.dll;baseline=.nudge/baseline.txt
```

Work:
- `Nudge.Logger` DLL implementing `Microsoft.Build.Framework.ILogger`:
  structured `ErrorRaised`/`WarningRaised` events (no text scraping), per-build
  buffering, dedupe of multi-target repeats, baseline filtering via the logger
  parameter.
- The pipeline-core extraction (`Parse` → `CoachingEngine.Generate`) happens
  **here**, driven by the logger as the second consumer — validated by two live
  consumers (text path + logger), not one plus a hope. Golden test: `--stdin`
  output byte-identical before/after.
- At build end: full report to `.nudge/report.md` + one-line console pointer.
  The logger never fails the build — coaching, not gating.
- Packaging: logger DLL ships inside the nupkg; `init` locates it in the tool
  store. `init` merges with an existing `Directory.Build.rsp` (never clobbers),
  is idempotent (re-run refreshes the DLL after upgrades), and supports
  `--remove`.
- Tests: logger against a fake event source (dedupe, baseline, report written);
  integration test — init a scratch repo, `dotnet build`, assert report + pointer.

Acceptance: fresh clone → `nudge init` → `dotnet build` → coaching report
appears with zero changes to how the user builds.

## Slice 3 — Skill, docs, lifecycle (vertical)

- `nudge-coaching` skill next to `nudge-ruleset-gen`, installed via
  `nudge skill install`: the loop is build → read `.nudge/report.md` (or
  `nudge explain <rule>`) → fix the root cause → rebuild.
- README: the init flow, what's installed, the trust note (an auto-loaded DLL
  is code execution on build — `init` being an explicit per-repo opt-in is
  load-bearing), disable via deleting the `.rsp` or `/noautoresponse`.

Acceptance: `nudge skill install` lists the skill; README documents the whole
flow; `nudge init --remove` undoes it.

## Dogfooding

After slice 2 ships: replace GameVM's `--stdin` CI step with the real thing
(`nudge init` in the GameVM repo) and run the agent loop against it.

## Open decisions (Kenneth's call)

1. Report as console-pointer + `.nudge/report.md` file (recommended — inline
   embellishment breaks tooling that parses build output).
2. Logger never fails the build (recommended — coaching, not gating).
3. Logger DLL committed under `.nudge/`, refreshed by re-running `init`.

## Non-goals

- VS IDE in-process builds (can't be reached via `.rsp`; out of scope).
- Rider-native (ReSharper) inspection IDs in the catalog — the catalog is keyed
  by analyzer rule ID; Roslyn-analyzer diagnostics in Rider are covered, native
  inspections are not.
- An MCP server wrapper around `explain` — natural follow-up once the CLI
  exists, not part of these slices.
