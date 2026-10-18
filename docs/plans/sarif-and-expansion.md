# Nudge SARIF support and expansion plan

**Status:** decided (Kenneth, 2026-10-17). Slice sequence approved as the
working roadmap; each slice still ships as its own PR under the standing
workflow (feature branch, CI green before review, Kenneth approves/merges).
**Predecessor:** `docs/plans/msbuild-logger-integration.md` (slices 1–3:
`nudge explain`, Core extraction + ILogger, coaching skill + inline
coaching). PR #11 (slice 3) is complete and awaiting merge; this plan's
branch was cut from `main` before that merge and picks it up by merging
`main` once #11 lands.

## Goal

Nudge's unit of value is not the compiler diagnostic. It is the terse,
consequential message at a moment of agent decision — a message with a
known cheap dodge and a known honest fix, where a Why / Do this / AVOID
guide changes what the agent does next. Compiler errors were the tip of
the iceberg. This plan expands nudge's reach — complete compiler streams
(including Info severity), MSBuild task diagnostics, and standalone
analysis tools — without painting the architecture into a corner.

## Established facts (verified 2026-10-17, probes and primary sources)

- **The agent stream law.** An agent's default behavior is to run a tool
  and either read its terminal output all at once when it exits, or
  monitor that output over time. An agent does not go looking for log
  files, report files, or SARIF files unless instructed. Coaching must
  therefore be inside the terminal stream of the single command the
  agent ran. (This is what retired `report.md` + pointer in favour of
  inline coaching.)
- **The build has two complementary channels; neither is complete.**
  - The MSBuild *event stream* (what `NudgeLogger` subscribes to)
    carries every warning/error — compiler, analyzer, **and task**
    diagnostics (MSB####, NU####, NETSDK####, PKV####) — but the Csc
    task promotes only Warning/Error diagnostics into events.
    **Info-severity diagnostics never enter the event stream at all**
    (probe: CA1822 demoted to Info appeared zero times across error,
    warning, and all 1,831 message events of a build; a warning-severity
    control arrived normally). This is how GameVM PR #9's NUnit
    Info findings were invisible locally yet harvested by SonarScanner
    as `external_roslyn` issues.
  - The compiler's **ErrorLog SARIF** carries the complete compiler +
    analyzer stream at all severities (probe: the same fixture's
    `diag.sarif` held CA1822 at level `note` and CA1825 at `warning`)
    — but no task diagnostics, which are never compiler diagnostics.
  - The union of the two channels is the complete build. In-build, the
    union is the logger reading ErrorLog files at `BuildFinished`;
    wrapper-side, it is the `.binlog` (the recorded event stream) plus
    the ErrorLog files.
- **SARIF is the cross-tool interchange format** (OASIS standard).
  Covered natively: InspectCode (default output since 2024.1), CodeQL,
  BinSkim, Semgrep, DevSkim; PVS-Studio via its official converter;
  the whole Roslyn analyzer ecosystem via ErrorLog. **Exceptions:**
  `dotnet format` (its own JSON report schema; third-party converters
  exist), NDepend (imports SARIF; its own findings export as HTML/XML),
  SonarScanner (findings go to the server; SARIF is an *import* format
  for SonarQube, not a scanner output), legacy standalone FxCop (XML).
  Task diagnostics are the standing exception that no SARIF source
  covers (see above).
- **Per-project ErrorLog arrangement is a proven pattern:**
  `Directory.Build.props` with
  `<ErrorLog>$(SolutionDir)\.sarif\$(MSBuildProjectName).json</ErrorLog>`
  yields one SARIF file per project (NDepend's documented integration
  pattern). Nudge's location will live under `.nudge/`; exact path is a
  slice-2 decision.
- **InspectCode has no supported stream-injection surface** (verified
  against JetBrains documentation, correcting an earlier unverified
  claim): MSBuild is used internally only for project evaluation and
  reference resolution (a repo's `Directory.Build.rsp` logger
  registration is not consumed), and ReSharper extensions load
  in-process but contribute inspections to the report file — there is
  no documented output-shaping API. Standalone tools therefore get a
  launcher, not an inhabitant.
- **Delivery follows the tool's relationship to the compiler
  platform.** Tools inside Roslyn's diagnostic substrate (one rule-ID
  namespace, one severity system) can be inhabited (logger) and keyed
  by raw code. Tools outside it (InspectCode and friends) get the
  habit-hooks shape: a runner plus an interchange file, with the
  coaching layer doing the unification.

## Architecture invariants (the anti-corner seams)

1. **The engine stays source-agnostic.** `CoachingEngine` consumes
   finding records and resolves guides by message code. NU1605 is just
   another key; an InspectCode inspection is just another key. Nothing
   in the core knows whether a finding was born in a compilation, a
   restore, or a standalone tool.
2. **No channel's filter defines visibility.** Ingest from the most
   complete source per family; nudge's own policy (guide exists, not
   baselined) decides what is coached. Visibility is data; coaching is
   policy.
3. **Adapters are plural and replaceable.** Events, binlog, ErrorLog
   SARIF, native tool SARIF, and text as the adapter of last resort —
   each at the edge, none load-bearing for the others. A new message
   family costs one adapter plus catalog entries, never engine surgery.
4. **Guides are provenance-free.** Plain markdown keyed by code,
   resolving identically whether authored from an analyzer descriptor
   (`ruleset-gen`), from documentation (task diagnostics), or by hand
   (project-local `rules/` already proves the format needs no
   descriptor).
5. **Rendering separates from transport.** The same coached output
   must be emittable inline by a logger, at build end, or printed by a
   wrapper, because different families force different transports and
   the stream law is the only constant.
6. **Baseline semantics are source-independent**, keyed on the finding,
   not on the channel that delivered it. The current baseline key is
   `RULEID|path/relative/to/repo-root` — or `RULEID|(no file)` for
   unlocated findings — with line number and message deliberately
   excluded, so code motion within a file does not resurrect a snoozed
   finding. (Habit Hooks' content-keyed snooze — editing a snoozed file
   re-arms its findings — is recorded as a candidate improvement to
   that key; not scheduled.)

## Slice sequence

Each slice: one demonstrable behavior end-to-end, an executable
acceptance test, CI as the test arbiter (local `dotnet test` is
unreliable on the dev VM — vstest testhost fails to connect; state the
executor plainly in PR bodies), and an end-to-end check in a repo path
containing spaces.

### Slice 1 — SARIF ingestion + `nudge coach`

- A SARIF reader in `Nudge.Core` mapping SARIF runs/results to the
  engine's finding records (rule ID, level → severity, locations,
  message), including the `note`/`none` levels no other channel
  carries.
- `nudge coach <file.sarif>`: renders the grouped coached report to
  **stdout** (never a file the agent must go find), honours the
  baseline, keeps the established exit-code contract (0 clean /
  1 findings / 2 failure). Uncatalogued rules surface as uncoached
  findings, never dropped silently.
- Acceptance: coach a real ErrorLog SARIF produced by a fixture build
  containing an Info-severity finding; its coaching appears in the
  output alongside warning/error findings.

### Slice 2 — ErrorLog arrangement + logger SARIF finish

- `nudge init` additionally arranges per-project ErrorLog SARIF
  (via `Directory.Build.props`, following the proven
  `$(MSBuildProjectName)` pattern, output under `.nudge/`); `init
  --remove` undoes it; re-runs are idempotent; a user's own pre-existing
  ErrorLog setting is detected and respected, not overwritten.
- `NudgeLogger` ingests the SARIF files at `BuildFinished` and prints
  the coaching for findings the event stream could not carry into the
  same build output. Dedup partition: events own warning/error, the
  SARIF pass owns note/none (disjoint by construction — events provably
  never carry Info).
- Acceptance: in an init'd fixture repo whose only finding is an
  Info-severity diagnostic, a plain `dotnet build` prints that rule's
  coaching; a multi-project solution produces and consumes per-project
  SARIF files.

### Slice 3 — Build-errors ruleset

- A shipped ruleset of documentation-sourced guides for the
  dodge-famous task diagnostics: NU1605, NU1101, NU1107, NU190x,
  NETSDK1045, NETSDK1004, MSB3644 (initial subset; grow by evidence,
  not by error-index coverage).
- Authored in the standard guide format; no analyzer assembly is
  involved. `ruleset-gen` is not the authoring path for this family —
  the guides are written from Microsoft Learn error documentation
  plus the known dodge per code.
- Acceptance: a fixture build failing with NU1605 receives inline
  coaching naming the honest fix and the `NoWarn` dodge under AVOID.

### Slice 4 — InspectCode adapter (first standalone tool)

- A launcher (working name `nudge inspect`) that runs InspectCode
  with its report directed to a scratch SARIF, streams the tool's own
  stdout through live (the monitoring pattern), appends the coached
  findings to the same stream at exit, and propagates the tool's exit
  code.
- Requires the InspectCode catalog work (ruleset authoring for
  ReSharper inspection IDs) to be useful beyond the uncoached bucket;
  sequence the catalog subset by observed findings, habit-hooks style.
- Acceptance: a fixture solution with a known ReSharper inspection
  produces coached output in the launcher's terminal stream.

### Explicitly unscheduled

- `dotnet format` JSON adapter; binlog reader for wrapper-side
  post-hoc builds; SonarQube-side coaching (server findings are a
  separate surface — PR comments — not a terminal stream).
- A tool-independent "smell key" layer (habit-hooks' abstraction).
  Not needed while catalog keys are platform-unified per family;
  revisit only if cross-tool guide duplication becomes real.
- Test-runner and other CLI message families: plausible future
  families under the same seams; not chartered by this plan.
- Multi-targeted (netstandard2.0) logger for .NET Framework MSBuild /
  older SDK hosts: remains its own deferred slice from the logger plan.

## Open decisions (deliberately not settled by this plan)

- **Delivery model per family** — uniform wrapper vs platform
  exploitation — remains open and is now decidable per family without
  foreclosing the others, provided the invariants above hold. Builds
  currently favour in-build delivery (slices 1–2 do not depend on the
  wrapper question); standalone tools force the launcher shape.
- **`--stdin` / text-parser retirement.** Gated on GameVM's CI
  swapping its `nudge --stdin` step for `nudge init` (the pending
  dogfooding step). Until then the parser stays as the adapter of last
  resort; it gains no new investment.
- **Whether `nudge init` should write `Directory.Build.props`** at
  all (slice 2 widens init's footprint beyond the `.rsp`); slice 2's
  PR must address detection/respect of existing props explicitly.
