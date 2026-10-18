# Nudge (src)

Implementation of the Habit Shim. See the [repo README](../../README.md) for
usage, the guide format, and extension documentation.

Projects:

- `Nudge/` — the CLI: option parsing and the `ruleset`, `skill`, `explain`,
  and `init` subcommands, plus the build runner behind the default command.
- `Nudge.Core/` — the shared coaching pipeline used by both the CLI and the
  logger: `RuleGuide`/`KnownRules`/`RuleCatalog` (guide resolution order),
  diagnostic parsing, baselines, ruleset management, and `CoachingEngine`.
- `Nudge.Logger/` — `NudgeLogger`, the MSBuild `ILogger` that `nudge init`
  installs into a repo's `.nudge/` for build-time coaching.

Other content:

- `../../rulesets/Nudge.Rules.Sonar/rules/` — the shipped Sonar coaching
  guides, one markdown file per rule ID.
- `Nudge/EXPERIMENT.md` — the protocol and results of the three-way coaching experiment.
