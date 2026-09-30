using System.Text.Json.Nodes;
using Shouldly;

namespace ClearMeasure.Bootcamp.UnitTests.BuildGates.GitHubApp;

/// <summary>
/// Integration layer of the feature-loop credential change: the helper <c>board.ps1</c> and the stall watchdog
/// <c>Check-StalledLanes.ps1</c> run whole, in a real <c>pwsh</c>, against a stub GitHub API (the GITHUB_API_URL seam), with
/// the real <c>.claude/factory-loop.json</c>. They resolve their GitHub token in the documented order (a pre-minted App token,
/// a token minted from the App's key, then the GitHub CLI), send it as the Bearer of every call, retry a call the App cannot
/// make with the CLI token, ignore the retired personal access token variable, and never print a token, key or JWT.
/// </summary>
[TestFixture]
public class BoardScriptTests
{
    private const string BoardScript = ".claude/skills/feature-loop/board.ps1";
    private const string WatchdogScript = ".claude/skills/feature-loop-dispatch/Check-StalledLanes.ps1";
    private const string AppRepo = "clearmeasure-aisf-sample-apps/20260923-001";
    private const string EnvRepo = "clearmeasure-aisf-sample-apps/basic-environment-octopus-codefresh";
    private const string DecoyPat = "decoy-personal-access-token-0004";

    [Test]
    public void ShouldMintTheAppTokenThenUseItOnEveryCall_WhenBoardStatusRunsWithOnlyTheKeyVariables()
    {
        using var key = new GeneratedAppKey();
        using var api = new StubGitHubApi();
        using var ghCli = new StubGhCli();

        var result = RunBoard(api, ghCli, key, ["status", "45"], ("FAKE_GH_TOKEN", StubGitHubApi.CliToken));

        result.ExitCode.ShouldBe(0, result.Transcript);
        result.Output.ShouldContain("board: credential: GitHub App installation token");
        result.Output.ShouldContain($"PR {AppRepo}#45 open head=0123456789ab mergeable=clean");
        result.Output.ShouldContain("codefresh/ci success");
        var requests = api.Requests;
        var mint = requests[0];
        mint.Method.ShouldBe("POST");
        mint.PathOnly.ShouldBe("/app/installations/166366113/access_tokens");
        key.Verifies(mint.Bearer).ShouldBeTrue("the exchange is authenticated by a JWT signed with the App key");
        var body = JsonNode.Parse(mint.Body)!.AsObject();
        body["repositories"]!.AsArray().Select(node => node!.GetValue<string>()).ShouldBe(["basic-environment-octopus-codefresh", "20260923-001"]);
        var permissions = body["permissions"]!.AsObject().ToDictionary(property => property.Key, property => property.Value!.GetValue<string>(), StringComparer.Ordinal);
        permissions.ShouldBe(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["organization_projects"] = "write", ["issues"] = "read", ["pull_requests"] = "read", ["metadata"] = "read" },
            ignoreOrder: true);
        requests.Skip(1).ShouldNotBeEmpty();
        requests.Skip(1).ShouldAllBe(request => request.Bearer == StubGitHubApi.AppToken, "every call after the mint carries the minted token");
        requests.ShouldNotContain(request => request.Authorization.Contains(DecoyPat, StringComparison.Ordinal));
        AssertNoSecrets(result, key, mint.Bearer);
    }

    [Test]
    public void ShouldUseThePreMintedTokenWithoutMinting_WhenTheAppTokenVariableIsSet()
    {
        using var api = new StubGitHubApi();
        using var ghCli = new StubGhCli();

        var result = RunBoard(api, ghCli, null, ["status", "45"], ("AISF_BOARD_APP_TOKEN", StubGitHubApi.PreMintedAppToken), ("FAKE_GH_TOKEN", StubGitHubApi.CliToken));

        result.ExitCode.ShouldBe(0, result.Transcript);
        result.Output.ShouldContain("board: credential: GitHub App installation token");
        api.Requests.ShouldNotContain(request => request.PathOnly.StartsWith("/app/", StringComparison.Ordinal));
        api.Requests.ShouldAllBe(request => request.Bearer == StubGitHubApi.PreMintedAppToken);
        result.Transcript.ShouldNotContain(StubGitHubApi.PreMintedAppToken);
    }

    [Test]
    public void ShouldUseTheGitHubCliTokenAndIgnoreTheRetiredVariable_WhenNoAppVariableIsSet()
    {
        using var api = new StubGitHubApi();
        using var ghCli = new StubGhCli();

        var result = RunBoard(api, ghCli, null, ["status", "45"], ("FAKE_GH_TOKEN", StubGitHubApi.CliToken));

        result.ExitCode.ShouldBe(0, result.Transcript);
        result.Output.ShouldContain("board: credential: GitHub CLI token");
        api.Requests.ShouldAllBe(request => request.Bearer == StubGitHubApi.CliToken);
        result.Transcript.ShouldNotContain(StubGitHubApi.CliToken);
        result.Transcript.ShouldNotContain(DecoyPat);
    }

    [Test]
    public void ShouldRetryOnceWithTheCliToken_WhenTheAppTokenIsRefusedByTheStatusesEndpoint()
    {
        using var key = new GeneratedAppKey();
        using var api = new StubGitHubApi { AppMayReadStatuses = false };
        using var ghCli = new StubGhCli();

        var result = RunBoard(api, ghCli, key, ["status", "45"], ("FAKE_GH_TOKEN", StubGitHubApi.CliToken));

        result.ExitCode.ShouldBe(0, result.Transcript);
        result.Output.ShouldContain("board: the App token was refused (HTTP 403 on Get repos/");
        result.Output.ShouldContain("codefresh/ci success");
        var statusCalls = api.Requests.Where(request => request.PathOnly.EndsWith("/status", StringComparison.Ordinal)).ToArray();
        statusCalls.Select(request => request.Bearer).ShouldBe([StubGitHubApi.AppToken, StubGitHubApi.CliToken]);
        AssertNoSecrets(result, key, api.Requests[0].Bearer, StubGitHubApi.CliToken);
    }

    [Test]
    public void ShouldRetryTheDispatchWithTheCliTokenAndMove_WhenTheAppTokenCannotDispatch()
    {
        using var key = new GeneratedAppKey();
        using var api = new StubGitHubApi();
        using var ghCli = new StubGhCli();

        var result = RunBoard(api, ghCli, key, ["move", "68", "In Progress"], ("FAKE_GH_TOKEN", StubGitHubApi.CliToken));

        result.ExitCode.ShouldBe(0, result.Transcript);
        result.Output.ShouldContain($"MOVED {AppRepo}#68 -> In Progress (dispatch 204)");
        var dispatches = api.Requests.Where(request => request.PathOnly.EndsWith("/dispatches", StringComparison.Ordinal)).ToArray();
        dispatches.Select(request => request.Bearer).ShouldBe([StubGitHubApi.AppToken, StubGitHubApi.CliToken]);
        dispatches[0].PathOnly.ShouldBe($"/repos/{EnvRepo}/dispatches");
        var payload = JsonNode.Parse(dispatches[1].Body)!;
        payload["event_type"]!.GetValue<string>().ShouldBe("board-status");
        payload["client_payload"]!["status"]!.GetValue<string>().ShouldBe("In Progress");
        payload["client_payload"]!["issue"]!.GetValue<int>().ShouldBe(68);
        api.Requests.ShouldNotContain(request => request.PathOnly.EndsWith("/comments", StringComparison.Ordinal));
        AssertNoSecrets(result, key, api.Requests[0].Bearer, StubGitHubApi.CliToken);
    }

    [Test]
    public void ShouldPostTheFallbackCommentAndExitOne_WhenTheDispatchIsRefusedEverywhere()
    {
        using var key = new GeneratedAppKey();
        using var api = new StubGitHubApi { DispatchStatusForCli = 403 };
        using var ghCli = new StubGhCli();

        var result = RunBoard(api, ghCli, key, ["move", "68", "Done"], ("FAKE_GH_TOKEN", StubGitHubApi.CliToken));

        result.ExitCode.ShouldBe(1, result.Transcript);
        result.Output.ShouldContain($"REFUSED {AppRepo}#68 -> Done (dispatch HTTP 403)");
        result.Output.ShouldContain($"FALLBACK comment posted on {AppRepo}#68");
        var comments = api.Requests.Where(request => request.PathOnly.EndsWith("/issues/68/comments", StringComparison.Ordinal)).ToArray();
        comments.Select(request => request.Bearer).ShouldBe([StubGitHubApi.AppToken, StubGitHubApi.CliToken]);
        JsonNode.Parse(comments[1].Body)!["body"]!.GetValue<string>().ShouldStartWith("board-status: Done");
        AssertNoSecrets(result, key, api.Requests[0].Bearer, StubGitHubApi.CliToken);
    }

    [Test]
    public void ShouldReportBothRefusals_WhenOnlyTheAppTokenExistsAndItCannotDispatch()
    {
        using var key = new GeneratedAppKey();
        using var api = new StubGitHubApi();
        using var ghCli = new StubGhCli();

        var result = RunBoard(api, ghCli, key, ["move", "68", "Done"]);

        result.ExitCode.ShouldBe(1, result.Transcript);
        result.Output.ShouldContain($"REFUSED {AppRepo}#68 -> Done (dispatch HTTP 403)");
        result.Output.ShouldContain("FALLBACK comment refused too (HTTP 403)");
        api.Requests.ShouldNotContain(request => request.Bearer == StubGitHubApi.CliToken);
    }

    [Test]
    public void ShouldFallBackToTheCliToken_WhenTheAppKeyCannotBeExchanged()
    {
        using var key = new GeneratedAppKey();
        using var api = new StubGitHubApi { MintStatus = 401 };
        using var ghCli = new StubGhCli();

        var result = RunBoard(api, ghCli, key, ["status", "45"], ("FAKE_GH_TOKEN", StubGitHubApi.CliToken));

        result.ExitCode.ShouldBe(0, result.Transcript);
        result.Output.ShouldContain("github-app: GitHub App 5130401: no installation token (HTTP 401). Falling back to the GitHub CLI token.");
        result.Output.ShouldContain("board: credential: GitHub CLI token");
        api.Requests.Skip(1).ShouldAllBe(request => request.Bearer == StubGitHubApi.CliToken);
        AssertNoSecrets(result, key, api.Requests[0].Bearer, StubGitHubApi.CliToken);
    }

    [Test]
    public void ShouldExitTwoNamingTheAppVariables_WhenThereIsNoTokenAtAll()
    {
        using var api = new StubGitHubApi();
        using var ghCli = new StubGhCli();

        var result = RunBoard(api, ghCli, null, ["status", "45"]);

        result.ExitCode.ShouldBe(2, result.Transcript);
        result.Output.ShouldContain("AISF_BOARD_APP_TOKEN");
        result.Output.ShouldContain("AISF_BOARD_APP_PRIVATE_KEY_PATH");
        api.Requests.ShouldBeEmpty();
    }

    [Test]
    public void ShouldMintThenReadWithTheAppToken_WhenTheWatchdogRunsWithOnlyTheKeyVariables()
    {
        using var key = new GeneratedAppKey();
        using var api = new StubGitHubApi();
        using var ghCli = new StubGhCli();

        var result = GitHubScriptHost.Run(
            GitHubScriptHost.Script(WatchdogScript),
            GitHubScriptHost.Environment(api, ghCli, ("AISF_BOARD_APP_PRIVATE_KEY_PATH", key.Path), ("FAKE_GH_TOKEN", StubGitHubApi.CliToken), (GitHubScriptHost.RetiredTokenVariable, DecoyPat)));

        result.ExitCode.ShouldBe(0, result.Transcript);
        result.Output.ShouldContain($"PASS no stalled work items in {AppRepo}.");
        api.Requests[0].PathOnly.ShouldBe("/app/installations/166366113/access_tokens");
        api.Requests.Skip(1).ShouldNotBeEmpty();
        api.Requests.Skip(1).ShouldAllBe(request => request.Bearer == StubGitHubApi.AppToken);
        AssertNoSecrets(result, key, api.Requests[0].Bearer);
    }

    [Test]
    public void ShouldUseTheCliTokenAndIgnoreTheRetiredVariable_WhenTheWatchdogHasNothingElse()
    {
        using var api = new StubGitHubApi();
        using var ghCli = new StubGhCli();

        var result = GitHubScriptHost.Run(
            GitHubScriptHost.Script(WatchdogScript),
            GitHubScriptHost.Environment(api, ghCli, ("FAKE_GH_TOKEN", StubGitHubApi.CliToken), (GitHubScriptHost.RetiredTokenVariable, DecoyPat)));

        result.ExitCode.ShouldBe(0, result.Transcript);
        api.Requests.ShouldNotBeEmpty();
        api.Requests.ShouldAllBe(request => request.Bearer == StubGitHubApi.CliToken);
    }

    [Test]
    public void ShouldExitTwoNamingTheAppVariables_WhenTheWatchdogHasNoToken()
    {
        using var api = new StubGitHubApi();
        using var ghCli = new StubGhCli();

        var result = GitHubScriptHost.Run(GitHubScriptHost.Script(WatchdogScript), GitHubScriptHost.Environment(api, ghCli));

        result.ExitCode.ShouldBe(2, result.Transcript);
        result.Output.ShouldContain("AISF_BOARD_APP_TOKEN");
        api.Requests.ShouldBeEmpty();
    }

    private static ProcessResult RunBoard(StubGitHubApi api, StubGhCli ghCli, GeneratedAppKey? key, string[] arguments, params (string Name, string Value)[] set)
    {
        var variables = new List<(string Name, string Value)>(set) { (GitHubScriptHost.RetiredTokenVariable, DecoyPat) };
        if (key is not null)
        {
            variables.Add(("AISF_BOARD_APP_PRIVATE_KEY_PATH", key.Path));
        }

        return GitHubScriptHost.Run(GitHubScriptHost.Script(BoardScript), GitHubScriptHost.Environment(api, ghCli, [.. variables]), arguments);
    }

    private static void AssertNoSecrets(ProcessResult result, GeneratedAppKey key, params string[] secrets)
    {
        var transcript = result.Transcript;
        transcript.ShouldNotContain("BEGIN");
        transcript.ShouldNotContain(key.BodyFragment);
        transcript.ShouldNotContain(StubGitHubApi.AppToken);
        transcript.ShouldNotContain(DecoyPat);
        foreach (var secret in secrets.Where(secret => secret.Length > 0))
        {
            transcript.ShouldNotContain(secret);
        }
    }
}
