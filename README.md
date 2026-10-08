# Habit Shim

`nudge` turns raw Roslyn / SonarQube build diagnostics into **coaching guides
for AI coding agents** — the Habit Hooks pattern, native to .NET.

## The problem it solves

A bare linter diagnostic is a *target*, and Goodhart's law applies: an agent told
only `function is too complex (11 > 5)` will game the metric (split branches into
helpers so the number drops while the code gets no simpler) instead of fixing the
code.

The shim closes the loop from outside: the diagnostic is the cue, the guide is the
action. Each finding is replaced with:

- **Why** — why the rule exists and what it costs to ignore it,
- **Do this** — concrete remediation steps,
- **AVOID** — the metric-gaming anti-patterns, named explicitly.

## Measured efficacy

A three-way experiment (352 SonarSource analyzer rules × 3 arms = 1,056 blind-judged
fix trials, 2026-10-04) measured the effect:

| Arm | Genuine fixes | Metric gaming |
|---|---|---:|
| Bare diagnostic | 88.9% | 7.4% |
| + official rule description | 92.3% | 4.5% |
| + generated coaching guide | 96.9% | 0.6% |

The active ingredient is explicitly naming the dodge. The 7% gaming rate on bare
diagnostics compounds: every quietly-wrong fix is tech debt invisible to downstream
checks. See `src/Nudge/EXPERIMENT.md` for the full protocol and results.

## Installation

```bash
dotnet tool install -g Nudge
```

Then:

```bash
nudge --sln YourSolution.sln
```

Requires the .NET 10 runtime (the tool is framework-dependent, keeping the
package small).

## Rulesets

Coaching guides ship as **rulesets** — versioned packs of guides for a specific
analyzer family — not bundled with the tool. Install what you use:

```bash
nudge ruleset add sonar      # SonarSource analyzers (SonarAnalyzer.CSharp)
nudge ruleset list            # show installed rulesets
nudge ruleset remove sonar   # remove one
nudge ruleset detect          # scan csproj files for analyzer packages and report missing rulesets
```

`nudge ruleset add` accepts a ruleset name (downloaded from NuGet), a local
directory containing `ruleset.json`, or a `.nupkg` file path.

**Automatic detection.** When you run `nudge`, it scans your `.csproj` files for
known analyzer packages. If findings fall back to generic guides and a ruleset
exists for one of your analyzers, it tells you exactly what to install:

```
Some findings used generic guides because their rulesets aren't installed:
  SonarAnalyzer.CSharp → nudge ruleset add sonar
```

**Resolution order** per rule: `--rules` dir → project-local `rules/` →
installed rulesets (`~/.nudge/rulesets/`) → your own analyzer metadata
(`KnownRules` in `Rules.cs`) → generic fallback. Your hand-written guides always
win.

## Usage

```bash
# Build the solution and report enriched diagnostics (exit 1 on findings, 0 when clean)
nudge --sln YourSolution.sln

# Or enrich any build's output (composable with CI, hooks, other agents)
dotnet build YourSolution.sln | nudge --stdin

# Pass extra build args through
nudge --sln YourSolution.sln --build-args "-c Release"

# Snooze the existing backlog so only NEW findings surface (habit-hooks ratchet)
nudge --sln YourSolution.sln --write-baseline .nudge/baseline.txt
nudge --sln YourSolution.sln --baseline .nudge/baseline.txt
```

Exit codes follow the habit-hooks convention: `0` = clean, `1` = findings to fix,
`2` = tool failure (bad arguments, build could not run, or the build itself failed
with errors — analyzer results may be incomplete).

## Rule guides

Guides live in `src/Nudge/rules/<RULEID>.md`:

```markdown
# S1234 — Short Title

## Why
2–4 sentences: why the rule exists, what it costs to ignore.

## Do this
1. Concrete step.
2. Concrete step.

## AVOID
- The metric-gaming move, named explicitly.
```

Resolution order per rule: hand-written guide → metadata from your own analyzer
`DiagnosticDescriptor`s (see `KnownRules` in `Rules.cs`) → a generic fallback that
still carries the anti-gaming instruction. **The hand-written guide always wins** —
that is where project-specific taste lives. If a rule fires often and has no guide,
write one; the fallback says so.

The shipped `rules/` directory contains 510+ coaching guides for the SonarSource
analyzer corpus, generated and semantically validated per the experiment protocol.
They are distributed as the `Nudge.Rules.Sonar` ruleset (see `rulesets/`), not
bundled with the tool.

## Extending it for your own use

**Covering your own analyzers.** Two options, in priority order:

1. Drop a markdown guide in `src/Nudge/rules/<RULEID>.md`. This is the
   preferred path — full control over the Why, the steps, and the named dodges.
2. Add the rule's title and description to `KnownRules.Descriptors` in
   `src/Nudge/Rules.cs`. The shim wraps it in the generic coaching template.
   Less control, but zero new files for rules that rarely fire.

**Writing a good guide.** Don't just explain the rule better — name the dodge.
The experiment showed that describing the rule gets about half the benefit;
explicitly listing the metric-gaming moves ("don't pack methods onto lines to
dodge the line count", "don't move the throw elsewhere") is what cuts gaming
from 7% to under 1%. Write the *Why* from the rule's documentation and the
*AVOID* section from the gaming moves you've actually seen agents try.

**Adopting on an existing codebase.** Run the shim with `--write-baseline` to
snooze the current backlog, then fix forward — only new findings surface. Write
guides for the rules that fire most often with the generic fallback first; those
are your highest-leverage guides.

**Validating a new guide.** The methodology from the experiment is reusable:
generate the guide from the rule's documentation, have someone who knows the rule
check it for semantic accuracy (generated guides can legitimize non-fixes — see
the S2696 case in EXPERIMENT.md), then measure fix quality with and without it
on a handful of seeded violations.

## Design notes

- **Zero NuGet dependencies** — only the .NET SDK. The shim must never be the
  thing that breaks a restore.
- **Deterministic core, nondeterministic reader**: parsing, grouping, baselines
  and exit codes are plain code. Judgment (what the fix should be) stays with
  the agent — but now informed by the rationale, delivered at the moment of
  violation.
- The `AVOID` section is the load-bearing part. Naming the evasion explicitly
  is what turns a gate into coaching.

## License

Public domain — see LICENSE (Unlicense).
