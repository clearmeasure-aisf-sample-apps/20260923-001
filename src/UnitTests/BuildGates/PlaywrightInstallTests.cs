using Shouldly;

namespace ClearMeasure.Bootcamp.UnitTests.BuildGates;

[TestFixture]
public class PlaywrightInstallTests
{
    [Test]
    public void ShouldInstallOsDependenciesByDefault_WhenBuildScriptIsRead()
    {
        var acceptanceTests = ReadAcceptanceTestsFunction();

        acceptanceTests.ShouldContain("$playwrightInstallArguments = @(\"install\", \"chromium\", \"--with-deps\")");
        acceptanceTests.ShouldContain("& pwsh $playwrightScript @playwrightInstallArguments");
    }

    [Test]
    public void ShouldSkipOsDependenciesOnlyOnLinuxWithoutAptGet_WhenBuildScriptIsRead()
    {
        var acceptanceTests = ReadAcceptanceTestsFunction();

        acceptanceTests.ShouldContain("if ((Test-IsLinux) -and -not (Get-Command apt-get -ErrorAction SilentlyContinue))");
        acceptanceTests.ShouldContain("$playwrightInstallArguments = @(\"install\", \"chromium\")");
    }

    private static string ReadAcceptanceTestsFunction()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "build.ps1"));
        var start = source.IndexOf("Function AcceptanceTests", StringComparison.Ordinal);
        start.ShouldBeGreaterThan(-1);
        var end = source.IndexOf("\nFunction ", start + 1, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(start);
        return source[start..end];
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
