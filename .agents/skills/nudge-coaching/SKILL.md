---
name: nudge-coaching
description: "Use when building or fixing diagnostics in a .NET repository that uses Nudge (Directory.Build.rsp registers Nudge.Logger, or build output contains 'nudge: coaching for' lines), and when working through compiler or analyzer warnings or errors (CS####, S####, CA####) in a .NET repo, including diagnostics pasted from an IDE or CI log."
---

# Nudge Coaching

A diagnostic fixed by gaming its metric is a failure, even when the build
goes green. In a repo where `nudge init` has run, the build prints the
coaching inline, next to each diagnostic it applies to — Why / Do this /
AVOID from the rule's guide, in the build output itself. Work from the
coaching the build already gave you.

## The loop

1. **Build with plain `dotnet build`.** The registered logger arranges
   the coaching; don't route the build through nudge itself.
2. **Fix the root cause the coaching names,** in the flagged code. The
   AVOID section lists forbidden moves for that specific rule, not
   background reading. A rule's full guide prints with its first
   diagnostic in the build; later diagnostics for the same rule get a
   one-line reminder.
3. **Look up diagnostics that arrived without coaching** — an IDE
   squiggle, a CI log line, a rule the user names, or a build diagnostic
   that printed bare (rules with no recorded guide print nothing extra):
   `nudge explain <RULEID>`.
4. **Rebuild.** Clean only counts when it comes from a forced rebuild of
   the projects you touched (`dotnet build --no-incremental`) — an
   incremental build never re-checks projects it didn't recompile. When a
   build fails with errors, analyzer findings are incomplete: fix the
   errors first and treat the next build's coaching as the real list.

## When there is no coaching in the output

- **The logger isn't registered.** Check the registration, not the
  folder: the repo root's `Directory.Build.rsp` holds a `/logger:` line
  for `Nudge.Logger` and `.nudge/Nudge.Logger.dll` exists. (`.nudge/`
  alone proves nothing — `nudge init --remove` unregisters the logger but
  keeps `baseline.txt`.) Unregistered means the repo hasn't opted in, or
  has opted out: propose `nudge init` and let the user decide; an
  auto-loaded DLL is their call. Until then,
  `dotnet build ... | nudge --stdin` gives the same coaching for one
  build.
- **Visual Studio in-process builds** ignore `Directory.Build.rsp` by
  design. Build from the command line, or use `nudge explain`.
- **A stale `.nudge/report.md`** may exist from an older nudge version.
  The logger no longer writes it; ignore it.

## Operating rules

1. **Fix, never silence.** No `#pragma warning disable`, no
   `[SuppressMessage]`, no `NoWarn`, no `.editorconfig` severity
   downgrades, and no metric-gaming edits (renames, wrapper helpers,
   splitting code purely to shrink a number) — unless the repo's own
   instructions explicitly allow it for that case. That is the failure
   mode nudge exists to prevent.
2. **The baseline is the user's snooze button.** Baselined findings print
   no coaching — they are deliberately deferred. Propose additions to
   `.nudge/baseline.txt` and let the user decide; never add findings just
   to make a build come back quiet.
3. **Report by rule.** Summarize what you fixed grouped by rule ID, say
   what changed in the code, and name anything left unfixed, with its
   rule ID. The logger never failing the build doesn't make findings
   optional.
