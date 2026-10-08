using Shouldly;

namespace ClearMeasure.Bootcamp.UnitTests.BuildGates;

/// <summary>
/// "Publish Release Candidate" logs in to Azure. On a branch where that login is not configured the job is skipped,
/// on the word of a job that reports only whether the login's secret is set. On master it runs either way.
/// </summary>
[TestFixture]
public class ReleaseCandidateAzureLoginTests
{
    private const string ReleaseCandidateJob = "docker-build-image-for-churchbulletin-ui";
    private const string DetectJob = "detect-azure-login";
    private const string LoginSecret = "secrets.AZURE_CREDENTIALS";
    private const string NeedsLine = $"    needs: [changes, build-linux, {DetectJob}]";
    private const string LoginConfigured = $"needs.{DetectJob}.outputs.configured == 'true'";
    private const string OnMaster = "github.ref == 'refs/heads/master'";

    private const string IfLine =
        $"    if: success() && needs.changes.outputs.code == 'true' && ({LoginConfigured} || {OnMaster})";

    [Test]
    public void ShouldSkipReleaseCandidateJobOnABranchWithoutAzureLogin_WhenBuildWorkflowIsRead()
    {
        var job = ReadJob(ReleaseCandidateJob);

        job.ShouldContain("    name: Publish Release Candidate");
        job.ShouldContain(NeedsLine);
        job.ShouldContain(IfLine);
    }

    /// <summary>
    /// The Deploy workflow starts when a Build run on master succeeds. Without the Azure login that run fails in
    /// "Publish Release Candidate", and nothing else keeps Deploy from running; so on master the job is not skipped.
    /// When this test fails because Deploy is gated some other way, the master clause can go.
    /// </summary>
    [Test]
    public void ShouldKeepReleaseCandidateJobOnMaster_WhenDeployWorkflowStartsOnASuccessfulBuild()
    {
        var deploy = string.Join('\n', ReadWorkflow("deploy.yml"));
        var jobCondition = ReadJob(ReleaseCandidateJob)
            .Single(line => line.StartsWith("    if:", StringComparison.Ordinal));

        deploy.ShouldContain("workflows: [\"Build\"]");
        deploy.ShouldContain("branches: [master]");
        deploy.ShouldContain("github.event.workflow_run.conclusion == 'success'");
        jobCondition.ShouldEndWith($"|| {OnMaster})");
    }

    [Test]
    public void ShouldDetectTheOnlySecretTheReleaseCandidateJobNeedsConfigured_WhenBuildWorkflowIsRead()
    {
        var secretLines = ReadJob(ReleaseCandidateJob)
            .Where(line => line.Contains("secrets.", StringComparison.Ordinal)
                           && !line.Contains("secrets.GITHUB_TOKEN", StringComparison.Ordinal))
            .ToArray();

        secretLines.ShouldNotBeEmpty();
        secretLines.ShouldAllBe(line => line.Contains(LoginSecret, StringComparison.Ordinal));
    }

    [Test]
    public void ShouldReportOnlyWhetherTheSecretIsSet_WhenAzureLoginIsDetected()
    {
        var job = ReadJob(DetectJob);
        var secretLines = job.Where(line => line.Contains("secrets.", StringComparison.Ordinal)).ToArray();

        job.ShouldContain("    permissions: {}");
        job.ShouldContain("      configured: ${{ steps.detect.outputs.configured }}");
        job.ShouldContain("        run: echo \"configured=${CONFIGURED}\" >> \"$GITHUB_OUTPUT\"");
        secretLines.ShouldHaveSingleItem().ShouldBe($"          CONFIGURED: ${{{{ {LoginSecret} != '' }}}}");
    }

    [Test]
    public void ShouldNotGateAnyOtherJob_WhenReleaseCandidateJobIsSkipped()
    {
        var lines = ReadWorkflow("build.yml");
        var candidateMentions = lines.Where(line => line.Contains(ReleaseCandidateJob, StringComparison.Ordinal));
        var detectMentions = lines.Where(line => line.Contains(DetectJob, StringComparison.Ordinal));

        candidateMentions.ShouldBe([$"  {ReleaseCandidateJob}:"]);
        detectMentions.ShouldBe([$"  {DetectJob}:", NeedsLine, IfLine]);
    }

    private static string[] ReadJob(string jobId)
    {
        var lines = ReadWorkflow("build.yml");
        var start = Array.IndexOf(lines, $"  {jobId}:");
        start.ShouldBeGreaterThan(-1, $"no job {jobId}");
        var end = Array.FindIndex(lines, start + 1, IsJobStart);

        return end < 0 ? lines[start..] : lines[start..end];
    }

    private static bool IsJobStart(string line)
    {
        return line.Length > 2 && line.StartsWith("  ", StringComparison.Ordinal) && char.IsLetter(line[2])
               && line.EndsWith(':');
    }

    private static string[] ReadWorkflow(string fileName)
    {
        return File.ReadAllLines(Path.Combine(FindRepoRoot(), ".github", "workflows", fileName));
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
