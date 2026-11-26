using System.Text.RegularExpressions;
using Shouldly;

namespace Web.API.IntegrationTests.Architecture;

/// <summary>
/// Source-level organization rules (CLAUDE.md §12 "DTOs e records em pastas próprias").
/// They scan the .cs files under <c>src/</c> instead of loading assemblies, so they cover every
/// project (Auth.*, Web.Blazor, Gateway...) without this test project referencing them.
/// </summary>
public sealed partial class CodeOrganizationTests
{
    // Layers where records are domain building blocks (value objects, Error), not DTOs.
    private static readonly string[] ExcludedProjects = ["Domain", "Auth.Domain", "SharedKernel"];

    // Folders allowed to hold DTO-like records.
    private static readonly HashSet<string> DtoFolders = new(StringComparer.Ordinal)
    {
        "Dtos",      // Application / Auth.Application / entrypoint-internal DTOs
        "Requests",  // Web.API endpoint request bodies
        "Contracts", // Web.Blazor typed-client contracts mirrored from the Gateway
        "Events",    // Message contracts shared by publishers and consumers
    };

    [GeneratedRegex(
        @"^(?<indent>[ \t]*)(?:(?:public|internal|private|protected|file|sealed|abstract|partial|readonly)\s+)*record\s+(?:struct\s+|class\s+)?(?<name>\w+)",
        RegexOptions.Multiline)]
    private static partial Regex RecordDeclaration();

    [Fact]
    public void Records_Should_BeDeclaredOnePerFile_WithMatchingFileName()
    {
        var violations = SourceFiles()
            .Select(file => (file, records: Records(file).Where(r => !r.Nested).ToList()))
            .Where(x => x.records.Count > 1
                        || (x.records.Count == 1
                            && x.records[0].Name != Path.GetFileNameWithoutExtension(x.file)))
            .Select(x => $"{Relative(x.file)}: {string.Join(", ", x.records.Select(r => r.Name))}")
            .ToList();

        violations.ShouldBeEmpty(Explain("Um record por arquivo, com o mesmo nome do arquivo", violations));
    }

    [Fact]
    public void Records_Should_NotBeNested()
    {
        var violations = SourceFiles()
            .SelectMany(file => Records(file).Where(r => r.Nested).Select(r => $"{Relative(file)}: {r.Name}"))
            .ToList();

        violations.ShouldBeEmpty(Explain("Records aninhados (ex.: Request dentro do endpoint) devem ir para a pasta Requests/ ou Dtos/", violations));
    }

    [Fact]
    public void DtoRecords_Should_LiveInDedicatedFolders()
    {
        var violations = SourceFiles()
            .Where(file => Records(file).Count > 0)
            .Where(file => !IsCommandOrQuery(file))
            .Where(file => !DtoFolders.Contains(Path.GetFileName(Path.GetDirectoryName(file)!)))
            .Select(Relative)
            .ToList();

        violations.ShouldBeEmpty(Explain(
            $"Records que não são Command/Query devem ficar em uma pasta {string.Join("/", DtoFolders)}",
            violations));
    }

    private static bool IsCommandOrQuery(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        return name.EndsWith("Command", StringComparison.Ordinal) || name.EndsWith("Query", StringComparison.Ordinal);
    }

    private static List<(string Name, bool Nested)> Records(string file) =>
        RecordDeclaration().Matches(File.ReadAllText(file))
            .Select(m => (m.Groups["name"].Value, m.Groups["indent"].Length > 0))
            .ToList();

    private static IEnumerable<string> SourceFiles()
    {
        var src = Path.Combine(RepoRoot, "src");
        return Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f =>
            {
                var parts = Path.GetRelativePath(src, f).Split(Path.DirectorySeparatorChar);
                return !ExcludedProjects.Contains(parts[0])
                       && !parts.Any(p => p is "bin" or "obj" or "Migrations");
            });
    }

    private static string Relative(string file) => Path.GetRelativePath(RepoRoot, file);

    private static string Explain(string rule, List<string> violations) =>
        $"{rule}. Violações:{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", violations)}";

    private static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BaseProjectScaffold.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("BaseProjectScaffold.sln não encontrado acima do diretório de testes.");
    }
}
