---
name: nudge-coaching
description: "Use when building or fixing diagnostics in a .NET repository that uses Nudge (build output ends with a 'nudge:' pointer line, a .nudge/report.md exists, or Directory.Build.rsp registers Nudge.Logger), and when working through compiler or analyzer warnings or errors (CS####, S####, CA####) in a .NET repo, including diagnostics pasted from an IDE or CI log."
---

# Nudge Coaching Loop

## Purpose
Nudge turns bare build diagnostics into coaching guides: why the rule exists,
how to fix the underlying problem, and the specific cheats to avoid. This
skill is the working loop for consuming those guides while you fix a build.
The guides are the point — a diagnostic fixed by gaming its metric is a
failure, even when the build goes green.

## The loop

**1. Build the way the repo builds.** Plain `dotnet build` (the repo's usual
solution/project and configuration). Do not build through nudge wrappers when
the repo has the logger installed — `nudge init` has already arranged for
every build to produce a coaching report.

**2. Read the report before editing anything.** If the build output ends with
a pointer like `nudge: 3 finding(s) across 2 rule(s) — see <path>`, read that
file (default `.nudge/report.md` at the repo root) in full. It groups findings
by rule, with the guide for each: **Why**, **Do this**, **AVOID**, and the
flagged locations.

**3. Look up anything the report doesn't cover.** A diagnostic you met
outside the report — an IDE squiggle, a CI log line, a rule the user names —
has the same guide on demand:

```bash
nudge explain <RULEID>    # e.g. nudge explain S1118, nudge explain CS0219
```

**4. Fix the root cause, per guide.** Work rule by rule, following the
guide's *Do this* in the flagged code itself. The *AVOID* section names the
metric-gaming dodges for that specific rule — treat it as a list of forbidden
moves, not background reading.

**5. Rebuild and re-read the report.** Repeat until the pointer says clean.
Two cautions about trusting a clean result:

- An incremental build only re-emits diagnostics for projects it actually
  recompiled. A fix you just made recompiles its project, so its diagnostics
  genuinely clear — but findings in projects that didn't rebuild aren't
  re-checked. For a final verification, force a rebuild of the projects you
  touched (`dotnet build --no-incremental` on the affected project).
- When a build *fails* with errors, analyzer findings may be incomplete.
  Fix compile errors first, then take the next report as the real list.

## When there is no report

- **No pointer in the build output and no fresh report:** the logger isn't
  running for this build. Check whether it's actually registered — the
  `Directory.Build.rsp` at the repo root contains a `/logger:` line for
  `Nudge.Logger` and `.nudge/Nudge.Logger.dll` exists. (A `.nudge/`
  directory by itself proves nothing: `nudge init --remove` unregisters the
  logger but deliberately leaves `baseline.txt` behind.) If it isn't
  registered, the repo hasn't opted in (or has opted back out). Propose
  `nudge init` to the user rather than running it unprompted — it installs an
  auto-loaded logger DLL, and that opt-in is the user's call. Until then,
  `dotnet build ... | nudge --stdin` gives the same coaching for one build.
- **A finding used the generic fallback guide:** the report says so, and may
  name a ruleset to install (`nudge ruleset add <name>`). Surface that to the
  user; with only the fallback guide, research the rule's actual purpose
  (the analyzer's documentation for that rule ID) before fixing.
- **Visual Studio in-process builds** ignore `Directory.Build.rsp` by design,
  so no report appears. Build from the command line, or use `nudge explain`.

## Operating rules

1. **Never suppress a diagnostic to clear it.** No `#pragma warning disable`,
   no `[SuppressMessage]`, no `NoWarn`, and no `.editorconfig` severity
   downgrades — unless the repo's own instructions explicitly allow it for
   that case. Suppression is the failure mode nudge exists to prevent.
2. **Never game the metric.** If a change would make the diagnostic disappear
   while the flagged problem survives (renames, wrapper helpers that hide the
   construct, splitting code purely to shrink a complexity number), it is the
   dodge the guide's AVOID section describes. Don't do it.
3. **The baseline is the user's snooze button, not yours.** Findings listed
   in `.nudge/baseline.txt` are deliberately deferred. Don't add findings to
   the baseline to make a report come back clean; propose it and let the
   user decide.
4. **The logger never failing the build doesn't make findings optional.**
   Nudge coaches instead of gating, but the findings in the report are the
   work. Report what remains unfixed, with rule IDs, rather than quietly
   moving on.
5. **Summarize by rule.** When you report back, group what you fixed by rule
   ID and say what changed in the code — not just "warnings fixed."
