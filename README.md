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

The idea started as a "habit shim" inside my [GameVM](https://github.com/kennethcochran/GameVM) compiler project: a way to
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

**Build-time coaching:** `nudge init` installs an MSBuild logger into the repo
so every `dotnet build` prints coaching inline, right where the diagnostic
appears — no wrapper, no piping, no separate report to go find:

```bash
nudge init
dotnet build
```

```
Program.cs(14,21): error CS0659: 'Customer' overrides Object.Equals(object o) but does not override Object.GetHashCode()
nudge: coaching for CS0659 — Override Equals Without GetHashCode
nudge: Why: Equals and GetHashCode are a contract: equal objects must produce equal hash codes...
nudge: Do this:
nudge:   1. Override GetHashCode to combine the same fields Equals compares, e.g. HashCode.Combine(X, Y).
nudge:   ...
nudge: AVOID:
nudge:   - Do not implement GetHashCode() => 0; ...
```

A rule's full guide prints with its first diagnostic in a build; later
diagnostics for the same rule get a one-line reminder. Findings in
`.nudge/baseline.txt` print no coaching — they're snoozed. The build itself
is never failed by the logger — nudge coaches, it doesn't gate. (Earlier
versions wrote a `.nudge/report.md` report and an end-of-build pointer line;
the inline design replaces that — a stale `report.md` is safe to delete.)

Info-severity findings (suggestion-level analyzer rules) never reach a
build logger at all — no event carries them. Those are coached from a
second channel: `nudge init` arranges a per-project compiler ErrorLog, and
at the end of the build the logger reads the SARIF files and prints each
Info finding with the same coaching. Only the files of projects that
started in this build are read, so a partial build never prints another
project's stale findings from an earlier build. To inspect a SARIF log on its own,
`nudge coach <file.sarif>` prints the full grouped report.

Prefer a quieter output? `nudge init --console-logger` goes one step
further: the nudge logger replaces the default console logger
(`/noconsolelogger`) and prints every diagnostic itself in canonical
MSBuild format, coaching inline, plus the end-of-build summary. It is not
a byte-for-byte clone of the default logger — ordinary build chatter
(project/target/task messages) is not reproduced.

What `nudge init` actually installs:

- `.nudge/Nudge.Logger.dll` and `.nudge/Nudge.Core.dll` — the logger payload.
  Re-running `nudge init` after a nudge upgrade refreshes them.
- A `/logger:` line (plus a marker comment) in `Directory.Build.rsp` at the
  repo root, merged with any content already there. The line points MSBuild
  at the payload, at the baseline (`.nudge/baseline.txt`), and at the SARIF
  directory (`.nudge/sarif`). Console mode adds a `/noconsolelogger` line
  alongside.
- A labelled `ErrorLog` group in `Directory.Build.props` at the repo root,
  merged the same way: one compiler SARIF log per project, written to
  `.nudge/sarif/<project>.sarif`. If your props already set `ErrorLog`,
  init detects it and leaves it exactly as is — your arrangement wins, and
  only the location nudge arranged gets the build-end coaching.
  Known limitation: the file is keyed by project *name*, so two projects
  with the same name in different directories share one SARIF file and the
  last compilation wins — their Info findings are not kept separately.

> **Trust note:** an MSBuild logger is code that executes inside every build
> of the repo. That is exactly why installation is an explicit, per-repo
> opt-in — `nudge init` is a command a human runs, never something that
> happens as a side effect — and why the payload sits in `.nudge/`, in plain
> sight, instead of a global hook. Committing `Directory.Build.rsp` and
> `.nudge/` shares the setup with everyone who clones the repo, loggers and
> all; review it like any other build input.

Which builds does this cover? `dotnet build` on the .NET 10 SDK (the same
requirement as the nudge tool itself) — including CI and agent harnesses,
which build from the command line. Two honest gaps: Visual Studio's
in-process IDE builds ignore `Directory.Build.rsp` entirely (no coaching;
build from the command line, or use `nudge explain` below), and MSBuild
hosts running on .NET Framework or an older SDK can't load a .NET 10 logger
at all — run `nudge init --remove` before building there.

Turning it off, in descending order of permanence: `nudge init --remove`
(undoes the install, keeps your `baseline.txt`), delete the nudge lines from
`Directory.Build.rsp`, or skip response files for a single build with
`dotnet build /noautoresponse`.

**One-off lookup:** `nudge explain S1234` prints the coaching guide for a single
rule — handy when an agent (or human) meets a diagnostic outside a build.

**The agent loop, as a skill:** `nudge skill install` also installs the
`nudge-coaching` skill, which teaches an AI coding agent the loop this tool
is built for: build → fix the root cause the inline coaching names (never
the dodge in its AVOID section) → rebuild, with `nudge explain` for
diagnostics met outside a build. See `.agents/skills/nudge-coaching/` in
this repo.

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

## Contributing a ruleset

Built guides for an analyzer nobody's covered yet? This repo ships the skill
for it: `.agents/skills/nudge-ruleset-gen/` walks through the whole process —
extracting the analyzer's diagnostic descriptors, researching each rule,
writing the guides, validating them, and packaging the ruleset. Get it into
your AI harness with `nudge skill install` (which installs the shipped
skills as a bundle — this one and `nudge-coaching`). Open a PR with
the new ruleset and include the validation report — the methodology is what
gets reviewed, not just the guides.

## Installing the skills

Both shipped skills (`nudge-coaching` and `nudge-ruleset-gen`) follow the
[Agent Skills open standard](https://agentskills.io) — a `SKILL.md` with
`name` and `description` frontmatter — so the same files work in every
harness below. They install and uninstall as a bundle:

```bash
nudge skill install                    # global, universal pair (see below)
nudge skill install --project          # into the current repo instead
nudge skill install --harness cursor,windsurf
nudge skill install --all              # every known harness
nudge skill list                       # where the skills are installed
nudge skill uninstall                  # remove the skills
```

With no `--harness`, `nudge skill install` writes two copies:
`~/.agents/skills/` and `~/.claude/skills/`. The `.agents/skills/` directory
is the cross-harness interop path — Codex, Cursor, Gemini CLI, GitHub
Copilot, opencode, Amp, and Windsurf all discover skills there — while
Claude Code only reads its own `.claude/skills/`. Two copies, full coverage.
Restart the harness session after installing so it picks the skills up.
Prefer the universal installer? `npx skills add kennethcochran/nudge` (from
[vercel-labs/skills](https://github.com/vercel-labs/skills)) detects your
harnesses and installs from this repo.

Global is the right default for both skills: they're general capabilities,
not tied to one codebase. `--project` fits teams that want the skills
versioned and shared, so every contributor's agent has them without a
separate install step.

### Manual install

Copy the whole skill folder (not just `SKILL.md` — `nudge-ruleset-gen`'s
`references/` guides and `bin/` helper travel with it) into the harness's
skills directory, then start a new session. Substitute the skill name for
`<skill>`:

| Harness | Global (you) | Per-repo (team) |
|---|---|---|
| Claude Code | `~/.claude/skills/<skill>/` | `.claude/skills/<skill>/` |
| Codex CLI | `~/.agents/skills/<skill>/` | `.agents/skills/<skill>/` |
| Cursor | `~/.cursor/skills/<skill>/` | `.cursor/skills/<skill>/` |
| GitHub Copilot | `~/.copilot/skills/<skill>/` | `.github/skills/<skill>/` |
| Gemini CLI | `~/.gemini/skills/<skill>/` | `.gemini/skills/<skill>/` |
| opencode | `~/.config/opencode/skills/<skill>/` | `.opencode/skills/<skill>/` |
| Windsurf | `~/.codeium/windsurf/skills/<skill>/` | `.windsurf/skills/<skill>/` |
| Cline | `~/.cline/skills/<skill>/` | `.cline/skills/<skill>/` |
| Roo Code | `~/.roo/skills/<skill>/` | `.roo/skills/<skill>/` |
| Amp | `~/.config/agents/skills/<skill>/` | `.agents/skills/<skill>/` |
| Kiro | `~/.kiro/skills/<skill>/` | `.kiro/skills/<skill>/` |
| Antigravity | `~/.agents/skills/<skill>/` | `.agent/skills/<skill>/` |

On Windows, `~` is `%USERPROFILE%`.

### Gotchas per harness

- **Claude Code** — `SKILL.md` casing is exact and the folder name should be
  kebab-case. Skills are scanned at session start: installing mid-session
  does nothing until you start a new one. The `description` frontmatter is
  what drives discovery — keep it about *when* to use the skill, not just
  what it does.
- **Codex CLI** — only `name` and `description` frontmatter are honored;
  extra fields are ignored, so the skills deliberately use just those two.
  The old `~/.codex/prompts/` location is deprecated in favor of skills.
  The skill catalog is bounded (~2% of context), so terse descriptions win.
- **Cursor** — the skills directory has moved across Cursor versions; if
  the paths above don't work, check Cursor's settings/docs for the current
  one. Don't paste skill bodies into `.cursor/rules/` — rules are for short
  policies, skills are loaded on demand.
- **GitHub Copilot** — agent skills require the `chat.useAgentSkills`
  setting in VS Code. Copilot also reads `~/.claude/skills/`, so the Claude
  Code copy doubles as a Copilot fallback.
- **Gemini CLI** — skills surface as `/skill:` slash commands and the first
  activation shows a confirmation prompt; expect it. Within one tier,
  `.agents/skills/` takes precedence over `.gemini/skills/`. `/skills list`
  verifies discovery; `gemini skills install <git-url> --consent` is the
  native installer.
- **opencode** — also supports remote skill catalogs via `"skills":
  ["https://…"]` in `opencode.jsonc`, if you'd rather pin a URL than copy
  files.
- **Windsurf / Cline / Roo** — SKILL.md support is newer here than in the
  Claude/Codex world; if a version doesn't pick a skill up, the fallback is
  a thin workspace rule pointing at the skill's path.
- **Aider** — no native SKILL.md support; it reads convention files
  (`AGENTS.md`/`CONVENTIONS.md`). Point it at the skill with a one-line rule
  instead of installing.

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
