---
name: "nudge_ruleset_gen"
description: "Generate a Nudge coaching-guide ruleset for a Roslyn analyzer or static analysis tool. Use when the user asks to create rules for an analyzer (e.g. 'make a nudge ruleset for Meziantou.Analyzer') or to extend Nudge to a new diagnostic source."
---

# Nudge Ruleset Generator

## Purpose
Systematically analyze a Roslyn analyzer (or other static analysis tool) and produce a validated Nudge ruleset: one coaching guide per diagnostic, packaged for `nudge ruleset add`.

## Workflow

**Phase 1 — Discover.** Identify the analyzer: NuGet package ID or DLL path. Extract every diagnostic descriptor (ID, title, description, severity, category). See `references/extraction.md` for methods in priority order: reflection helper (`bin/extract-descriptors.cs`) → shipped XML docs → analyzer documentation site. Record the full descriptor list; this is the coverage contract — every descriptor gets a guide or an explicit skip reason (e.g. deprecated).

**Phase 2 — Research.** For each diagnostic, establish its *purpose* before writing. Sources in priority order:
1. The analyzer's own rule documentation (most analyzers document each rule).
2. The descriptor's title + description, read carefully.
3. Web search for the rule ID — official docs first, then quality community writeups.
4. The analyzer's source code, for ambiguous cases.

Do not write a guide from the ID and title alone. If the purpose is still unclear after research, mark the rule for human review rather than guessing.

**Phase 3 — Write.** One markdown file per rule at `<ruleset-dir>/rules/<RULEID>.md`, following the format in `references/guide-craft.md`. The load-bearing section is **AVOID**: for each rule, think adversarially about how an agent would game *that specific* metric, and name the dodges explicitly. Generic AVOID text ("don't suppress") is a failure — it must be rule-specific.

**Phase 4 — Validate.**
- Structural: every file parses to non-empty Why / Do this / AVOID (mirror the checks in Nudge's `ShippedGuideTests`).
- Semantic spot-check: for at least 10% of guides (minimum 5), verify the advice against the official rule documentation. Pay special attention to guides where the "Do this" could legitimize a non-fix (see the S2696 failure mode in `references/guide-craft.md`).
- Deprecated rules: write honest guides that say so (point at the replacement rule or advise removing it from the profile), per the pattern in the Sonar ruleset.

**Phase 5 — Package.** Write `ruleset.json` (name, version, packageId `Nudge.Rules.<Name>`, rulePrefixes, analyzerPackages, description) at the ruleset root. Test with `nudge ruleset add <dir>` and confirm guides resolve for sample diagnostics.

## Output Contract
A directory ready for `nudge ruleset add`:
```
<ruleset>/
  ruleset.json
  rules/<RULEID>.md   (one per diagnostic)
```
Plus a short report: descriptor count, guides written, rules skipped (with reasons), semantic spot-check results, and any rules flagged for human review.

## Operating Rules
1. Never invent a rule's purpose. Research first; flag for human review when uncertain.
2. The AVOID section must name rule-specific gaming moves, not generic platitudes.
3. Deprecated/retired rules get honest "this rule is deprecated" guides, not fabricated advice.
4. Keep Nudge's zero-dependency constraint in mind: the skill output is markdown + JSON only.
5. For submission to the Nudge repo, include the validation report — maintainers evaluate the methodology, not just the guides.
