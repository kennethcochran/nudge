using System.IO.Compression;
using System.Text.Json;

namespace Nudge;

/// <summary>
/// Metadata for an installed coaching-guide ruleset, read from ruleset.json.
/// </summary>
internal sealed record RulesetManifest(
    string Name,
    string Version,
    string PackageId,
    IReadOnlyList<string> RulePrefixes,
    IReadOnlyList<string> AnalyzerPackages,
    string Description)
{
    public static RulesetManifest Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        return new RulesetManifest(
            root.GetProperty("name").GetString() ?? "",
            root.GetProperty("version").GetString() ?? "",
            root.GetProperty("packageId").GetString() ?? "",
            root.GetProperty("rulePrefixes").EnumerateArray().Select(e => e.GetString() ?? "").ToList(),
            root.TryGetProperty("analyzerPackages", out var ap)
                ? ap.EnumerateArray().Select(e => e.GetString() ?? "").ToList()
                : new List<string>(),
            root.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "");
    }
}

/// <summary>
/// Installs, lists, and removes coaching-guide rulesets.
/// Rulesets live under ~/.nudge/rulesets/&lt;name&gt;/, each containing
/// ruleset.json and the guide markdown files.
/// </summary>
internal static class RulesetManager
{
    public static string RulesetsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nudge", "rulesets");

    /// <summary>Well-known rulesets and the NuGet package IDs that provide them.</summary>
    public static readonly IReadOnlyDictionary<string, string> KnownRulesets =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sonar"] = "Nudge.Rules.Sonar",
        };

    public static IReadOnlyList<(string Name, RulesetManifest Manifest)> ListInstalled()
    {
        var result = new List<(string, RulesetManifest)>();
        if (!Directory.Exists(RulesetsDir))
            return result;
        foreach (var dir in Directory.GetDirectories(RulesetsDir))
        {
            var manifestPath = Path.Combine(dir, "ruleset.json");
            if (!File.Exists(manifestPath))
                continue;
            try
            {
                result.Add((Path.GetFileName(dir), RulesetManifest.Load(manifestPath)));
            }
            catch
            {
                // Skip malformed manifests; ListInstalled must not throw.
            }
        }
        return result;
    }

    public static bool IsInstalled(string name) =>
        Directory.Exists(Path.Combine(RulesetsDir, name)) &&
        File.Exists(Path.Combine(RulesetsDir, name, "ruleset.json"));

    /// <summary>
    /// Guides directory for an installed ruleset (the rules/ subdir),
    /// or null if not installed.
    /// </summary>
    public static string? GuidesDir(string name)
    {
        var dir = Path.Combine(RulesetsDir, name, "rules");
        return Directory.Exists(dir) ? dir : null;
    }

    /// <summary>
    /// Installs a ruleset by name (from NuGet) or from a local directory/nupkg path.
    /// Returns the installed ruleset name.
    /// </summary>
    public static async Task<string> InstallAsync(string nameOrPath)
    {
        // Local directory containing ruleset.json?
        if (Directory.Exists(nameOrPath) && File.Exists(Path.Combine(nameOrPath, "ruleset.json")))
            return InstallFromDirectory(nameOrPath);

        // Local .nupkg file?
        if (File.Exists(nameOrPath) && nameOrPath.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
            return await InstallFromNupkgAsync(nameOrPath);

        // Named ruleset from NuGet.
        if (!KnownRulesets.TryGetValue(nameOrPath, out var packageId))
            throw new InvalidOperationException(
                $"Unknown ruleset '{nameOrPath}'. Known rulesets: {string.Join(", ", KnownRulesets.Keys)}. " +
                "Or pass a local directory containing ruleset.json, or a .nupkg file path.");

        var nupkgPath = await DownloadPackageAsync(packageId);
        try
        {
            return await InstallFromNupkgAsync(nupkgPath);
        }
        finally
        {
            try { File.Delete(nupkgPath); } catch { }
        }
    }

    public static void Remove(string name)
    {
        var dir = Path.Combine(RulesetsDir, name);
        if (!Directory.Exists(dir))
            throw new InvalidOperationException($"Ruleset '{name}' is not installed.");
        Directory.Delete(dir, recursive: true);
    }

    private static string InstallFromDirectory(string sourceDir)
    {
        var manifest = RulesetManifest.Load(Path.Combine(sourceDir, "ruleset.json"));
        var destDir = Path.Combine(RulesetsDir, manifest.Name);
        if (Directory.Exists(destDir))
            Directory.Delete(destDir, recursive: true);
        CopyDirectory(sourceDir, destDir);
        return manifest.Name;
    }

    private static async Task<string> InstallFromNupkgAsync(string nupkgPath)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "nudge-ruleset-" + Guid.NewGuid().ToString("N"));
        try
        {
            ZipFile.ExtractToDirectory(nupkgPath, tempDir);
            var manifestPath = Directory.GetFiles(tempDir, "ruleset.json", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (manifestPath == null)
                throw new InvalidOperationException("Package does not contain a ruleset.json manifest.");
            return InstallFromDirectory(Path.GetDirectoryName(manifestPath)!);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static async Task<string> DownloadPackageAsync(string packageId)
    {
        // NuGet flat container API — plain HTTPS, no NuGet client dependency.
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("nudge/1.0");
        var id = packageId.ToLowerInvariant();

        // Resolve latest version.
        var indexUrl = $"https://api.nuget.org/v3-flatcontainer/{id}/index.json";
        var indexJson = await http.GetStringAsync(indexUrl);
        using var indexDoc = JsonDocument.Parse(indexJson);
        var versions = indexDoc.RootElement.GetProperty("versions").EnumerateArray()
            .Select(e => e.GetString()!).ToList();
        if (versions.Count == 0)
            throw new InvalidOperationException($"No versions found for package '{packageId}'.");
        var version = versions[^1];

        var nupkgUrl = $"https://api.nuget.org/v3-flatcontainer/{id}/{version}/{id}.{version}.nupkg";
        var destPath = Path.Combine(Path.GetTempPath(), $"{id}.{version}.nupkg");
        using var response = await http.GetAsync(nupkgUrl);
        response.EnsureSuccessStatusCode();
        await using var fs = File.Create(destPath);
        await response.Content.CopyToAsync(fs);
        return destPath;
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
        {
            // Only ship the manifest and guide markdown; never project files.
            var name = Path.GetFileName(file);
            if (!name.Equals("ruleset.json", StringComparison.OrdinalIgnoreCase) &&
                !name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                continue;
            File.Copy(file, Path.Combine(dest, name), overwrite: true);
        }
        foreach (var dir in Directory.GetDirectories(source))
        {
            var name = Path.GetFileName(dir);
            if (name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("obj", StringComparison.OrdinalIgnoreCase))
                continue;
            CopyDirectory(dir, Path.Combine(dest, name));
        }
    }
}
