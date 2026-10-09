using Shouldly;

namespace ClearMeasure.Bootcamp.UnitTests.BuildGates;

/// <summary>
/// "Publish Release Candidate" logs in to Azure. Where that login is not configured the job is skipped, on the word
/// of a job that reports only whether the login's secret is set. That lets a Build run on master succeed, which
/// starts the Deploy workflow; its gate skips the deployment where its own Octopus settings are not configured.
/// </summary>
[TestFixture]
public class ReleaseCandidateAzureLoginTests
{
    private const string ReleaseCandidateJob = "docker-build-image-for-churchbulletin-ui";
    private const string DetectJob = "detect-azure-login";
    private const string LoginSecret = "secrets.AZURE_CREDENTIALS";
    private const string NeedsLine = $"    needs: [changes, build-linux, {DetectJob}]";
    private const string LoginConfigured = $"needs.{DetectJob}.outputs.configured == 'true'";
    private const string DeployGateJob = "resolve-deploy-gate";

    private const string IfLine =
        $"    if: success() && needs.changes.outputs.code == 'true' && {LoginConfigured}";

    private const string OctopusConfiguredLine =
        "          OCTOPUS_CONFIGURED: ${{ secrets.OCTO_API_KEY != '' && secrets.OCTOPUS_URL != '' "
        + "&& vars.OCTOPUS_SPACE != '' && vars.OCTOPUS_PROJECT != '' }}";

    [Test]
    public void ShouldSkipReleaseCandidateJobWithoutAzureLogin_WhenBuildWorkflowIsRead()
    {
        var job = ReadJob(ReleaseCandidateJob);

        job.ShouldContain("    name: Publish Release Candidate");
        job.ShouldContain(NeedsLine);
        job.ShouldContain(IfLine);
    }

    /// <summary>
    /// The Deploy workflow starts when a Build run on master succeeds, and with the release-candidate job skipped
    /// that run succeeds where nothing is configured. So the Deploy gate itself answers "no" where the workflow's
    /// Octopus settings are not all there, before it reads anything, and the first environment job runs only on a
    /// "yes". The gate's step is handed "true" or "false", never a secret's value.
    /// </summary>
    [Test]
    public void ShouldSkipDeployWithoutOctopusSettings_WhenDeployWorkflowStartsOnASuccessfulBuild()
    {
        var gate = ReadJob("deploy.yml", DeployGateJob);
        var secretLines = gate
            .Where(line => line.Contains("secrets.", StringComparison.Ordinal)
                           && !line.Contains("secrets.GITHUB_TOKEN", StringComparison.Ordinal))
            .ToArray();
        var refusal = Array.IndexOf(gate, "          if [ \"${OCTOPUS_CONFIGURED}\" != \"true\" ]; then");
        var firstRead = Array.FindIndex(gate, line => line.Contains("gh api", StringComparison.Ordinal));
        var firstEnvironmentJob = string.Join('\n', ReadJob("deploy.yml", "deploy-to-tdd"));

        secretLines.ShouldHaveSingleItem().ShouldBe(OctopusConfiguredLine);
        refusal.ShouldBeGreaterThan(-1);
        gate[refusal + 2].ShouldBe("            echo \"should_deploy=false\" >> \"$GITHUB_OUTPUT\"");
        gate[refusal + 3].ShouldBe("            exit 0");
        firstRead.ShouldBeGreaterThan(refusal);
        firstEnvironmentJob.ShouldContain($"    needs: [{DeployGateJob}]");
        firstEnvironmentJob.ShouldContain($"needs.{DeployGateJob}.outputs.should_deploy == 'true'");
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
        return ReadJob("build.yml", jobId);
    }

    private static string[] ReadJob(string fileName, string jobId)
    {
        var lines = ReadWorkflow(fileName);
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
