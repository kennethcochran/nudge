using System.Diagnostics;
using System.Text;
using System.Text.Json;

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
        if (args.Length > 0 && args[0].Equals("explain", StringComparison.OrdinalIgnoreCase))
            return RunExplainCommand(args.Skip(1).ToArray());
        if (args.Length > 0 && args[0].Equals("init", StringComparison.OrdinalIgnoreCase))
            return RunInitCommand(args.Skip(1).ToArray());
        if (args.Length > 0 && args[0].Equals("coach", StringComparison.OrdinalIgnoreCase))
            return RunCoachCommand(args.Skip(1).ToArray());

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

    private const string ExplainUsage = """
        Usage: nudge explain <ruleId> [--rules <dir>]

        Prints the coaching guide (Why / Do this / AVOID) for one diagnostic
        rule. When no guide is recorded for the rule, prints the generic
        fallback guide and suggests rulesets that may cover it.
        """;

    /// <summary>
    /// The model-facing lookup: print the coaching guide for one rule ID.
    /// Exit 0 even for fallback guides (honest output, not a failure);
    /// exit 2 on usage errors.
    /// </summary>
    private const string InitUsage = """
        Usage: nudge init [--force] [--remove] [--console-logger]

        Installs the nudge MSBuild logger into this repo so every `dotnet build`
        prints coaching inline, next to each diagnostic it applies to:
          - copies Nudge.Logger.dll (and Nudge.Core.dll) into .nudge/
          - writes Directory.Build.rsp at the repo root loading the logger
          - arranges per-project ErrorLog SARIF in Directory.Build.props
            (.nudge/sarif/<project>.sarif), so the logger can also coach the
            Info-severity findings the build event stream never carries.
            An existing ErrorLog setting in Directory.Build.props is
            detected and left as is, never overwritten.

        With --console-logger, the logger additionally replaces the default
        console logger (/noconsolelogger): it prints every diagnostic itself,
        coaching inline, plus the end-of-build summary. A quieter, coaching-
        first output; not a byte-for-byte clone of the default logger.

        Idempotent: re-running refreshes the logger DLLs. Merges with an
        existing Directory.Build.rsp instead of overwriting it.
        `nudge init --remove` undoes the installation (keeps .nudge/baseline.txt).
        """;

    /// <summary>Marker comment identifying the lines `nudge init` manages in Directory.Build.rsp.</summary>
    internal const string InitMarker = "# Added by `nudge init`";

    /// <summary>
    /// The Directory.Build.rsp line that loads the logger. Both paths are
    /// quoted: MSBuild expands %MSBuildThisFileDirectory% before it splits
    /// response-file lines on unquoted whitespace, so an unquoted path breaks
    /// every build (MSB4017, logger assembly not found) in a repo whose path
    /// contains spaces.
    /// </summary>
    internal const string InitLoggerLine =
        "/logger:Nudge.Logger.NudgeLogger,\"%MSBuildThisFileDirectory%.nudge/Nudge.Logger.dll\"" +
        ";baseline=\"%MSBuildThisFileDirectory%.nudge/baseline.txt\"" +
        ";sarifdir=\"%MSBuildThisFileDirectory%.nudge/sarif\"";

    /// <summary>
    /// The logger registration for console mode (<c>nudge init
    /// --console-logger</c>): the default line plus the logger's
    /// <c>console=true</c> parameter. Written together with
    /// <see cref="InitNoConsoleLine"/> so nudge's logger is the build's only
    /// console output.
    /// </summary>
    internal const string InitConsoleLoggerLine = InitLoggerLine + ";console=true";

    /// <summary>
    /// The response-file switch that silences the default console logger,
    /// written only in console mode. Bare and unremarkable on its own, so
    /// init claims it as managed only in a file that carries an init
    /// installation (see <see cref="StripInitLines"/>).
    /// </summary>
    internal const string InitNoConsoleLine = "/noconsolelogger";

    /// <summary>
    /// True for lines `nudge init` owns in Directory.Build.rsp: its marker
    /// comment, or the logger registration it wrote. Lines that merely mention
    /// Nudge.Logger (a user's own comment, an unrelated registration) are not
    /// init's to strip or to count as an installation.
    /// </summary>
    internal static bool IsInitManagedLine(string line)
    {
        if (line.StartsWith(InitMarker, StringComparison.Ordinal))
            return true;
        var trimmed = line.TrimStart();
        return (trimmed.StartsWith("/logger:", StringComparison.OrdinalIgnoreCase)
             || trimmed.StartsWith("-logger:", StringComparison.OrdinalIgnoreCase))
            && trimmed.Contains("Nudge.Logger.NudgeLogger", StringComparison.Ordinal);
    }

    /// <summary>
    /// Removes every line `nudge init` owns from a Directory.Build.rsp. The
    /// console-mode <c>/noconsolelogger</c> line counts as init's only when
    /// the file carries an init installation — the switch is generic, and a
    /// user's own use of it in a repo without the logger is theirs to keep.
    /// </summary>
    internal static List<string> StripInitLines(IEnumerable<string> lines)
    {
        var list = lines.ToList();
        var isInitInstall = list.Any(IsInitManagedLine);
        return list.Where(l => !IsInitManagedLine(l)
            && !(isInitInstall
                 && l.Trim().Equals(InitNoConsoleLine, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <summary>
    /// The Label of the PropertyGroup `nudge init` manages in
    /// Directory.Build.props: the per-project ErrorLog arrangement. The
    /// logger's SARIF finish reads these files at build end to coach the
    /// note/none (Info) findings the build event stream never carries.
    /// </summary>
    internal const string ErrorLogPropsLabel = "nudge-init-errorlog";

    /// <summary>The comment line written directly above the managed ErrorLog group.</summary>
    internal const string ErrorLogPropsComment =
        "<!-- Added by nudge init: per-project ErrorLog SARIF for Info-level coaching. -->";

    /// <summary>
    /// The Directory.Build.props lines `nudge init` owns: a comment plus a
    /// labelled PropertyGroup setting ErrorLog to one SARIF file per
    /// project under .nudge/sarif/. $(MSBuildThisFileDirectory) is the
    /// props file's own directory — the repo root — and ends in a
    /// separator, so the concatenation is correct on any platform.
    /// </summary>
    internal static readonly string[] ErrorLogPropsBlockLines =
    [
        ErrorLogPropsComment,
        $"<PropertyGroup Label=\"{ErrorLogPropsLabel}\">",
        "  <ErrorLog>$(MSBuildThisFileDirectory).nudge/sarif/$(MSBuildProjectName).sarif,version=2.1</ErrorLog>",
        "</PropertyGroup>",
    ];

    /// <summary>True when the lines carry init's managed ErrorLog group.</summary>
    internal static bool HasErrorLogPropsBlock(IEnumerable<string> lines) =>
        lines.Any(l => l.Contains($"Label=\"{ErrorLogPropsLabel}\"", StringComparison.Ordinal));

    /// <summary>
    /// True when an ErrorLog setting exists outside init's managed group —
    /// the user's own arrangement, which init detects and respects rather
    /// than overwriting or shadowing it.
    /// </summary>
    internal static bool HasUnmanagedErrorLog(IEnumerable<string> lines) =>
        StripErrorLogPropsLines(lines).Any(
            l => l.Contains("<ErrorLog", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Removes the lines `nudge init` owns from a Directory.Build.props:
    /// its comment line and the labelled PropertyGroup (through its
    /// closing tag). Every other line passes through untouched.
    /// </summary>
    internal static List<string> StripErrorLogPropsLines(IEnumerable<string> lines)
    {
        var result = new List<string>();
        var inBlock = false;
        foreach (var line in lines)
        {
            if (line.Trim().Equals(ErrorLogPropsComment, StringComparison.Ordinal))
                continue;
            if (line.Contains($"Label=\"{ErrorLogPropsLabel}\"", StringComparison.Ordinal))
            {
                inBlock = true;
                continue;
            }
            if (inBlock)
            {
                if (line.Contains("</PropertyGroup>", StringComparison.Ordinal))
                    inBlock = false;
                continue;
            }
            result.Add(line);
        }
        return result;
    }

    /// <summary>
    /// True when nothing but an empty Project element (and whitespace or
    /// an XML declaration) remains — the props file exists only because
    /// init created it, so removal may delete it outright.
    /// </summary>
    internal static bool IsOnlyEmptyProject(IEnumerable<string> lines) =>
        lines.All(l =>
        {
            var t = l.Trim();
            return t.Length == 0
                || t.Equals("<Project>", StringComparison.Ordinal)
                || t.Equals("</Project>", StringComparison.Ordinal)
                || t.StartsWith("<?xml", StringComparison.Ordinal);
        });

    /// <summary>
    /// The on-ramp for build-time coaching: install the MSBuild logger into
    /// the repo. Explicit opt-in — an auto-loaded logger DLL is code execution
    /// on build, so this never happens without the user asking for it.
    /// </summary>
    private static int RunInitCommand(string[] args)
    {
        try
        {
            var remove = false;
            var force = false;
            var consoleLogger = false;
            foreach (var a in args)
            {
                if (a == "--remove") remove = true;
                else if (a == "--force") force = true;
                else if (a == "--console-logger") consoleLogger = true;
                else throw new CliUsageException($"Unknown argument: {a}");
            }

            var rootDir = FindRepoRoot();
            var nudgeDir = Path.Combine(rootDir, ".nudge");
            var rspPath = Path.Combine(rootDir, "Directory.Build.rsp");
            var propsPath = Path.Combine(rootDir, "Directory.Build.props");

            if (remove)
                return RemoveInit(nudgeDir, rspPath, propsPath);

            Directory.CreateDirectory(nudgeDir);

            // The logger payload ships inside the nudge tool package, next to
            // the tool's own binaries.
            var toolDir = AppContext.BaseDirectory;
            foreach (var dll in new[] { "Nudge.Logger.dll", "Nudge.Core.dll" })
            {
                var source = Path.Combine(toolDir, dll);
                if (!File.Exists(source))
                    throw new InvalidOperationException(
                        $"Logger payload not found: {source}. Reinstall nudge and try again.");
                File.Copy(source, Path.Combine(nudgeDir, dll), overwrite: true);
            }

            var lines = File.Exists(rspPath)
                ? File.ReadAllLines(rspPath).ToList()
                : new List<string>();
            var alreadyInstalled = lines.Any(IsInitManagedLine);

            if (!alreadyInstalled || force)
            {
                // Strip any previous init-managed lines before writing fresh ones.
                lines = StripInitLines(lines);
                if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                    lines.Add(string.Empty);
                lines.Add(InitMarker + " — loads the nudge MSBuild logger for coaching diagnostics. Remove with `nudge init --remove`.");
                if (consoleLogger)
                    lines.Add(InitNoConsoleLine);
                lines.Add(consoleLogger ? InitConsoleLoggerLine : InitLoggerLine);
                File.WriteAllLines(rspPath, lines);
            }

            var propsStatus = ArrangeErrorLogProps(propsPath, nudgeDir, force);

            Console.WriteLine($"nudge init: logger installed.");
            Console.WriteLine($"  logger:  {Path.Combine(nudgeDir, "Nudge.Logger.dll")}");
            Console.WriteLine($"  rsp:     {rspPath}");
            Console.WriteLine($"  props:   {propsPath} — {propsStatus}");
            Console.WriteLine(consoleLogger
                ? $"  Every `dotnet build` under {rootDir} now prints diagnostics through the nudge logger, coaching inline."
                : $"  Every `dotnet build` under {rootDir} now gets coaching inline, next to each diagnostic.");
            return 0;
        }
        catch (CliUsageException ex)
        {
            if (ex.Message != null)
                Console.Error.WriteLine($"Error: {ex.Message}\n");
            Console.Error.WriteLine(InitUsage);
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"nudge init failed: {ex.Message}");
            return 2;
        }
    }

    /// <summary>
    /// Arranges the per-project ErrorLog SARIF in Directory.Build.props.
    /// Returns a one-line status for init's report. A user's own ErrorLog
    /// setting in the props is detected and respected: the arrangement is
    /// skipped, because replacing or shadowing their setting is not
    /// init's to do. Idempotent: an existing managed block is left alone
    /// unless forced, in which case it is rewritten fresh.
    /// </summary>
    private static string ArrangeErrorLogProps(string propsPath, string nudgeDir, bool force)
    {
        var lines = File.Exists(propsPath)
            ? File.ReadAllLines(propsPath).ToList()
            : new List<string>();

        if (HasUnmanagedErrorLog(lines))
            return "an ErrorLog setting already exists there and was left as is";

        if (HasErrorLogPropsBlock(lines) && !force)
            return "already arranged";

        lines = StripErrorLogPropsLines(lines);
        Directory.CreateDirectory(Path.Combine(nudgeDir, "sarif"));
        if (lines.Count == 0)
        {
            lines.Add("<Project>");
            lines.AddRange(ErrorLogPropsBlockLines);
            lines.Add("</Project>");
        }
        else
        {
            var closeIndex = lines.FindLastIndex(
                l => l.Contains("</Project>", StringComparison.Ordinal));
            if (closeIndex < 0)
                lines.AddRange(ErrorLogPropsBlockLines);
            else
                lines.InsertRange(closeIndex, ErrorLogPropsBlockLines);
        }
        File.WriteAllLines(propsPath, lines);
        return "arranged (.nudge/sarif/<project>.sarif, one per project)";
    }

    private static int RemoveInit(string nudgeDir, string rspPath, string propsPath)
    {
        var removed = new List<string>();

        // Unregister before deleting the payload: if removal is interrupted
        // after this point, the repo is merely un-coached; deleting the DLLs
        // first would leave a registered logger that no longer exists, which
        // fails every build at logger-load time.
        if (File.Exists(rspPath))
        {
            var remaining = StripInitLines(File.ReadAllLines(rspPath));
            if (remaining.All(string.IsNullOrWhiteSpace))
            {
                File.Delete(rspPath);
                removed.Add(rspPath + " (deleted — only contained nudge lines)");
            }
            else
            {
                File.WriteAllLines(rspPath, remaining);
                removed.Add(rspPath + " (nudge lines removed)");
            }
        }

        // Undo the ErrorLog arrangement the same way: strip only init's
        // block, and delete the props file only when nothing of the
        // user's would remain in it.
        if (File.Exists(propsPath))
        {
            var original = File.ReadAllLines(propsPath);
            var remaining = StripErrorLogPropsLines(original);
            if (remaining.Count != original.Length)
            {
                if (IsOnlyEmptyProject(remaining))
                {
                    File.Delete(propsPath);
                    removed.Add(propsPath + " (deleted — only contained the nudge ErrorLog arrangement)");
                }
                else
                {
                    File.WriteAllLines(propsPath, remaining);
                    removed.Add(propsPath + " (ErrorLog arrangement removed)");
                }
            }
        }

        // Generated ErrorLog files are build output, not user data.
        var sarifDir = Path.Combine(nudgeDir, "sarif");
        if (Directory.Exists(sarifDir))
        {
            foreach (var file in Directory.EnumerateFiles(sarifDir, "*.sarif"))
                File.Delete(file);
            if (!Directory.EnumerateFileSystemEntries(sarifDir).Any())
            {
                Directory.Delete(sarifDir);
                removed.Add(sarifDir + " (generated SARIF removed)");
            }
        }

        foreach (var dll in new[] { "Nudge.Logger.dll", "Nudge.Core.dll" })
        {
            var path = Path.Combine(nudgeDir, dll);
            if (File.Exists(path))
            {
                File.Delete(path);
                removed.Add(path);
            }
        }

        // Baseline is user data; never delete it here.
        if (removed.Count == 0)
            Console.WriteLine("nudge init: nothing to remove.");
        else
        {
            Console.WriteLine("nudge init: removed:");
            foreach (var r in removed)
                Console.WriteLine($"  {r}");
        }
        return 0;
    }

    private const string CoachUsage = """
        Usage: nudge coach <file.sarif> [--rules <dir>] [--baseline <file>]

        Reads a SARIF 2.1.0 log — a compiler ErrorLog, or native SARIF from a
        standalone tool (InspectCode, CodeQL, Semgrep, …) — and prints the
        grouped coached report to stdout. This is the channel that carries
        Info-severity (note) and none-level findings, which never reach the
        build event stream. Rules without a coaching guide surface as
        uncoached findings; they are never dropped.

        Honours the baseline: --baseline <file> when given, otherwise the
        repo's .nudge/baseline.txt when one exists.

        Exit codes (the established contract): 0 clean, 1 findings,
        2 failure — an error-level finding, an unreadable or invalid SARIF
        file, or invalid arguments.
        """;

    /// <summary>
    /// The post-hoc SARIF front end: coach findings from a SARIF log file,
    /// rendered to stdout (never to a file an agent must go find).
    /// </summary>
    private static int RunCoachCommand(string[] args)
    {
        try
        {
            string? sarifPath = null;
            string? rulesDirOption = null;
            string? baselineOption = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--rules" && i + 1 < args.Length)
                    rulesDirOption = args[++i];
                else if (args[i] == "--baseline" && i + 1 < args.Length)
                    baselineOption = args[++i];
                else if (!args[i].StartsWith("-") && sarifPath == null)
                    sarifPath = args[i];
                else
                    throw new CliUsageException($"Unknown argument: {args[i]}");
            }
            if (sarifPath == null)
                throw new CliUsageException("nudge coach requires a SARIF file path.");
            if (!File.Exists(sarifPath))
                throw new CliUsageException($"SARIF file not found: {sarifPath}");

            var rootDir = FindRepoRoot();
            var rulesDir = ResolveRulesDir(rulesDirOption, rootDir);
            var catalog = RuleCatalog.WithInstalledRulesets(rulesDir);
            var baseline = Baseline.Load(baselineOption
                ?? Path.Combine(rootDir, ".nudge", "baseline.txt"));

            var diagnostics = SarifReader.ReadFile(
                sarifPath, baseDirectory: Path.GetDirectoryName(Path.GetFullPath(sarifPath)));
            var result = CoachingEngine.Generate(
                diagnostics, catalog, baseline, rootDir,
                Path.GetFileName(sarifPath), buildFailed: false);
            Console.Write(result.Report);

            SuggestMissingRulesets(result.FallbackRuleIds, rootDir);

            // Established contract, same as the build path: error-level
            // findings are a failure (2), lesser findings are 1, clean is 0.
            return result.HasErrors ? 2 : result.FindingCount > 0 ? 1 : 0;
        }
        catch (CliUsageException ex)
        {
            if (ex.Message != null)
                Console.Error.WriteLine($"Error: {ex.Message}\n");
            Console.Error.WriteLine(CoachUsage);
            return 2;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or IOException)
        {
            Console.Error.WriteLine($"nudge coach failed: {ex.Message}");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"nudge coach failed: {ex.Message}");
            return 2;
        }
    }

    private static int RunExplainCommand(string[] args)
    {
        try
        {
            string? ruleId = null;
            string? rulesDirOption = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--rules" && i + 1 < args.Length)
                    rulesDirOption = args[++i];
                else if (!args[i].StartsWith("-") && ruleId == null)
                    ruleId = args[i];
                else
                    throw new CliUsageException($"Unknown argument: {args[i]}");
            }
            if (ruleId == null)
                throw new CliUsageException("nudge explain requires a rule ID.");

            var rootDir = FindRepoRoot();
            var rulesDir = ResolveRulesDir(rulesDirOption, rootDir);
            var catalog = RuleCatalog.WithInstalledRulesets(rulesDir);

            var guide = catalog.Resolve(ruleId, rawMessage: string.Empty);
            Console.Write(Reporter.RenderGuide(guide));

            if (catalog.UsesFallback(ruleId))
            {
                var missing = AnalyzerDetector.MissingRulesets(rootDir);
                if (missing.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("No specific guide is recorded for this rule. Rulesets that may cover it:");
                    foreach (var (analyzer, ruleset) in missing.DistinctBy(x => x.Ruleset))
                        Console.WriteLine($"  {analyzer} → nudge ruleset add {ruleset}");
                }
            }
            return 0;
        }
        catch (CliUsageException ex)
        {
            if (ex.Message != null)
                Console.Error.WriteLine($"Error: {ex.Message}\n");
            Console.Error.WriteLine(ExplainUsage);
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"nudge explain failed: {ex.Message}");
            return 2;
        }
    }

    /// <summary>
    /// Handles `nudge skill ...`: install the nudge skills (nudge-ruleset-gen,
    /// nudge-coaching) into AI coding harnesses. The skills ship and install
    /// as a bundle — install and uninstall always cover every shipped skill.
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
                        Console.WriteLine("No skills installed anywhere. Try: nudge skill install");
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

        foreach (var skill in SkillInstaller.KnownSkills)
        {
            var source = SkillInstaller.FindSkillSource(skill)
                ?? throw new InvalidOperationException(
                    $"Skill files for '{skill}' not found next to the nudge binary — reinstall the tool.");
            var targets = SkillInstaller.ResolveTargets(
                harnessNames, globalScope, SkillInstaller.HomeDir, Directory.GetCurrentDirectory(), skill);
            Console.WriteLine($"{skill}:");
            SkillInstaller.Install(source, targets, force, dryRun, Console.WriteLine);
        }
        if (!dryRun)
            Console.WriteLine("Restart your harness session so it picks up the new skills.");
        return 0;
    }

    private static int RunSkillUninstall(string[] args)
    {
        var (harnessNames, globalScope, _, dryRun) = ParseSkillTargetArgs(args, "uninstall");
        foreach (var skill in SkillInstaller.KnownSkills)
        {
            var targets = SkillInstaller.ResolveTargets(
                harnessNames, globalScope, SkillInstaller.HomeDir, Directory.GetCurrentDirectory(), skill);
            Console.WriteLine($"{skill}:");
            SkillInstaller.Uninstall(targets, dryRun, Console.WriteLine);
        }
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
        nudge skill — install the nudge skills into AI coding harnesses.

        Skills:
          nudge-coaching     The build → fix → rebuild loop for repos using nudge (coaching prints inline)
          nudge-ruleset-gen  Generate coaching-guide rulesets for uncovered analyzers

        Usage:
          nudge skill install [--global|--project] [--harness <name>...] [--all] [--force] [--dry-run]
          nudge skill uninstall [--global|--project] [--harness <name>...] [--all] [--dry-run]
          nudge skill list          Show where the skills are currently installed
          nudge skill harnesses     Show known harnesses and their skill directories

        Install and uninstall always cover both shipped skills. With no
        --harness, installs to the universal pair (~/.agents/skills and
        ~/.claude/skills), which every harness in the table reads except Claude
        Code — hence the second copy. --project installs into the current
        directory instead of your home directory. Restart the harness session
        after installing so it discovers the new skills.
        """;


    private static async Task<int> RunAsync(CliOptions options)
    {
        var rootDir = FindRootDir(options);
        var rulesDir = ResolveRulesDir(options.RulesDir, rootDir);

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

        var diagnostics = LogParser.Parse(logText).ToList();

        if (options.WriteBaseline != null)
        {
            Baseline.Write(options.WriteBaseline, diagnostics, rootDir);
            Console.WriteLine($"Wrote baseline with {diagnostics.Count} finding(s) to {options.WriteBaseline}");
            return 0;
        }

        var baseline = Baseline.Load(options.Baseline);
        var catalog = RuleCatalog.WithInstalledRulesets(rulesDir);
        var result = CoachingEngine.Generate(diagnostics, catalog, baseline, rootDir, scopeLabel, buildFailed);
        Console.Write(result.Report);

        SuggestMissingRulesets(result.FallbackRuleIds, rootDir);

        // Errors mean the build failed: analyzers may not have run to completion.
        return result.HasErrors || buildFailed ? 2 : result.FindingCount > 0 ? 1 : 0;
    }

    /// <summary>
    /// When findings fell back to generic guides and the project uses analyzers
    /// with available-but-uninstalled rulesets, suggest installing them.
    /// </summary>
    private static void SuggestMissingRulesets(
        IReadOnlyList<string> fallbackRuleIds, string rootDir)
    {
        if (fallbackRuleIds.Count == 0)
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

        return FindRepoRoot();
    }

    /// <summary>Nearest ancestor (or self) containing a .git directory.</summary>
    private static string FindRepoRoot()
    {
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

    private static string? ResolveRulesDir(string? rulesDirOption, string rootDir)
    {
        var candidates = new[]
        {
            rulesDirOption,
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
