// File-based C# app (requires .NET 10+): dotnet run extract-descriptors.cs -- /path/to/Analyzer.dll
// Loads an analyzer DLL, finds all DiagnosticAnalyzer subclasses, and dumps
// their SupportedDiagnostics as JSON. Read-only: never executes analyzer actions.

using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: dotnet run extract-descriptors.cs -- /path/to/Analyzer.dll [probeDir]");
    Console.Error.WriteLine("  probeDir: extra directory to search for dependencies (e.g. Microsoft.CodeAnalysis.dll)");
    return 2;
}

var dllPath = Path.GetFullPath(args[0]);
if (!File.Exists(dllPath))
{
    Console.Error.WriteLine($"Not found: {dllPath}");
    return 2;
}

// Isolated load context that resolves dependencies from the DLL's directory
// plus an optional extra probing directory.
var probeDirs = new List<string> { Path.GetDirectoryName(dllPath)! };
if (args.Length > 1 && Directory.Exists(args[1]))
    probeDirs.Add(Path.GetFullPath(args[1]));
var alc = new AnalyzerLoadContext(probeDirs);

// Proactively load Microsoft.CodeAnalysis so the DiagnosticAnalyzer base type
// resolves (dependency loads are lazy and nothing else triggers them).
try { alc.LoadFromAssemblyName(new AssemblyName("Microsoft.CodeAnalysis")); }
catch (Exception ex)
{
    Console.Error.WriteLine($"Could not load Microsoft.CodeAnalysis from probing directories: {ex.Message.Split('\n')[0]}");
    Console.Error.WriteLine("Tip: pass the Roslyn bincore dir, e.g. <sdk>/Roslyn/bincore, as the second argument.");
    return 2;
}
Assembly assembly;
try
{
    assembly = alc.LoadFromAssemblyPath(dllPath);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to load {dllPath}: {ex.Message}");
    return 2;
}

// Find Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer without a hard reference.
var analyzerBase = FindType(assembly, "Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer")
    ?? FindTypeFromLoaded("Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer");
if (analyzerBase == null)
{
    Console.Error.WriteLine("Could not resolve DiagnosticAnalyzer base type. Is Microsoft.CodeAnalysis available next to the DLL?");
    return 2;
}

var seen = new HashSet<string>();
var descriptors = new List<Dictionary<string, string?>>();

foreach (var type in GetLoadableTypes(assembly))
{
    if (type.IsAbstract || !analyzerBase.IsAssignableFrom(type))
        continue;

    object? analyzer;
    try { analyzer = Activator.CreateInstance(type); }
    catch { continue; } // Skip analyzers that need constructor args.

    var supportedProp = analyzerBase.GetProperty("SupportedDiagnostics");
    var diagnostics = supportedProp?.GetValue(analyzer) as System.Collections.IEnumerable;
    if (diagnostics == null)
        continue;

    foreach (var d in diagnostics)
    {
        var id = GetStringProp(d, "Id");
        if (id == null || !seen.Add(id))
            continue;
        descriptors.Add(new Dictionary<string, string?>
        {
            ["id"] = id,
            ["title"] = GetStringProp(d, "Title"),
            ["description"] = GetStringProp(d, "Description"),
            ["severity"] = GetEnumName(d, "Severity"),
            ["category"] = GetStringProp(d, "Category"),
            ["isEnabledByDefault"] = GetBoolProp(d, "IsEnabledByDefault")?.ToString(),
        });
    }
}

var sorted = descriptors.OrderBy(d => d["id"]).ToList();
var sb = new System.Text.StringBuilder();
sb.AppendLine("[");
for (int i = 0; i < sorted.Count; i++)
{
    var d = sorted[i];
    sb.Append("  {");
    sb.Append(string.Join(", ", d.Select(kv => $"\"{kv.Key}\": {(kv.Value == null ? "null" : JsonEscape(kv.Value))}")));
    sb.Append("}");
    sb.AppendLine(i < sorted.Count - 1 ? "," : "");
}
sb.AppendLine("]");
Console.WriteLine(sb.ToString());
return 0;

static string JsonEscape(string s) =>
    "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") + "\"";

static Type? FindType(Assembly assembly, string fullName)
{
    try { return assembly.GetType(fullName); }
    catch { return null; }
}

static Type? FindTypeFromLoaded(string fullName)
{
    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
    {
        var t = FindType(asm, fullName);
        if (t != null) return t;
    }
    return null;
}

static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
{
    try { return assembly.GetTypes(); }
    catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null)!; }
}

static string? GetStringProp(object obj, string name)
{
    var prop = obj.GetType().GetProperty(name);
    var val = prop?.GetValue(obj);
    // Title/Description are LocalizableString; ToString() gives the localized value.
    return val?.ToString();
}

static string? GetEnumName(object obj, string name)
{
    var prop = obj.GetType().GetProperty(name);
    return prop?.GetValue(obj)?.ToString();
}

static bool? GetBoolProp(object obj, string name)
{
    var prop = obj.GetType().GetProperty(name);
    return prop?.GetValue(obj) as bool?;
}

sealed class AnalyzerLoadContext : AssemblyLoadContext
{
    private readonly List<string> _dirs;
    public AnalyzerLoadContext(List<string> dirs) => _dirs = dirs;
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Prefer already-loaded, then probe the given directories.
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == assemblyName.Name);
        if (loaded != null) return loaded;
        foreach (var dir in _dirs)
        {
            var candidate = Path.Combine(dir, assemblyName.Name + ".dll");
            if (File.Exists(candidate)) return LoadFromAssemblyPath(candidate);
        }
        return null;
    }
}
