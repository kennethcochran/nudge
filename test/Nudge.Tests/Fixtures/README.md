# Test fixtures

## errorlog.sarif

A real compiler ErrorLog SARIF, produced 2026-10-17 by building a scratch
fixture project (in a path containing spaces, `/tmp/nudge sarif fixture/`)
with `<ErrorLog>diag.sarif,version=2.1</ErrorLog>`:

- `Widget.Announce` / `Widget.Count` / `Widget.Unused` — methods that do
  not access instance data, with `dotnet_diagnostic.CA1822.severity =
  suggestion` in the fixture's `.editorconfig`, so CA1822 arrives at the
  Info severity the build event stream never carries. SARIF level: `note`.
- `Widget.Count` allocates `new int[0]`, with
  `dotnet_diagnostic.CA1825.severity = error`. SARIF level: `error`.
- An unused local in `Widget.Unused`. CS0219, SARIF level: `warning`.

Trimmed for the repo: the `results` array is verbatim from the real build;
the `tool.driver.rules` array is reduced to the three referenced rules and
each result's `ruleIndex` remapped to match (the untrimmed original lists
all 331 rule descriptors, ~350 KB, and carries no extra findings).
