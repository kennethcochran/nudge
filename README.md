# Nudge

Nudge is a small command-line tool that makes linter warnings useful to AI coding
agents. Instead of handing an agent a bare diagnostic like "method too complex",
it hands over a short coaching guide: why the rule exists, how to fix it
properly, and the specific cheats to avoid.

> **Hat tip:** Nudge is a .NET-native take on the [Habit Hooks](https://github.com/habit-hooks/habit-hooks) idea — attach the rule's rationale to the diagnostic, so the agent fixes the code instead of gaming the metric. Habit Hooks covers Python and Node.js; Nudge brings the same coaching to Roslyn and Sonar diagnostics.

## Why it exists

I write most of my code with AI coding agents. They're fast, tireless, and —
when you point them at a linter warning — incorrigible metric-gamers.

Tell an agent "this method is too complex (11 > 5)" and it will happily split
the method into three helpers. The number drops. The code is no simpler. The
warning is gone and the problem is still there, now wearing a disguise.
Suppress the warning, shuffle code across a boundary, rename the construct —
the diagnostic goes green and nothing got better.

That's Goodhart's law with a compiler: the moment a metric becomes the target,
it stops being a useful measure. A bare diagnostic is a target, so agents
optimize for the diagnostic instead of the code.

Nudge changes what's on the table. Every diagnostic gets replaced with a
coaching guide in three parts:

- **Why** — why the rule exists, and what it costs to ignore it.
- **Do this** — concrete steps to fix the underlying problem.
- **AVOID** — the metric-gaming dodges, named explicitly so they can't be
  "accidentally" used.

A gate says "you shall not pass." A nudge says "here's the better path — and
yes, I know the shortcut you're thinking of." The name stuck.

The idea started as a "habit shim" inside my GameVM compiler project: a way to
coach the agents writing that codebase. It worked well enough to deserve its own
life, so here it is — a standalone .NET global tool with downloadable guide
packs ("rulesets") for common analyzer families.

## Does it actually help?

I measured it: 352 SonarSource analyzer rules × 3 approaches = 1,056 fix
trials, each judged blind as a genuine fix, a gamed fix, or a failure.

| What the agent saw | Genuine fixes | Gamed the metric |
|---|---|---:|
| Bare diagnostic | 88.9% | 7.4% |
| Diagnostic + official rule docs | 92.3% | 4.5% |
| Diagnostic + coaching guide | 96.9% | 0.6% |

Describing the rule gets about half the benefit. Explicitly naming the dodge is
the active ingredient — it takes gaming from 7% down to under 1%. The full
protocol and results live in `src/Nudge/EXPERIMENT.md`.

## Install

```bash
dotnet tool install -g Nudge
```

Requires the .NET 10 runtime (the tool is framework-dependent, which keeps the
package small). Nudge has zero NuGet dependencies — just the .NET SDK — so it
can never be the thing that breaks your restore.

## Use it

Point it at your solution:

```bash
nudge --sln YourSolution.sln
```

It builds the solution, collects the Roslyn/Sonar diagnostics, and prints each
one with its coaching guide. Exit code `0` means clean, `1` means there are
findings to fix, `2` means something went wrong with the tool or the build
itself.

Or pipe any build's output through it — handy in CI, git hooks, or agent
harnesses:

```bash
dotnet build YourSolution.sln | nudge --stdin
```

Need to pass build arguments through? `nudge --sln YourSolution.sln --build-args "-c Release"`.

**Adopting it on an existing codebase:** if you already have a backlog of
warnings, snooze it and fix forward — only new findings will surface:

```bash
nudge --sln YourSolution.sln --write-baseline .nudge/baseline.txt
nudge --sln YourSolution.sln --baseline .nudge/baseline.txt
```

## Guide packs (rulesets)

The coaching guides ship as versioned **rulesets** — one per analyzer family —
rather than being bundled with the tool. Install the ones you use:

```bash
nudge ruleset add sonar      # SonarSource analyzers (SonarAnalyzer.CSharp)
nudge ruleset list            # what's installed
nudge ruleset remove sonar   # remove one
nudge ruleset detect          # scan your csproj files and tell you what's missing
```

`ruleset add` accepts a ruleset name (downloaded from NuGet), a local folder
containing a `ruleset.json`, or a `.nupkg` file.

Run Nudge without the ruleset for one of your analyzers and it won't silently
fall back to generic guides — it tells you exactly what to install:

```
Some findings used generic guides because their rulesets aren't installed:
  SonarAnalyzer.CSharp → nudge ruleset add sonar
```

## Writing your own guides

If a rule fires often and only gets the generic fallback, write a guide for
it — that's your highest-leverage guide. Drop a markdown file at
`rules/<RULEID>.md`:

```markdown
# S1234 — Short Title

## Why
Why the rule exists, what it costs to ignore it.

## Do this
1. Concrete step.
2. Concrete step.

## AVOID
- The dodge, named explicitly.
```

Your hand-written guides always beat installed rulesets. And the `AVOID`
section is the load-bearing part: don't just explain the rule better — write
down the gaming moves you've actually seen agents try.

## The fine print

**How a guide is picked for each rule**, in order: `--rules` directory →
project-local `rules/` folder → installed rulesets (`~/.nudge/rulesets/`) →
your analyzer's own metadata (`KnownRules` in `Rules.cs`) → generic fallback.

**Exit codes** follow the habit-hooks convention: `0` = clean, `1` =
findings, `2` = tool or build failure (analyzer results may be incomplete).

**Design notes:** deterministic core, nondeterministic reader. Parsing,
grouping, baselines, and exit codes are plain code; judgment about the fix
stays with the agent — now informed by the rationale, delivered at the moment
of violation. The `AVOID` section is what turns a gate into coaching.

**Generating guides at scale:** the 510+ Sonar guides in `rulesets/` were
generated from the analyzer's own documentation and semantically validated per
the experiment protocol — generated guides can legitimize non-fixes if nobody
checks them (see the S2696 case in `EXPERIMENT.md`).

## License

Public domain — see LICENSE (Unlicense).
