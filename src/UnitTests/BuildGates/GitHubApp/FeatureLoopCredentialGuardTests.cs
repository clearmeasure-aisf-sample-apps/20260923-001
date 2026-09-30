using System.Text.Json.Nodes;
using Shouldly;

namespace ClearMeasure.Bootcamp.UnitTests.BuildGates.GitHubApp;

/// <summary>
/// Repository guard of the feature-loop credential change: no file of the feature-loop tooling, its configuration, its
/// documentation or its tests names the retired personal access token variable, the token order and the App block of
/// <c>.claude/factory-loop.json</c> equal the values pinned from the environment repository, and the documentation names the
/// App installation token first.
/// </summary>
[TestFixture]
public class FeatureLoopCredentialGuardTests
{
    private static readonly string[] ToolingFiles =
    [
        ".claude/factory-loop.json",
        ".claude/skills/feature-loop/board.ps1",
        ".claude/skills/feature-loop/SKILL.md",
        ".claude/skills/feature-loop/reference.md",
        ".claude/skills/feature-loop-dispatch/Check-StalledLanes.ps1",
        ".claude/skills/feature-loop-dispatch/SKILL.md",
        "scripts/github/GitHubAppAuth.ps1",
    ];

    [Test]
    public void ShouldNameNoRetiredVariable_WhenFeatureLoopToolingAndTestsAreScanned()
    {
        var root = GitHubScriptHost.RepositoryRoot;
        var scanned = ToolingFiles.Select(GitHubScriptHost.Script)
            .Concat(Directory.EnumerateFiles(Path.Join(root, ".claude", "skills"), "*", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Join(root, "scripts"), "*", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Join(root, "src", "UnitTests", "BuildGates", "GitHubApp"), "*.cs"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var offenders = scanned.Where(path => File.ReadAllText(path).Contains(GitHubScriptHost.RetiredTokenVariable, StringComparison.Ordinal)).ToArray();

        scanned.Length.ShouldBeGreaterThan(ToolingFiles.Length);
        offenders.ShouldBeEmpty("these files still name the retired personal access token variable");
    }

    [Test]
    public void ShouldPinTheAppBlock_WhenFactoryLoopJsonIsRead()
    {
        var config = ReadConfig();
        var app = config["githubApp"]!;

        app["name"]!.GetValue<string>().ShouldBe("aisf-board");
        app["appId"]!.GetValue<long>().ShouldBe(5130401);
        app["installationId"]!.GetValue<long>().ShouldBe(166366113);
        app["repositories"]!.AsArray().Select(node => node!.GetValue<string>()).ShouldBe(
            ["clearmeasure-aisf-sample-apps/basic-environment-octopus-codefresh", "clearmeasure-aisf-sample-apps/20260923-001"]);
        app["permissions"]!.AsObject().ToDictionary(property => property.Key, property => property.Value!.GetValue<string>(), StringComparer.Ordinal).ShouldBe(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["organization_projects"] = "write", ["issues"] = "read", ["pull_requests"] = "read", ["metadata"] = "read" },
            ignoreOrder: true);
    }

    [Test]
    public void ShouldListTheAppTokenFirst_WhenTheTokenOrderIsRead()
    {
        var order = ReadConfig()["helper"]!["tokenOrder"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();

        order.Length.ShouldBe(3);
        order[0].ShouldStartWith("AISF_BOARD_APP_TOKEN");
        order[1].ShouldStartWith("AISF_BOARD_APP_ID");
        order[1].ShouldContain("AISF_BOARD_APP_PRIVATE_KEY_PATH");
        order[1].ShouldContain("AISF_BOARD_APP_PRIVATE_KEY");
        order[2].ShouldStartWith("gh auth token");
        ReadConfig()["helper"]!.AsObject().ContainsKey("tokenEnv").ShouldBeFalse("the old variable list is replaced by tokenOrder");
    }

    [Test]
    public void ShouldDocumentTheAppOrder_WhenTheSkillsAreRead()
    {
        foreach (var relative in new[] { ".claude/skills/feature-loop/SKILL.md", ".claude/skills/feature-loop-dispatch/SKILL.md" })
        {
            var text = File.ReadAllText(GitHubScriptHost.Script(relative));
            text.ShouldContain("AISF_BOARD_APP_TOKEN", customMessage: relative);
        }

        var skill = File.ReadAllText(GitHubScriptHost.Script(".claude/skills/feature-loop/SKILL.md"));
        skill.ShouldContain("GitHub App");
        skill.ShouldContain("AISF_BOARD_APP_PRIVATE_KEY_PATH");
        skill.ShouldContain("credential: GitHub App installation token");
        skill.IndexOf("AISF_BOARD_APP_TOKEN", StringComparison.Ordinal).ShouldBeLessThan(skill.IndexOf("gh auth token", StringComparison.Ordinal));
    }

    [Test]
    public void ShouldDotSourceTheSharedHelper_WhenTheScriptsAreRead()
    {
        File.Exists(GitHubScriptHost.Script("scripts/github/GitHubAppAuth.ps1")).ShouldBeTrue();
        foreach (var relative in new[] { ".claude/skills/feature-loop/board.ps1", ".claude/skills/feature-loop-dispatch/Check-StalledLanes.ps1" })
        {
            var text = File.ReadAllText(GitHubScriptHost.Script(relative));
            text.ShouldContain("GitHubAppAuth.ps1", customMessage: relative);
            text.ShouldContain("Resolve-GitHubToken", customMessage: relative);
            text.ShouldNotContain("Invoke-RestMethod -Uri \"https://api.github.com", customMessage: relative);
        }
    }

    [Test]
    public void ShouldNameNoRetiredProjectsToken_WhenFeatureLoopToolingIsScanned()
    {
        var retired = string.Concat("PROJECTS", "_", "PAT");
        var root = GitHubScriptHost.RepositoryRoot;
        var scanned = ToolingFiles.Select(GitHubScriptHost.Script)
            .Concat(Directory.EnumerateFiles(Path.Join(root, ".claude", "skills"), "*", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Join(root, "scripts"), "*", SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var offenders = scanned.Where(path => File.ReadAllText(path).Contains(retired, StringComparison.Ordinal)).ToArray();

        scanned.Length.ShouldBeGreaterThan(ToolingFiles.Length);
        offenders.ShouldBeEmpty("these files still name the retired board personal access token secret");
    }

    [Test]
    public void ShouldPinBoardWorkflowSecrets_WhenFactoryLoopJsonIsRead()
    {
        var dispatch = ReadConfig()["boardMoves"]!["dispatch"]!.AsObject();

        dispatch["workflowSecrets"]!.AsArray().Select(node => node!.GetValue<string>()).ShouldBe(["BOARD_APP_ID", "BOARD_APP_PRIVATE_KEY"]);
        dispatch.ContainsKey("workflowSecret").ShouldBeFalse("the single personal access token secret is replaced by workflowSecrets");
    }

    [Test]
    public void ShouldListNoPullRequestTargetEvent_WhenAutomaticBoardMovesAreRead()
    {
        var automatic = ReadConfig()["boardMoves"]!["automatic"]!.AsObject();

        automatic.Select(property => property.Key).Where(key => key.StartsWith("pull_request_target", StringComparison.Ordinal)).ShouldBeEmpty();
        automatic.ContainsKey("pull_request.closed(merged)").ShouldBeTrue();
    }

    [Test]
    public void ShouldDocumentTheAppCredentialForCardMoves_WhenReferenceIsRead()
    {
        var reference = File.ReadAllText(GitHubScriptHost.Script(".claude/skills/feature-loop/reference.md"));

        reference.ShouldContain("BOARD_APP_ID");
        reference.ShouldContain("BOARD_APP_PRIVATE_KEY");
        reference.ShouldContain("Contents: write");
    }
    private static JsonNode ReadConfig() => JsonNode.Parse(File.ReadAllText(GitHubScriptHost.Script(".claude/factory-loop.json")))!;
}
