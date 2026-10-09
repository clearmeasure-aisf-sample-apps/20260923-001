using System.Text.Json.Nodes;
using Shouldly;

namespace ClearMeasure.Bootcamp.UnitTests.BuildGates.GitHubApp;

/// <summary>
/// Integration layer of the feature-loop credential change: the helper <c>board.ps1</c> and the stall watchdog
/// <c>Check-StalledLanes.ps1</c> run whole, in a real <c>pwsh</c>, against a stub GitHub API (the GITHUB_API_URL seam), with
/// the real <c>.claude/factory-loop.json</c>. They resolve their GitHub token in the documented order (a pre-minted App token,
/// a token minted from the App's key, then the GitHub CLI), send it as the Bearer of every call, retry a call the App cannot
/// make with the CLI token, ignore the retired personal access token variable, and never print a token, key or JWT.
/// The same runs hold which commit <c>board.ps1 wait ci</c> answers for (#90): the pull request's head at that poll, and
/// with <c>-Head</c> that commit alone.
/// </summary>
[TestFixture]
public class BoardScriptTests
{
    private const string BoardScript = ".claude/skills/feature-loop/board.ps1";
    private const string WatchdogScript = ".claude/skills/feature-loop-dispatch/Check-StalledLanes.ps1";
    private const string AppRepo = "clearmeasure-aisf-sample-apps/20260923-001";
    private const string EnvRepo = "clearmeasure-aisf-sample-apps/basic-environment-octopus-codefresh";
    private const string DecoyPat = "decoy-personal-access-token-0004";

    // #90: the head a push replaced (its build had failed; the first 12 characters are the ones pull request 84 printed)
    // and the head that push made (a made-up SHA).
    private const string PreviousHead = "ea088d69200e4f1a9b7c5d3e2f1a0b9c8d7e6f5a";
    private const string PushedHead = "3cee7c2b5a4d3e2f1a0b9c8d7e6f5a4b3c2d1e0f";

    // A wait polls every 0.3 s here; the production interval is minutes.
    private static readonly string[] FastPoll = ["-IntervalMinutes", "0.005"];

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

    /// <summary>
    /// #90 as it happened: the wait starts right after a push, the API still names the previous head, whose build had
    /// failed, then the pushed head with a pending and later a successful build. With the pushed commit as <c>-Head</c> the
    /// wait never reads the previous head's result and returns the pushed head's success.
    /// </summary>
    [Test]
    public void ShouldReturnThePushedHeadsSuccess_WhenWaitCiExpectsAHeadTheApiDoesNotNameYet()
    {
        using var api = new StubGitHubApi { Heads = [PreviousHead, PreviousHead, PushedHead] };
        api.States[PreviousHead] = ["failure"];
        api.States[PushedHead] = ["pending", "success"];
        using var ghCli = new StubGhCli();

        var result = RunBoard(api, ghCli, null, ["wait", "ci", "45", "-Head", PushedHead[..7], .. FastPoll], ("FAKE_GH_TOKEN", StubGitHubApi.CliToken));

        result.ExitCode.ShouldBe(0, result.Transcript);
        WaitLines(result).ShouldBe(
        [
            "waiting for head 3cee7c2: the pull request names head ea088d69200e, whose codefresh/ci result is not read",
            "pull request head changed from ea088d69200e to 3cee7c2b5a4d: the wait decides on 3cee7c2b5a4d",
            $"codefresh/ci pending on head 3cee7c2b5a4d {StubGitHubApi.BuildUrl(PushedHead)}",
            $"codefresh/ci success on head 3cee7c2b5a4d {StubGitHubApi.BuildUrl(PushedHead)}",
        ]);
        StatusReads(api, PreviousHead).ShouldBe(0, "the result of a head other than -Head is never read");
        result.Output.ShouldContain("board: credential: GitHub CLI token");
    }

    /// <summary>While the API names only another head, a wait with <c>-Head</c> keeps waiting until its deadline (exit 4) and never returns that head's finished failure.</summary>
    [Test]
    public void ShouldTimeOutWithoutReturningTheOtherHeadsFailure_WhenWaitCiExpectsAHeadTheApiNeverNames()
    {
        using var api = new StubGitHubApi { Heads = [PreviousHead] };
        api.States[PreviousHead] = ["failure"];
        using var ghCli = new StubGhCli();

        var result = RunBoard(api, ghCli, null, ["wait", "ci", "45", "-Head", PushedHead, "-TimeoutMinutes", "0.02", .. FastPoll], ("FAKE_GH_TOKEN", StubGitHubApi.CliToken));

        result.ExitCode.ShouldBe(4, result.Transcript);
        WaitLines(result).ShouldBe(
        [
            "waiting for head 3cee7c2b5a4d: the pull request names head ea088d69200e, whose codefresh/ci result is not read",
            "TIMEOUT after 0.02 min: waiting for head 3cee7c2b5a4d: the pull request names head ea088d69200e, whose codefresh/ci result is not read",
        ]);
        StatusReads(api, PreviousHead).ShouldBe(0);
    }

    /// <summary>
    /// The race without <c>-Head</c>: the API names the previous head, its failed result is read, and the pull request names the
    /// pushed head when the head is read again. The failure does not count; the wait decides on the pushed head and returns its success.
    /// </summary>
    [Test]
    public void ShouldReturnThePushedHeadsSuccess_WhenTheHeadMovesWhileThePreviousHeadsFailureIsRead()
    {
        using var api = new StubGitHubApi { Heads = [PreviousHead, PushedHead] };
        api.States[PreviousHead] = ["failure"];
        api.States[PushedHead] = ["pending", "success"];
        using var ghCli = new StubGhCli();

        var result = RunBoard(api, ghCli, null, ["wait", "ci", "45", .. FastPoll], ("FAKE_GH_TOKEN", StubGitHubApi.CliToken));

        result.ExitCode.ShouldBe(0, result.Transcript);
        WaitLines(result).ShouldBe(
        [
            "codefresh/ci failure on ea088d69200e does not count: the pull request head moved to 3cee7c2b5a4d while it was read",
            $"codefresh/ci pending on head 3cee7c2b5a4d {StubGitHubApi.BuildUrl(PushedHead)}",
            $"codefresh/ci success on head 3cee7c2b5a4d {StubGitHubApi.BuildUrl(PushedHead)}",
        ]);
    }

    /// <summary>
    /// A head that changes between two polls restarts the decision: the answer (here a failure, exit 1) is the new head's, and
    /// the output says the head changed. The pull request is one of the environment repository (<c>owner/repo#N</c>), so the
    /// context and every call are that repository's.
    /// </summary>
    [Test]
    public void ShouldDecideOnTheNewHead_WhenTheHeadChangesBetweenPolls()
    {
        using var api = new StubGitHubApi { Heads = [PreviousHead, PushedHead], StatusContext = "codefresh/env-checks" };
        api.States[PreviousHead] = ["pending"];
        api.States[PushedHead] = ["pending", "failure"];
        using var ghCli = new StubGhCli();

        var result = RunBoard(api, ghCli, null, ["wait", "ci", $"{EnvRepo}#45", .. FastPoll], ("FAKE_GH_TOKEN", StubGitHubApi.CliToken));

        result.ExitCode.ShouldBe(1, result.Transcript);
        WaitLines(result).ShouldBe(
        [
            $"codefresh/env-checks pending on head ea088d69200e {StubGitHubApi.BuildUrl(PreviousHead)}",
            "pull request head changed from ea088d69200e to 3cee7c2b5a4d: the wait decides on 3cee7c2b5a4d",
            $"codefresh/env-checks pending on head 3cee7c2b5a4d {StubGitHubApi.BuildUrl(PushedHead)}",
            $"codefresh/env-checks failure on head 3cee7c2b5a4d {StubGitHubApi.BuildUrl(PushedHead)}",
        ]);
        api.Requests.ShouldAllBe(request => request.PathOnly.StartsWith($"/repos/{EnvRepo}/", StringComparison.Ordinal));
    }

    /// <summary>
    /// With <c>-Head</c>, the API names the expected head, then another head (whose finished failure would end the wait with
    /// exit 1), then the expected head again. The other head's result is never read: the wait says the head changed, waits,
    /// and returns the expected head's success.
    /// </summary>
    [Test]
    public void ShouldNeverReadTheOtherHeadsResult_WhenTheApiNamesAnotherHeadInTheMiddleOfTheWait()
    {
        using var api = new StubGitHubApi { Heads = [PushedHead, PreviousHead, PreviousHead, PushedHead] };
        api.States[PreviousHead] = ["failure"];
        api.States[PushedHead] = ["pending", "success"];
        using var ghCli = new StubGhCli();

        var result = RunBoard(api, ghCli, null, ["wait", "ci", "45", "-Head", PushedHead, .. FastPoll], ("FAKE_GH_TOKEN", StubGitHubApi.CliToken));

        result.ExitCode.ShouldBe(0, result.Transcript);
        WaitLines(result).ShouldBe(
        [
            $"codefresh/ci pending on head 3cee7c2b5a4d {StubGitHubApi.BuildUrl(PushedHead)}",
            "pull request head changed from 3cee7c2b5a4d to ea088d69200e",
            "waiting for head 3cee7c2b5a4d: the pull request names head ea088d69200e, whose codefresh/ci result is not read",
            "pull request head changed from ea088d69200e to 3cee7c2b5a4d: the wait decides on 3cee7c2b5a4d",
            $"codefresh/ci success on head 3cee7c2b5a4d {StubGitHubApi.BuildUrl(PushedHead)}",
        ]);
        StatusReads(api, PreviousHead).ShouldBe(0, "the result of a head other than -Head is never read");
    }

    /// <summary>
    /// A head that does not move keeps the old answer, with or without a matching <c>-Head</c> (a prefix in any case): one line,
    /// exit 0 for success and 1 for failure or error. Without <c>-Head</c> this is also the limit of the wait: the head the API
    /// names is the only one it can know, so after a push the pushed commit is passed.
    /// </summary>
    /// <param name="state">The finished state of the head's status.</param>
    /// <param name="expectHead">Pass the head as <c>-Head</c>.</param>
    /// <param name="exitCode">The expected exit code.</param>
    [TestCase("success", false, 0)]
    [TestCase("success", true, 0)]
    [TestCase("failure", false, 1)]
    [TestCase("failure", true, 1)]
    [TestCase("error", false, 1)]
    public void ShouldReturnTheResultAtOnce_WhenTheHeadStaysWithAFinishedResult(string state, bool expectHead, int exitCode)
    {
        using var api = new StubGitHubApi();
        api.States[StubGitHubApi.HeadSha] = [state];
        using var ghCli = new StubGhCli();
        string[] head = expectHead ? ["-Head", StubGitHubApi.HeadSha[..12].ToUpperInvariant()] : [];

        var result = RunBoard(api, ghCli, null, ["wait", "ci", "45", .. head, .. FastPoll], ("FAKE_GH_TOKEN", StubGitHubApi.CliToken));

        result.ExitCode.ShouldBe(exitCode, result.Transcript);
        WaitLines(result).ShouldBe([$"codefresh/ci {state} on head 0123456789ab {StubGitHubApi.BuildUrl(StubGitHubApi.HeadSha)}"]);
        StatusReads(api, StubGitHubApi.HeadSha).ShouldBe(1);
    }

    /// <summary>
    /// <c>-Head</c> that is no commit SHA, or given to anything but <c>wait ci &lt;pr&gt;</c>, is a usage error (exit 2) before any
    /// call. An empty value is one of them (a variable that was never set): it does not become a wait without the head.
    /// </summary>
    [Test]
    public void ShouldExitTwoBeforeAnyCall_WhenHeadIsNoShaOrGoesWithAnotherCommand()
    {
        using var api = new StubGitHubApi();
        using var ghCli = new StubGhCli();
        var cases = new (string[] Arguments, string Message)[]
        {
            (["wait", "ci", "45", "-Head", "not-a-sha"], "board: -Head 'not-a-sha' is not a commit SHA (7-40 hex characters)"),
            (["wait", "ci", "45", "-Head", "3cee7"], "board: -Head '3cee7' is not a commit SHA (7-40 hex characters)"),
            (["wait", "ci", "45", "-Head", string.Empty], "board: -Head '' is not a commit SHA (7-40 hex characters)"),
            (["wait", "release", "45", "-Head", "3cee7c2"], "board: -Head goes with wait ci <pr> only"),
            (["wait", "deploy", PushedHead, "tdd", "-Head", "3cee7c2"], "board: -Head goes with wait ci <pr> only"),
            (["wait", "ci", PushedHead, "-Head", "3cee7c2"], "board: -Head goes with wait ci <pr> only"),
            (["status", "45", "-Head", "3cee7c2"], "board: -Head goes with wait ci <pr> only"),
        };

        foreach (var (arguments, message) in cases)
        {
            var result = RunBoard(api, ghCli, null, arguments, ("FAKE_GH_TOKEN", StubGitHubApi.CliToken));

            result.ExitCode.ShouldBe(2, result.Transcript);
            result.Output.ShouldContain(message);
        }

        api.Requests.ShouldBeEmpty();
    }

    /// <summary>The helper's own help, SKILL.md and reference.md name the expected-head argument, and reference.md has the section that says what a wait without it knows.</summary>
    [Test]
    public void ShouldDocumentTheExpectedHeadArgumentOfWaitCi_WhenTheFeatureLoopFilesAreRead()
    {
        File.ReadAllText(GitHubScriptHost.Script(BoardScript)).ShouldContain("wait ci <pr> [-Head <sha>]");
        File.ReadAllText(GitHubScriptHost.Script(".claude/skills/feature-loop/SKILL.md")).ShouldContain("wait ci <pr> -Head <sha>");
        var reference = File.ReadAllText(GitHubScriptHost.Script(".claude/skills/feature-loop/reference.md"));
        reference.ShouldContain("## Waiting on the right head");
        reference.ShouldContain("wait ci <pr> -Head <sha>");
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

    /// <summary>The lines a wait printed: without the <c>[HH:mm]</c> clock in front of a progress line, and without the line naming the credential.</summary>
    private static string[] WaitLines(ProcessResult result) =>
    [
        .. result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("board: credential:", StringComparison.Ordinal))
            .Select(line => line.Length > 8 && line[0] == '[' && line[6] == ']' ? line[8..] : line),
    ];

    /// <summary>How often the combined status of a commit was read.</summary>
    private static int StatusReads(StubGitHubApi api, string sha) =>
        api.Requests.Count(request => request.PathOnly.EndsWith($"/commits/{sha}/status", StringComparison.Ordinal));

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
