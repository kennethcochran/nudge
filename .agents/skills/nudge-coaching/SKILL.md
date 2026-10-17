---
name: nudge-coaching
description: "Use when building or fixing diagnostics in a .NET repository that uses Nudge (build output ends with a 'nudge:' pointer line, a .nudge/report.md exists, or Directory.Build.rsp registers Nudge.Logger), and when working through compiler or analyzer warnings or errors (CS####, S####, CA####) in a .NET repo, including diagnostics pasted from an IDE or CI log."
---

# Nudge Coaching Loop

A diagnostic fixed by gaming its metric is a failure, even when the build
goes green. The guides are the point of this loop.

## The loop

1. **Build with plain `dotnet build`.** The logger `nudge init` installed
   already arranges the coaching report; don't route the build through
   nudge itself.
2. **Read the report before editing.** When the build output ends with
   `nudge: N finding(s) across M rule(s) — see <path>`, read that file
   (default `.nudge/report.md`) in full: findings grouped by rule, each
   with Why / Do this / AVOID and the flagged locations.
3. **Look up diagnostics the report doesn't cover** — an IDE squiggle, a
   CI log line, a rule the user names: `nudge explain <RULEID>`.
4. **Fix the root cause the guide names.** The AVOID section lists
   forbidden moves for that specific rule, not background reading.
5. **Rebuild and re-read.** A clean result only counts when it comes from
   a forced rebuild of the projects you touched
   (`dotnet build --no-incremental`) — an incremental build never
   re-checks projects it didn't recompile. When a build fails with
   errors, analyzer findings are incomplete: fix the errors first and
   treat the next report as the real list.

## When there is no report

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
- **A finding used the generic fallback guide** (the report says so):
  research the rule's actual purpose in the analyzer's documentation
  before fixing, and surface any ruleset the report names
  (`nudge ruleset add <name>`) to the user.

## Operating rules

1. **Fix, never silence.** No `#pragma warning disable`, no
   `[SuppressMessage]`, no `NoWarn`, no `.editorconfig` severity
   downgrades, and no metric-gaming edits (renames, wrapper helpers,
   splitting code purely to shrink a number) — unless the repo's own
   instructions explicitly allow it for that case. That is the failure
   mode nudge exists to prevent.
2. **The baseline is the user's snooze button.** Findings in
   `.nudge/baseline.txt` are deliberately deferred. Propose additions
   and let the user decide; never add findings just to make a report
   come back clean.
3. **Report by rule.** Summarize what you fixed grouped by rule ID, say
   what changed in the code, and name anything left unfixed, with its
   rule ID. The logger never failing the build doesn't make findings
   optional.
