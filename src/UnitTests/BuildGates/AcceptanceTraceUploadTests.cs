using Shouldly;

namespace ClearMeasure.Bootcamp.UnitTests.BuildGates;

[TestFixture]
public class AcceptanceTraceUploadTests
{
    [TestCase("playwright-traces-acceptance")]
    [TestCase("playwright-traces-acceptance-arm")]
    public void ShouldUploadPlaywrightTracesOnFailure_WhenBuildWorkflowIsRead(string artifactName)
    {
        var lines = File.ReadAllLines(Path.Combine(FindRepoRoot(), ".github", "workflows", "build.yml"));
        var nameLine = Array.FindIndex(lines, line => line.Trim() == $"name: {artifactName}");
        nameLine.ShouldBeGreaterThan(-1, $"no artifact named {artifactName}");

        var step = lines[(nameLine - 4)..(nameLine + 4)].Select(line => line.Trim()).ToArray();

        step.ShouldContain("uses: actions/upload-artifact@v4");
        step.ShouldContain("if: failure()");
        step.ShouldContain("path: '**/playwright-traces/*.zip'");
        step.ShouldContain("if-no-files-found: ignore");
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
