using Shouldly;

namespace ClearMeasure.Bootcamp.UnitTests.BuildGates;

[TestFixture]
public class NuGetCacheLocationTests
{
    [Test]
    public void ShouldKeepExistingNuGetCacheLocation_WhenBuildScriptIsRead()
    {
        var init = ReadInitFunction();

        init.ShouldContain("-and [string]::IsNullOrEmpty($env:NUGET_PACKAGES)");
    }

    [Test]
    public void ShouldPinNuGetCacheOnlyOutsideGitHubActions_WhenBuildScriptIsRead()
    {
        var init = ReadInitFunction();

        init.ShouldContain("if (-not (Test-IsGitHubActions) -and");
        init.ShouldContain("$env:NUGET_PACKAGES = \"/tmp/nuget-packages\"");
    }

    private static string ReadInitFunction()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "build.ps1"));
        var initStart = source.IndexOf("Function Init", StringComparison.Ordinal);
        initStart.ShouldBeGreaterThan(-1);
        var initEnd = source.IndexOf("\nFunction ", initStart + 1, StringComparison.Ordinal);
        initEnd.ShouldBeGreaterThan(initStart);
        return source[initStart..initEnd];
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "build.ps1")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found from test directory.");
    }
}
