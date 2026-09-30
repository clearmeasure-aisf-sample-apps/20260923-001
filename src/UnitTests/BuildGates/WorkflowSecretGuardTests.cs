using Shouldly;

namespace ClearMeasure.Bootcamp.UnitTests.BuildGates;

/// <summary>
/// Guards the build workflow against personal-access-token secrets and the dead
/// "mark PR ready for review" automation that used one.
/// </summary>
[TestFixture]
public class WorkflowSecretGuardTests
{
    [Test]
    public void BuildWorkflow_WhenRead_DoesNotReferenceCopilotPat()
    {
        var yaml = File.ReadAllText(FindRepoFile(Path.Join(".github", "workflows", "build.yml")));

        yaml.ShouldNotContain("COPILOT_PAT");
    }

    [Test]
    public void BuildWorkflow_WhenRead_DoesNotMarkPullRequestReady()
    {
        var yaml = File.ReadAllText(FindRepoFile(Path.Join(".github", "workflows", "build.yml")));

        yaml.ShouldNotContain("gh pr ready");
        yaml.ShouldNotContain("Mark PR as ready for review");
    }

    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null)
        {
            var candidate = Path.Join(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"{relativePath} not found from test directory.");
    }
}
