# Installing the nudge-ruleset-gen skill

The skill follows the [Agent Skills open standard](https://agentskills.io)
(`SKILL.md` + YAML frontmatter with `name` and `description`), so the same files
work in every harness below. The easy path is the nudge tool itself:

```bash
nudge skill install                  # global, universal pair (see below)
nudge skill install --project        # into the current repo instead
nudge skill install --harness cursor,windsurf
nudge skill install --all            # every known harness
nudge skill list                     # where it's currently installed
nudge skill uninstall --global
```

With no `--harness`, `nudge skill install` writes two copies: `~/.agents/skills/`
and `~/.claude/skills/`. The `.agents/skills/` directory is the cross-harness
interop path — Codex, Cursor, Gemini CLI, GitHub Copilot, opencode, Amp, and
Windsurf all discover skills there — while Claude Code only reads its own
`.claude/skills/`. Two copies, full coverage. Restart the harness session after
installing so it picks the skill up.

Prefer the universal installer? `npx skills add kennethcochran/nudge` (from
[vercel-labs/skills](https://github.com/vercel-labs/skills)) detects your
harnesses and installs from this repo.

## Manual install matrix

Copy the whole `nudge-ruleset-gen/` folder (not just `SKILL.md` — the
`references/` guides and `bin/` helper travel with it) into the harness's
skills directory, then start a new session.

| Harness | Global (you) | Per-repo (team) |
|---|---|---|
| Claude Code | `~/.claude/skills/nudge-ruleset-gen/` | `.claude/skills/nudge-ruleset-gen/` |
| Codex CLI | `~/.agents/skills/nudge-ruleset-gen/` | `.agents/skills/nudge-ruleset-gen/` |
| Cursor | `~/.cursor/skills/nudge-ruleset-gen/` | `.cursor/skills/nudge-ruleset-gen/` |
| GitHub Copilot | `~/.copilot/skills/nudge-ruleset-gen/` | `.github/skills/nudge-ruleset-gen/` |
| Gemini CLI | `~/.gemini/skills/nudge-ruleset-gen/` | `.gemini/skills/nudge-ruleset-gen/` |
| opencode | `~/.config/opencode/skills/nudge-ruleset-gen/` | `.opencode/skills/nudge-ruleset-gen/` |
| Windsurf | `~/.codeium/windsurf/skills/nudge-ruleset-gen/` | `.windsurf/skills/nudge-ruleset-gen/` |
| Cline | `~/.cline/skills/nudge-ruleset-gen/` | `.cline/skills/nudge-ruleset-gen/` |
| Roo Code | `~/.roo/skills/nudge-ruleset-gen/` | `.roo/skills/nudge-ruleset-gen/` |
| Amp | `~/.config/agents/skills/nudge-ruleset-gen/` | `.agents/skills/nudge-ruleset-gen/` |
| Kiro | `~/.kiro/skills/nudge-ruleset-gen/` | `.kiro/skills/nudge-ruleset-gen/` |
| Antigravity | `~/.agents/skills/nudge-ruleset-gen/` | `.agent/skills/nudge-ruleset-gen/` |

On Windows, `~` is `%USERPROFILE%`.

## Gotchas per harness

- **Claude Code** — `SKILL.md` casing is exact and the folder name should be
  kebab-case. Skills are scanned at session start: installing mid-session does
  nothing until you start a new one. The `description` frontmatter is what drives
  discovery — keep it about *when* to use the skill, not just what it does.
- **Codex CLI** — only `name` and `description` frontmatter are honored; extra
  fields are ignored, so the skill deliberately uses just those two. The old
  `~/.codex/prompts/` location is deprecated in favor of skills. Skill catalog
  is bounded (~2% of context), so terse descriptions win.
- **Cursor** — the skills directory has moved across Cursor versions; if the
  paths above don't work, check Cursor's settings/docs for the current one.
  Don't paste skill bodies into `.cursor/rules/` — rules are for short policies,
  skills are loaded on demand.
- **GitHub Copilot** — agent skills require the `chat.useAgentSkills` setting in
  VS Code. Copilot also reads `~/.claude/skills/`, so the Claude Code copy
  doubles as a Copilot fallback.
- **Gemini CLI** — skills surface as `/skill:` slash commands and the first
  activation shows a confirmation prompt; expect it. Within one tier,
  `.agents/skills/` takes precedence over `.gemini/skills/`. `/skills list`
  verifies discovery; `gemini skills install <git-url> --consent` is the native
  installer.
- **opencode** — also supports remote skill catalogs via `"skills":
  ["https://…"]` in `opencode.jsonc`, if you'd rather pin a URL than copy files.
- **Windsurf / Cline / Roo** — SKILL.md support is newer here than in the
  Claude/Codex world; if a version doesn't pick the skill up, the fallback is a
  thin workspace rule pointing at the skill's path.
- **Aider** — no native SKILL.md support; it reads convention files
  (`AGENTS.md`/`CONVENTIONS.md`). Point it at the skill with a one-line rule
  instead of installing.

## Global or per-repo?

Global is the right default: this skill is a general capability (generate
rulesets for *any* analyzer), not something tied to one codebase — install once,
use in every repo you touch. Per-repo (`.agents/skills/`) fits teams that want
the skill versioned and shared, so every contributor's agent has it without a
separate install step.
