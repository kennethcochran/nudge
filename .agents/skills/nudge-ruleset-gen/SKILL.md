---
name: nudge-ruleset-gen
description: "Use when the user asks to create a Nudge ruleset for a Roslyn analyzer or static analysis tool (e.g. 'make a nudge ruleset for Meziantou.Analyzer'), or to extend Nudge's coaching guides to a new diagnostic source."
---

# Nudge Ruleset Generator

Produce a validated Nudge ruleset: one coaching guide per diagnostic of a
Roslyn analyzer (or other static analysis tool), packaged for
`nudge ruleset add`.

## Workflow

**Phase 1 — Discover.** Extract every diagnostic descriptor (ID, title,
description, severity, category) for the analyzer; methods in priority
order are in `references/extraction.md`, starting with the reflection
helper at `bin/extract-descriptors.cs`. The full descriptor list is the
coverage contract: every descriptor gets a guide or an explicit skip
reason (e.g. deprecated).

**Phase 2 — Research.** Establish each rule's *purpose* before writing,
from (in priority order) the analyzer's own rule documentation, the
descriptor text, web search for the rule ID, and the analyzer's source.
Never write a guide from the ID and title alone; if the purpose is still
unclear after research, flag the rule for human review instead of
guessing.

**Phase 3 — Write.** One file per rule at
`<ruleset-dir>/rules/<RULEID>.md`, in the format of
`references/guide-craft.md`. The load-bearing section is **AVOID**: for
each rule, think adversarially about how an agent would game *that
specific* metric and name the dodges. A generic AVOID ("don't suppress")
is a failed guide.

**Phase 4 — Validate.**
- Structural: every guide parses to non-empty Why / Do this / AVOID (the
  checks in Nudge's `ShippedGuideTests`).
- Semantic spot-check: at least 10% of guides (minimum 5) verified
  against the official rule documentation, watching for a "Do this" that
  legitimizes a non-fix (the S2696 failure mode in
  `references/guide-craft.md`).
- Deprecated rules get honest guides that say so — point at the
  replacement rule or advise dropping the rule — per the Sonar ruleset's
  pattern.

**Phase 5 — Package.** Write `ruleset.json` (name, version, packageId
`Nudge.Rules.<Name>`, rulePrefixes, analyzerPackages, description) at
the ruleset root. Verify with `nudge ruleset add <dir>` that guides
resolve for sample diagnostics.

## Output contract

```
<ruleset>/
  ruleset.json
  rules/<RULEID>.md   (one per diagnostic)
```

Plus a short report: descriptor count, guides written, rules skipped
(with reasons), spot-check results, and rules flagged for human review.
Output is markdown + JSON only; for submission to the Nudge repo, the
report is what gets reviewed.
