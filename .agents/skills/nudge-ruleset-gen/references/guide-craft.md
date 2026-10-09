# Guide Craft

The format and quality bar for Nudge coaching guides, distilled from the
three-way experiment (352 rules, 1,056 blind-judged trials).

## Format

```markdown
# <RULEID> — Short Title

## Why
2–4 sentences: why the rule exists, what it costs to ignore.

## Do this
1. Concrete step.
2. Concrete step.

## AVOID
- The metric-gaming move, named explicitly.
```

## The Why

Explain the *purpose*, not the mechanism. "This rule flags X" is not a Why.
A good Why answers: what goes wrong in real code when this is ignored, and who
pays the cost. 2–4 sentences; no more.

## The Do-this

Concrete, ordered steps. Each step should be verifiable ("extract the method
into a named type" not "improve the design"). Prefer the fix the rule's own
documentation recommends, stated as actions.

## The AVOID — the load-bearing section

The experiment's central finding: explicitly naming the dodge is the active
ingredient. Generated guides with rule-specific AVOID sections cut metric gaming
from 7.4% to 0.6%; official descriptions without them only reached 4.5%.

For each rule, think adversarially: *if I were a lazy agent told only the
diagnostic text, how would I silence it without fixing the problem?* Write those
moves down, phrased as prohibitions. Examples of the right specificity:

- S104 (file too long): "Do not split with `partial` classes just to drop the per-file count."
- S1133 (obsolete code): "Do not remove `[Obsolete]` while leaving the deprecated code in place."
- S4635: "Do not just assign the `Substring` result to a variable first."

Generic AVOID text ("do not suppress", "fix the root cause") is a failure mode —
it adds nothing over the bare diagnostic. Every rule has at least one
characteristic dodge; find it.

## Failure modes to avoid

**Legitimized non-fixes.** The most dangerous guide bug: "Do this" advice that
sounds like a fix but isn't. Real case — S2696 ("instance members should not
write to static fields") once advised "if genuinely shared, guard it with
`Interlocked`". The model wrapped the write in `Interlocked.Increment` — thread-safe,
still the exact design the rule flags, detector still fires. During the semantic
spot-check, ask of every Do-this step: *would the diagnostic still fire after
this change? Would the underlying issue still exist?* If yes to either, rewrite.

**Deprecated rules.** Don't fabricate advice for retired rules. Write an honest
guide: state the deprecation, point at the replacement rule if one exists, advise
removing it from the analyzer profile rather than churning code. See the Sonar
ruleset's S1227/S4212 guides as templates.

**Over-elaboration.** Guides that suggest ambitious refactors (new packages,
framework features) cause compile failures when the model follows them
aspirationally. Keep the Do-this steps within the flagged project's existing
dependencies unless the rule is specifically about adopting something.

## The 10% spot-check

For at least 10% of guides (minimum 5), verify against the official rule
documentation:
1. Does the Why match the documented rationale?
2. Does the Do-this match the documented remediation?
3. Does any Do-this step legitimize a non-fix (the S2696 test above)?
4. Are the AVOID dodges actually plausible for this rule?

Report pass/fail per sampled rule in the validation report.
