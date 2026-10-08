# Descriptor Extraction

How to get the complete diagnostic list from an analyzer, in priority order.

## 1. Reflection helper (preferred)

`bin/extract-descriptors.cs` is a file-based C# app (requires .NET 10+). It loads
the analyzer DLL, finds all `DiagnosticAnalyzer` subclasses, and dumps
`SupportedDiagnostics` as JSON:

```bash
# Download the analyzer package first
dotnet nuget download <PackageId> --version <Version> -o /tmp/analyzer  # or manual download

# Find the analyzer DLL (usually under analyzers/dotnet/cs/)
find /tmp/analyzer -name "*.dll" | head

# Extract descriptors
dotnet run bin/extract-descriptors.cs -- /path/to/Analyzer.dll [probeDir] > descriptors.json
```

`probeDir` is an extra directory to search for the analyzer's dependencies
(typically `Microsoft.CodeAnalysis.dll`). If the analyzer's own directory lacks
it, point at the SDK's Roslyn binaries:
`~/dotnet/sdk/<version>/Roslyn/bincore` (adjust for the local SDK path).

Output is a JSON array: `[{ "id": "S1234", "title": "...", "description": "...", "severity": "...", "category": "...", "isEnabledByDefault": "True" }]`.

Caveats:
- Some analyzers ship multiple DLLs (per-TFM or per-language); extract from each and deduplicate by ID.
- Never execute analyzer *actions* — only read `SupportedDiagnostics`, which is side-effect free.
- Verified against SonarAnalyzer.CSharp 10.18: 459 descriptors extracted.

## 2. Shipped XML documentation

Many analyzer NuGet packages include an XML doc file alongside the DLL. The
`<member>` entries for descriptor fields often contain the full description:

```bash
unzip -p package.nupkg '**/*.xml' | grep -A2 'P:.*Descriptor'
```

Less structured than reflection, but needs no dependency resolution.

## 3. Analyzer documentation site

Most mature analyzers document every rule online (e.g. SonarSource rule pages,
Meziantou's analyzer docs, Roslynator docs). Crawl or fetch the rule index and
parse ID/title/description per rule. This is also the primary *research* source
for Phase 2 — prefer it over guessing from terse descriptor text.

## Coverage contract

The descriptor list from this phase is the contract: every ID gets a guide, or
an explicit skip with a reason (deprecated, test-only, infrastructure). Report
the counts in the final validation report.
