namespace Nudge;

/// <summary>
/// Installs the nudge-ruleset-gen skill into AI coding harnesses so their agents
/// can generate Nudge rulesets for analyzers nobody has covered yet.
/// The skill ships inside the nudge tool package under skills/nudge-ruleset-gen/.
/// </summary>
internal static class SkillInstaller
{
    public const string SkillName = "nudge-ruleset-gen";

    /// <summary>
    /// A harness and where it looks for skills: home-relative directory for
    /// global installs, cwd-relative directory for per-project installs.
    /// Paths come from each harness's documentation and the agentskills.io
    /// interop convention; see INSTALL.md in the skill directory for sources
    /// and gotchas.
    /// </summary>
    internal sealed record Harness(string Name, string GlobalDir, string ProjectDir);

    internal static readonly IReadOnlyList<Harness> Harnesses = new List<Harness>
    {
        // Universal interop path (.agents/skills): read natively by Codex, Cursor,
        // Gemini CLI, GitHub Copilot, opencode, Amp, Windsurf, and others.
        new("agents", ".agents/skills", ".agents/skills"),
        // Claude Code does not read .agents/skills; it needs its own directory.
        new("claude", ".claude/skills", ".claude/skills"),
        new("codex", ".agents/skills", ".agents/skills"),
        new("cursor", ".cursor/skills", ".cursor/skills"),
        new("copilot", ".copilot/skills", ".github/skills"),
        new("gemini", ".gemini/skills", ".gemini/skills"),
        new("opencode", Path.Combine(".config", "opencode", "skills"), ".opencode/skills"),
        new("windsurf", Path.Combine(".codeium", "windsurf", "skills"), ".windsurf/skills"),
        new("cline", ".cline/skills", ".cline/skills"),
        new("roo", ".roo/skills", ".roo/skills"),
        new("amp", Path.Combine(".config", "agents", "skills"), ".agents/skills"),
        new("kiro", ".kiro/skills", ".kiro/skills"),
        new("antigravity", ".agents/skills", ".agent/skills"),
    };

    /// <summary>
    /// Harnesses installed when the user names none: the universal pair that
    /// covers every harness in the table except Claude Code, plus Claude Code
    /// itself. Two copies, full coverage.
    /// </summary>
    internal static readonly IReadOnlyList<string> DefaultHarnesses = new List<string> { "agents", "claude" };

    internal static string HomeDir =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// Locates the skill source bundled with the tool (skills/nudge-ruleset-gen/
    /// next to the binary), or null when it isn't there.
    /// </summary>
    public static string? FindSkillSource()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "skills", SkillName);
        return Directory.Exists(dir) && File.Exists(Path.Combine(dir, "SKILL.md")) ? dir : null;
    }

    /// <summary>
    /// Resolves the full target directories (each ending in the skill folder)
    /// for the named harnesses. Unknown names throw <see cref="CliUsageException"/>.
    /// </summary>
    public static IReadOnlyList<string> ResolveTargets(
        IEnumerable<string> harnessNames, bool globalScope, string homeDir, string projectDir)
    {
        var targets = new List<string>();
        foreach (var name in harnessNames)
        {
            var harness = Harnesses.FirstOrDefault(h =>
                h.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (harness == null)
                throw new CliUsageException(
                    $"Unknown harness '{name}'. Known harnesses: {string.Join(", ", Harnesses.Select(h => h.Name))}");
            var root = globalScope
                ? Path.Combine(homeDir, harness.GlobalDir)
                : Path.Combine(projectDir, harness.ProjectDir);
            targets.Add(Path.Combine(root, SkillName));
        }
        return targets.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Copies the skill into each target directory. Existing installs are
    /// skipped unless <paramref name="force"/> is set. Returns the directories
    /// actually written.
    /// </summary>
    public static IReadOnlyList<string> Install(
        string sourceDir, IReadOnlyList<string> targets, bool force, bool dryRun, Action<string> log)
    {
        var written = new List<string>();
        foreach (var target in targets)
        {
            var marker = Path.Combine(target, "SKILL.md");
            if (File.Exists(marker) && !force)
            {
                log($"Skipped {target} (already installed; use --force to overwrite).");
                continue;
            }
            if (dryRun)
            {
                log($"Would install to {target}");
                continue;
            }
            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);
            CopyDirectory(sourceDir, target);
            written.Add(target);
            log($"Installed to {target}");
        }
        return written;
    }

    /// <summary>Removes the skill from each target directory. Returns the count removed.</summary>
    public static int Uninstall(IReadOnlyList<string> targets, bool dryRun, Action<string> log)
    {
        var removed = 0;
        foreach (var target in targets)
        {
            if (!Directory.Exists(Path.Combine(target, "SKILL.md")) && !Directory.Exists(target))
            {
                log($"Not installed at {target}; skipping.");
                continue;
            }
            if (dryRun)
            {
                log($"Would remove {target}");
                continue;
            }
            Directory.Delete(target, recursive: true);
            removed++;
            log($"Removed {target}");
        }
        return removed;
    }

    /// <summary>
    /// Reports every location where the skill is currently installed, across all
    /// known harness directories (global under <paramref name="homeDir"/>,
    /// project under <paramref name="projectDir"/>).
    /// </summary>
    public static IReadOnlyList<string> ListInstalled(string homeDir, string projectDir)
    {
        var found = new List<string>();
        foreach (var harness in Harnesses)
        {
            foreach (var root in new[]
                     {
                         Path.Combine(homeDir, harness.GlobalDir),
                         Path.Combine(projectDir, harness.ProjectDir),
                     })
            {
                var dir = Path.Combine(root, SkillName);
                if (File.Exists(Path.Combine(dir, "SKILL.md")) &&
                    !found.Contains(dir, StringComparer.OrdinalIgnoreCase))
                    found.Add(dir);
            }
        }
        return found;
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }
}
