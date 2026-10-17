# Installing the nudge-coaching skill

The skill follows the [Agent Skills open standard](https://agentskills.io)
(`SKILL.md` + YAML frontmatter with `name` and `description`), so the same
files work in every supported harness. The easy path is the nudge tool
itself:

```bash
nudge skill install                    # installs BOTH nudge skills, global
nudge skill install nudge-coaching     # just this one
nudge skill install --project          # into the current repo instead
nudge skill install --harness cursor,windsurf
nudge skill list                       # where the skills are installed
nudge skill uninstall nudge-coaching   # remove just this one
nudge skill uninstall                  # remove both
```

With no `--harness`, installs go to the universal pair (`~/.agents/skills/`
and `~/.claude/skills/`), which together cover every known harness. Restart
the harness session after installing so it discovers the skill. The
per-harness directory matrix and gotchas are the same as for
`nudge-ruleset-gen` — see that skill's `INSTALL.md`.

Manual install: copy the whole `nudge-coaching/` folder into your harness's
skills directory (e.g. `~/.agents/skills/nudge-coaching/` or
`~/.claude/skills/nudge-coaching/`), then start a new session.

Global or per-repo? This skill is a general working loop for any repo that
uses nudge, so global is the right default. Per-repo (`--project`) fits teams
that want every contributor's agent to run the same loop in a nudge-enabled
repo.
