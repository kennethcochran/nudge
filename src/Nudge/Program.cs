using System.Diagnostics;
using System.Text;

namespace Nudge;

/// <summary>
/// Entry point. Exit codes: 0 = clean, 1 = findings, 2 = tool/usage/build failure.
/// </summary>
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Subcommands dispatch before option parsing.
        if (args.Length > 0 && args[0].Equals("ruleset", StringComparison.OrdinalIgnoreCase))
            return await RunRulesetCommand(args.Skip(1).ToArray());
        if (args.Length > 0 && args[0].Equals("skill", StringComparison.OrdinalIgnoreCase))
            return RunSkillCommand(args.Skip(1).ToArray());

        CliOptions options;
        try
        {
            options = CliOptions.Parse(args);
        }
        catch (CliUsageException ex)
        {
            if (ex.Message != null)
                Console.Error.WriteLine($"Error: {ex.Message}\n");
            Console.Error.WriteLine(CliOptions.Usage);
            return 2;
        }

        try
        {
            return await RunAsync(options);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"nudge failed: {ex.Message}");
            return 2;
        }
    }

    private static async Task<int> RunRulesetCommand(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine(RulesetUsage);
            return 2;
        }
        try
        {
            switch (args[0].ToLowerInvariant())
            {
                case "add":
                    if (args.Length < 2)
                        throw new CliUsageException("nudge ruleset add <name> — name, local directory, or .nupkg path required.");
                    var installed = await RulesetManager.InstallAsync(args[1]);
                    Console.WriteLine($"Installed ruleset '{installed}'.");
                    return 0;
                case "list":
                    var rulesets = RulesetManager.ListInstalled();
                    if (rulesets.Count == 0)
                        Console.WriteLine("No rulesets installed. Try: nudge ruleset add sonar");
                    foreach (var (name, manifest) in rulesets)
                        Console.WriteLine($"{name} ({manifest.Version}) — {manifest.Description}");
                    return 0;
                case "remove":
                    if (args.Length < 2)
                        throw new CliUsageException("nudge ruleset remove <name> — ruleset name required.");
                    RulesetManager.Remove(args[1]);
                    Console.WriteLine($"Removed ruleset '{args[1]}'.");
                    return 0;
                case "detect":
                    var root = Directory.GetCurrentDirectory();
                    var missing = AnalyzerDetector.MissingRulesets(root);
                    if (missing.Count == 0)
                    {
                        var detected = AnalyzerDetector.DetectAnalyzers(root);
                        Console.WriteLine(detected.Count == 0
                            ? "No known analyzer packages detected in csproj files under " + root
                            : "All detected analyzers have rulesets installed: " + string.Join(", ", detected));
                    }
                    else
                    {
                        foreach (var (analyzer, ruleset) in missing)
                            Console.WriteLine($"{analyzer} detected — ruleset not installed: nudge ruleset add {ruleset}");
                    }
                    return 0;
                default:
                    throw new CliUsageException($"Unknown ruleset command: {args[0]}");
            }
        }
        catch (CliUsageException ex)
        {
            if (ex.Message != null)
                Console.Error.WriteLine($"Error: {ex.Message}\n");
            Console.Error.WriteLine(RulesetUsage);
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"nudge ruleset failed: {ex.Message}");
            return 2;
        }
    }

    private const string RulesetUsage = """
        nudge ruleset — manage coaching-guide rulesets.

        Usage:
          nudge ruleset add <name>      Install a ruleset (from NuGet, a local directory, or a .nupkg file)
          nudge ruleset list            List installed rulesets
          nudge ruleset remove <name>   Remove an installed ruleset
          nudge ruleset detect          Detect analyzer packages in csproj files and report missing rulesets

        Known rulesets: sonar (Nudge.Rules.Sonar — SonarSource analyzers)
        """;

    /// <summary>
    /// Handles `nudge skill ...`: install the nudge-ruleset-gen skill into AI
    /// coding harnesses so their agents can generate rulesets for uncovered analyzers.
    /// </summary>
    private static int RunSkillCommand(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine(SkillUsage);
            return 2;
        }
        try
        {
            switch (args[0].ToLowerInvariant())
            {
                case "install":
                    return RunSkillInstall(args.Skip(1).ToArray());
                case "uninstall":
                    return RunSkillUninstall(args.Skip(1).ToArray());
                case "list":
                    if (args.Length > 1)
                        throw new CliUsageException("nudge skill list takes no arguments.");
                    var installed = SkillInstaller.ListInstalled(
                        SkillInstaller.HomeDir, Directory.GetCurrentDirectory());
                    if (installed.Count == 0)
                        Console.WriteLine("Skill not installed anywhere. Try: nudge skill install");
                    foreach (var dir in installed)
                        Console.WriteLine(dir);
                    return 0;
                case "harnesses":
                    if (args.Length > 1)
                        throw new CliUsageException("nudge skill harnesses takes no arguments.");
                    foreach (var h in SkillInstaller.Harnesses)
                        Console.WriteLine($"{h.Name,-12} global: ~/{h.GlobalDir}   project: {h.ProjectDir}/");
                    return 0;
                default:
                    throw new CliUsageException($"Unknown skill command: {args[0]}");
            }
        }
        catch (CliUsageException ex)
        {
            if (ex.Message != null)
                Console.Error.WriteLine($"Error: {ex.Message}\n");
            Console.Error.WriteLine(SkillUsage);
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"nudge skill failed: {ex.Message}");
            return 2;
        }
    }

    private static int RunSkillInstall(string[] args)
    {
        var (harnessNames, globalScope, force, dryRun) = ParseSkillTargetArgs(args, "install");

        var source = SkillInstaller.FindSkillSource()
            ?? throw new InvalidOperationException(
                "Skill files not found next to the nudge binary — reinstall the tool.");
        var targets = SkillInstaller.ResolveTargets(
            harnessNames, globalScope, SkillInstaller.HomeDir, Directory.GetCurrentDirectory());
        SkillInstaller.Install(source, targets, force, dryRun, Console.WriteLine);
        if (!dryRun)
            Console.WriteLine("Restart your harness session so it picks up the new skill.");
        return 0;
    }

    private static int RunSkillUninstall(string[] args)
    {
        var (harnessNames, globalScope, _, dryRun) = ParseSkillTargetArgs(args, "uninstall");
        var targets = SkillInstaller.ResolveTargets(
            harnessNames, globalScope, SkillInstaller.HomeDir, Directory.GetCurrentDirectory());
        SkillInstaller.Uninstall(targets, dryRun, Console.WriteLine);
        return 0;
    }

    /// <summary>
    /// Parses the target-selection flags shared by install and uninstall:
    /// --global/--project scope, --harness (repeatable, comma-separated),
    /// --all, plus --force/--dry-run for install.
    /// </summary>
    private static (IReadOnlyList<string> Harnesses, bool GlobalScope, bool Force, bool DryRun)
        ParseSkillTargetArgs(string[] args, string verb)
    {
        var harnessNames = new List<string>();
        var globalScope = true;
        var force = false;
        var dryRun = false;
        var all = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--global":
                    globalScope = true;
                    break;
                case "--project":
                    globalScope = false;
                    break;
                case "--all":
                    all = true;
                    break;
                case "--force":
                    force = true;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--harness":
                    if (i + 1 >= args.Length)
                        throw new CliUsageException($"nudge skill {verb} --harness expects a harness name.");
                    i++;
                    harnessNames.AddRange(args[i].Split(',',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
                default:
                    throw new CliUsageException($"Unknown argument: {args[i]}");
            }
        }

        IReadOnlyList<string> selected = all
            ? SkillInstaller.Harnesses.Select(h => h.Name).ToList()
            : harnessNames.Count > 0
                ? harnessNames
                : SkillInstaller.DefaultHarnesses;
        return (selected, globalScope, force, dryRun);
    }

    private const string SkillUsage = """
        nudge skill — install the nudge-ruleset-gen skill into AI coding harnesses.

        Usage:
          nudge skill install [--global|--project] [--harness <name>...] [--all] [--force] [--dry-run]
          nudge skill uninstall [--global|--project] [--harness <name>...] [--all] [--dry-run]
          nudge skill list          Show where the skill is currently installed
          nudge skill harnesses     Show known harnesses and their skill directories

        With no --harness, installs to the universal pair (~/.agents/skills and
        ~/.claude/skills), which every harness in the table reads except Claude
        Code — hence the second copy. --project installs into the current
        directory instead of your home directory. Restart the harness session
        after installing so it discovers the new skill.
        """;


    private static async Task<int> RunAsync(CliOptions options)
    {
        var rootDir = FindRootDir(options);
        var rulesDir = ResolveRulesDir(options, rootDir);

        string logText;
        bool buildFailed;
        string scopeLabel;

        if (options.FromStdin)
        {
            logText = await Console.In.ReadToEndAsync();
            buildFailed = false;
            scopeLabel = "stdin";
        }
        else
        {
            var sln = options.Solution ?? FindSolution(rootDir);
            scopeLabel = Path.GetFileName(sln);
            var (output, exitCode, timedOut) = await RunBuildAsync(sln, options);
            if (timedOut)
            {
                Console.Error.WriteLine("nudge failed: dotnet build timed out.");
                return 2;
            }
            logText = output;
            buildFailed = exitCode != 0;
        }

        var diagnostics = LogParser.Parse(logText)
            .OrderBy(d => d.RuleId)
            .ThenBy(d => d.File)
            .ThenBy(d => d.Line)
            .ToList();

        if (options.WriteBaseline != null)
        {
            Baseline.Write(options.WriteBaseline, diagnostics, rootDir);
            Console.WriteLine($"Wrote baseline with {diagnostics.Count} finding(s) to {options.WriteBaseline}");
            return 0;
        }

        var baseline = Baseline.Load(options.Baseline);
        var findings = diagnostics
            .Where(d => !Baseline.IsSnoozed(d, baseline, rootDir))
            .ToList();

        var catalog = RuleCatalog.WithInstalledRulesets(rulesDir);

        if (findings.Count == 0)
        {
            Console.Write(Reporter.RenderClean(scopeLabel));
            return 0;
        }

        var groups = findings
            .GroupBy(d => d.RuleId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Console.Write(Reporter.Render(groups, catalog, rootDir, scopeLabel, buildFailed));

        SuggestMissingRulesets(groups, catalog, rootDir);

        // Errors mean the build failed: analyzers may not have run to completion.
        return findings.Any(d => d.IsError) || buildFailed ? 2 : 1;
    }

    /// <summary>
    /// When findings fell back to generic guides and the project uses analyzers
    /// with available-but-uninstalled rulesets, suggest installing them.
    /// </summary>
    private static void SuggestMissingRulesets(
        List<IGrouping<string, BuildDiagnostic>> groups, RuleCatalog catalog, string rootDir)
    {
        var fallbackRules = groups
            .Select(g => g.Key)
            .Where(ruleId => catalog.UsesFallback(ruleId))
            .ToList();
        if (fallbackRules.Count == 0)
            return;

        var missing = AnalyzerDetector.MissingRulesets(rootDir);
        if (missing.Count == 0)
            return;

        Console.WriteLine();
        Console.WriteLine("Some findings used generic guides because their rulesets aren't installed:");
        foreach (var (analyzer, ruleset) in missing.DistinctBy(x => x.Ruleset))
            Console.WriteLine($"  {analyzer} → nudge ruleset add {ruleset}");
    }

    private static string FindRootDir(CliOptions options)
    {
        if (options.Solution != null)
            return Path.GetDirectoryName(Path.GetFullPath(options.Solution)) ?? Directory.GetCurrentDirectory();

        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return Directory.GetCurrentDirectory();
    }

    private static string FindSolution(string rootDir)
    {
        var found = Directory.GetFiles(rootDir, "*.sln").FirstOrDefault();
        if (found != null)
            return found;
        throw new InvalidOperationException($"No solution file found under {rootDir}. Pass --sln explicitly.");
    }

    private static string? ResolveRulesDir(CliOptions options, string rootDir)
    {
        var candidates = new[]
        {
            options.RulesDir,
            Path.Combine(Directory.GetCurrentDirectory(), "rules"),
            Path.Combine(rootDir, "rules"),
            Path.Combine(AppContext.BaseDirectory, "rules"),
        };
        return candidates.FirstOrDefault(d => d != null && Directory.Exists(d));
    }

    private static async Task<(string Output, int ExitCode, bool TimedOut)> RunBuildAsync(string sln, CliOptions options)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{sln}\" --nologo -v q {options.BuildArgs}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";

        using var process = new Process { StartInfo = psi };
        var output = new StringBuilder();
        var lockObj = new object();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) lock (lockObj) output.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) lock (lockObj) output.AppendLine(e.Data);
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not start 'dotnet build' — is the .NET SDK on PATH? ({ex.Message})");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var timeoutMs = options.TimeoutSeconds > 0 ? options.TimeoutSeconds * 1000 : Timeout.Infinite;
        var finished = await Task.Run(() => process.WaitForExit(timeoutMs));
        if (!finished)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            return (output.ToString(), -1, true);
        }

        // Drain any stragglers.
        await Task.Delay(200);
        return (output.ToString(), process.ExitCode, false);
    }
}
