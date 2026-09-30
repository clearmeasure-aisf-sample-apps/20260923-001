using System.Text.RegularExpressions;
using Shouldly;

namespace ClearMeasure.Bootcamp.UnitTests.BuildGates;

/// <summary>
/// Guards the repository against personal-token flows: copying an operator's
/// <c>gh</c> token into containers, storing credentials in plaintext, or
/// committing PAT-shaped literals. The retired AI Factory executor (audit row A4)
/// did all three. Patterns are built from fragments so this file never matches itself.
/// </summary>
[TestFixture]
public class PersonalTokenGuardTests
{
    private static readonly string[] SkippedDirectories = [".git", "bin", "obj", "node_modules"];

    private static readonly string[] TextExtensions =
    [
        ".ps1", ".sh", ".yml", ".yaml", ".md", ".cs", ".csx", ".json", ".txt", ".dockerfile", ".env"
    ];

    // Owned by other audit rows: the 'gh auth token' last-resort fallback of the GitHub App tooling (rows B1/B2,
    // tracked in #68) in .claude/, scripts/github/GitHubAppAuth.ps1 and its tests; and this test file.
    private static readonly string[] AllowlistedPrefixes =
    [
        ".claude/",
        "scripts/github/GitHubAppAuth.ps1",
        "src/UnitTests/BuildGates/GitHubApp/",
        "src/UnitTests/BuildGates/PersonalTokenGuardTests.cs"
    ];

    private static readonly (string Name, Func<string, bool> IsMatch)[] ForbiddenPatterns =
    [
        ("gh auth " + "token", text => text.Contains("gh auth " + "token", StringComparison.Ordinal)),
        ("gh auth login --with" + "-token",
            text => text.Contains("gh auth login --with" + "-token", StringComparison.Ordinal)),
        ("plaintext credential store (credential.helper " + "store / .git-" + "credentials)",
            text => text.Contains("credential.helper " + "store", StringComparison.Ordinal)
                    || text.Contains(".git-" + "credentials", StringComparison.Ordinal)),
        ("x-access-" + "token with a credential store",
            text => text.Contains("x-access-" + "token", StringComparison.Ordinal)
                    && (text.Contains("credential.helper", StringComparison.Ordinal)
                        || text.Contains(".git-" + "credentials", StringComparison.Ordinal))),
        ("classic PAT literal (gh" + "p_...)", text => Regex.IsMatch(text, "gh" + "p_[A-Za-z0-9]{20,}")),
        ("fine-grained PAT literal (github_" + "pat_...)",
            text => Regex.IsMatch(text, "github_" + "pat_[A-Za-z0-9_]{20,}"))
    ];

    [Test]
    public void AiFactoryExecutorDirectory_WhenRepoRootInspected_DoesNotExist()
    {
        var root = FindRepoRoot();

        Directory.Exists(Path.Join(root, ".bob", "skills", "ai-factory-executor")).ShouldBeFalse();
    }

    [Test]
    public void RepositoryTree_WhenScanned_ContainsNoPersonalTokenFlow()
    {
        var findings = Scan(FindRepoRoot());

        findings.ShouldBeEmpty(string.Join(Environment.NewLine, findings));
    }

    [Test]
    public void Scan_WhenTreeContainsEachForbiddenPattern_ReportsEveryPattern()
    {
        var root = CreateTempTree();
        try
        {
            WriteFile(root, Path.Join("scripts", "a.ps1"), "$t = " + "gh auth " + "token");
            WriteFile(root, Path.Join("scripts", "b.sh"), "echo x | gh auth login --with" + "-token");
            WriteFile(root, Path.Join("scripts", "c.yml"), "run: git config credential.helper " + "store");
            WriteFile(root, Path.Join("scripts", "d.md"),
                "url https://x-access-" + "token:abc@github.com and .git-" + "credentials");
            WriteFile(root, Path.Join("scripts", "e.json"), "{\"t\":\"gh" + "p_" + new string('a', 24) + "\"}");
            WriteFile(root, Path.Join("scripts", "f.txt"), "github_" + "pat_" + new string('B', 24));
            WriteFile(root, Path.Join("scripts", "Dockerfile"), "ENV T=gh" + "p_" + new string('c', 24));

            var findings = Scan(root);

            findings.Any(f => f.Contains("a.ps1")).ShouldBeTrue();
            findings.Any(f => f.Contains("b.sh")).ShouldBeTrue();
            findings.Any(f => f.Contains("c.yml")).ShouldBeTrue();
            findings.Any(f => f.Contains("d.md") && f.Contains("x-access-")).ShouldBeTrue();
            findings.Any(f => f.Contains("e.json")).ShouldBeTrue();
            findings.Any(f => f.Contains("f.txt")).ShouldBeTrue();
            findings.Any(f => f.Contains("Dockerfile")).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Scan_WhenForbiddenTextIsInAllowlistedSkippedOrBinaryPaths_ReportsNothing()
    {
        var root = CreateTempTree();
        try
        {
            var bad = "$t = " + "gh auth " + "token";
            WriteFile(root, Path.Join(".claude", "skills", "board.ps1"), bad);
            WriteFile(root, Path.Join("src", "UnitTests", "BuildGates", "PersonalTokenGuardTests.cs"), bad);
            WriteFile(root, Path.Join("scripts", "github", "GitHubAppAuth.ps1"), bad);
            WriteFile(root, Path.Join("src", "UnitTests", "BuildGates", "GitHubApp", "GitHubScriptHost.cs"), bad);
            WriteFile(root, Path.Join("node_modules", "pkg", "x.md"), bad);
            WriteFile(root, Path.Join("bin", "x.json"), bad);
            WriteFile(root, Path.Join("images", "logo.png"), bad);
            WriteFile(root, Path.Join("docs", "clean.md"), "x-access-" + "token is fine alone; so is gh auth status");

            Scan(root).ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static List<string> Scan(string root)
    {
        var findings = new List<string>();
        foreach (var file in EnumerateTextFiles(root))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (AllowlistedPrefixes.Any(p => relative.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            findings.AddRange(ForbiddenPatterns
                .Where(p => p.IsMatch(text))
                .Select(p => $"{relative}: {p.Name}"));
        }

        return findings;
    }

    private static IEnumerable<string> EnumerateTextFiles(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory).Where(IsTextFile))
        {
            yield return file;
        }

        foreach (var sub in Directory.EnumerateDirectories(directory))
        {
            if (SkippedDirectories.Contains(Path.GetFileName(sub), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var file in EnumerateTextFiles(sub))
            {
                yield return file;
            }
        }
    }

    private static bool IsTextFile(string path)
    {
        var name = Path.GetFileName(path);
        return TextExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase)
               || name.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith(".env", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateTempTree()
    {
        var root = Path.Join(Path.GetTempPath(), "personal-token-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void WriteFile(string root, string relativePath, string content)
    {
        var full = Path.Join(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Join(dir.FullName, "src", "ChurchBulletin.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found from test directory.");
    }
}
