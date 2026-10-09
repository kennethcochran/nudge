namespace Nudge;

/// <summary>
/// Command-line options for the habit shim.
/// </summary>
internal sealed class CliOptions
{
    /// <summary>Solution or project file to build. Defaults to the *.sln discovered upward from cwd.</summary>
    public string? Solution { get; private set; }

    /// <summary>Read MSBuild-format diagnostics from stdin instead of running a build.</summary>
    public bool FromStdin { get; private set; }

    /// <summary>Extra arguments appended to the <c>dotnet build</c> invocation.</summary>
    public string BuildArgs { get; private set; } = string.Empty;

    /// <summary>Rule catalog directory containing per-rule coaching guides.</summary>
    public string? RulesDir { get; private set; }

    /// <summary>Baseline (snooze) file; findings listed here are skipped.</summary>
    public string? Baseline { get; private set; }

    /// <summary>Write current findings to this path as a baseline file, then exit 0.</summary>
    public string? WriteBaseline { get; private set; }

    /// <summary>Maximum seconds to wait for the build. 0 means no timeout.</summary>
    public int TimeoutSeconds { get; private set; }

    /// <summary>
    /// Parses command-line arguments. Unknown arguments cause a usage error.
    /// </summary>
    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--sln":
                case "--solution":
                    options.Solution = TakeValue(args, ref i, args[i]);
                    break;
                case "--stdin":
                    options.FromStdin = true;
                    break;
                case "--build-args":
                    options.BuildArgs = TakeValue(args, ref i, args[i]);
                    break;
                case "--rules":
                    options.RulesDir = TakeValue(args, ref i, args[i]);
                    break;
                case "--baseline":
                    options.Baseline = TakeValue(args, ref i, args[i]);
                    break;
                case "--write-baseline":
                    options.WriteBaseline = TakeValue(args, ref i, args[i]);
                    break;
                case "--timeout":
                    var raw = TakeValue(args, ref i, args[i]);
                    if (!int.TryParse(raw, out var seconds) || seconds < 0)
                        throw new CliUsageException("--timeout expects a non-negative integer number of seconds.");
                    options.TimeoutSeconds = seconds;
                    break;
                case "-h":
                case "--help":
                    throw new CliUsageException(null);
                default:
                    throw new CliUsageException($"Unknown argument: {args[i]}");
            }
        }

        if (options.FromStdin && options.Solution != null)
            throw new CliUsageException("--stdin and --solution are mutually exclusive.");

        return options;
    }

    private static string TakeValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
            throw new CliUsageException($"{flag} expects a value.");
        i++;
        return args[i];
    }

    /// <summary>Usage text printed when argument parsing fails or --help is given.</summary>
    public static string Usage => """
        nudge — turn raw Roslyn/SonarQube build diagnostics into coaching guides for AI agents.

        Usage:
          nudge [--sln <path>] [--build-args "<args>"] [--rules <dir>]
                     [--baseline <file> | --write-baseline <file>] [--timeout <seconds>]
          dotnet build ... | nudge --stdin [--rules <dir>] [--baseline <file>]
          nudge ruleset add <name> | nudge ruleset list | nudge ruleset remove <name> | nudge ruleset detect
          nudge skill install [--global|--project] [--harness <name>...] [--all]
          nudge explain <ruleId> [--rules <dir>]

        Rulesets are downloadable coaching-guide packs (e.g. nudge ruleset add sonar).
        Run `nudge ruleset` for details. `nudge skill install` puts the
        ruleset-generator skill into your AI coding harnesses.

        Exit codes:
          0  clean — no findings
          1  findings — the branch has diagnostics to fix
          2  tool failure — the build could not run, failed with errors, or arguments were invalid
        """;
}

/// <summary>Thrown when command-line usage is invalid; carries the message to display.</summary>
internal sealed class CliUsageException : Exception
{
    public CliUsageException(string? message) : base(message) { }
}
