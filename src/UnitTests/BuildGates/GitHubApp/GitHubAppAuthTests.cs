using System.Text;
using System.Text.Json.Nodes;
using Shouldly;

namespace ClearMeasure.Bootcamp.UnitTests.BuildGates.GitHubApp;

/// <summary>
/// Unit layer of the feature-loop credential change: <c>scripts/github/GitHubAppAuth.ps1</c>, the GitHub App helper (RS256
/// JWT, installation token, token order). Every test dot-sources the script in a real <c>pwsh</c>, with a key generated at
/// test time and a stub GitHub API behind GITHUB_API_URL; none makes a live call. The transcripts are checked for the key,
/// the JWT and the token.
/// </summary>
[TestFixture]
public class GitHubAppAuthTests
{
    private const string AppId = "5130401";
    private const long Now = 1_800_000_000;

    private string _scratch = null!;

    [SetUp]
    public void CreateScratch() => _scratch = Directory.CreateTempSubdirectory("github-app-auth-").FullName;

    [TearDown]
    public void DeleteScratch() => Directory.Delete(_scratch, recursive: true);

    [Test]
    public void ShouldProduceValidRs256Token_WhenKeyIsReadFromFile()
    {
        using var key = new GeneratedAppKey();

        var result = Snippet($"Write-Output (New-GitHubAppJwt -AppId '{AppId}' -Now {Now})", Key(key));

        result.ExitCode.ShouldBe(0, result.Transcript);
        var jwt = result.Output.Trim();
        jwt.Split('.').Length.ShouldBe(3);
        Json(jwt.Split('.')[0]).ToJsonString().ShouldBe("""{"alg":"RS256","typ":"JWT"}""");
        var claims = Json(jwt.Split('.')[1]);
        claims["iat"]!.GetValue<long>().ShouldBe(Now - 60);
        claims["exp"]!.GetValue<long>().ShouldBe(Now + 540);
        claims["iss"]!.GetValue<string>().ShouldBe(AppId);
        (claims["exp"]!.GetValue<long>() - claims["iat"]!.GetValue<long>()).ShouldBeLessThanOrEqualTo(600);
        key.Verifies(jwt).ShouldBeTrue("the signature does not verify with the public half of the key");
    }

    [Test]
    public void ShouldGiveTheSameToken_WhenKeyComesFromFileTextOrEscapedText()
    {
        using var key = new GeneratedAppKey();
        var snippet = $"Write-Output (New-GitHubAppJwt -AppId '{AppId}' -Now {Now})";

        var fromFile = Snippet(snippet, Key(key));
        var fromText = Snippet(snippet, ("AISF_BOARD_APP_PRIVATE_KEY", key.Pem));
        var fromEscapedText = Snippet(snippet, ("AISF_BOARD_APP_PRIVATE_KEY", key.Pem.Trim().Replace("\n", "\\n", StringComparison.Ordinal)));

        fromFile.ExitCode.ShouldBe(0, fromFile.Transcript);
        fromText.Output.Trim().ShouldBe(fromFile.Output.Trim(), fromText.Transcript);
        fromEscapedText.Output.Trim().ShouldBe(fromFile.Output.Trim(), fromEscapedText.Transcript);
    }

    [Test]
    public void ShouldUseTheKeyFile_WhenBothKeySourcesAreSet()
    {
        using var fileKey = new GeneratedAppKey();
        using var textKey = new GeneratedAppKey();

        var result = Snippet($"Write-Output (New-GitHubAppJwt -AppId '{AppId}' -Now {Now})", Key(fileKey), ("AISF_BOARD_APP_PRIVATE_KEY", textKey.Pem));

        var jwt = result.Output.Trim();
        fileKey.Verifies(jwt).ShouldBeTrue(result.Transcript);
        textKey.Verifies(jwt).ShouldBeFalse();
    }

    [Test]
    public void ShouldFailWithoutLeakingKeyMaterial_WhenKeyIsMissingOrGarbled()
    {
        // Assembled at run time so that no file of the repository holds a private key block, not even a bogus one.
        var bogusKey = "-----BEGIN " + "PRIVATE KEY-----\nQUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVo=\n-----END " + "PRIVATE KEY-----\n";
        var snippet = $"try {{ New-GitHubAppJwt -AppId '{AppId}' -Now {Now} | Out-Null; Write-Output 'NO-ERROR' }} catch {{ Write-Output $_.Exception.Message }}";

        var missing = Snippet(snippet);
        var absentFile = Snippet(snippet, ("AISF_BOARD_APP_PRIVATE_KEY_PATH", Path.Join(_scratch, "no-such-key.pem")));
        var garbled = Snippet(snippet, ("AISF_BOARD_APP_PRIVATE_KEY", bogusKey));

        missing.Output.ShouldContain($"GitHub App {AppId}: no private key");
        missing.Output.ShouldContain("AISF_BOARD_APP_PRIVATE_KEY_PATH");
        absentFile.Output.ShouldContain("AISF_BOARD_APP_PRIVATE_KEY_PATH does not exist");
        garbled.Output.ShouldContain($"GitHub App {AppId}: the private key is not a valid RSA PEM key");
        foreach (var result in new[] { missing, absentFile, garbled })
        {
            result.Transcript.ShouldNotContain("BEGIN");
            result.Transcript.ShouldNotContain("QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVo");
            result.Transcript.ShouldNotContain("NO-ERROR");
        }
    }

    [Test]
    public void ShouldSendNarrowedRepositoriesAndPermissions_WhenTheInstallationTokenIsRequested()
    {
        using var key = new GeneratedAppKey();
        using var api = new StubGitHubApi();
        var snippet = $$"""
            $token = Get-GitHubAppInstallationToken -AppId '{{AppId}}' -InstallationId '777' -Now {{Now}} `
                -Repository @('acme/first-repo', 'acme/second-repo') -Permission @{ organization_projects = 'write'; issues = 'read' }
            Write-Output "minted=$($token.Length -gt 0)"
            """;

        var result = Snippet(snippet, api, Key(key));

        result.ExitCode.ShouldBe(0, result.Transcript);
        result.Output.ShouldContain("minted=True");
        var request = api.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe("POST");
        request.PathOnly.ShouldBe("/app/installations/777/access_tokens");
        key.Verifies(request.Bearer).ShouldBeTrue("the exchange is not authenticated by a JWT signed with the App key");
        var body = JsonNode.Parse(request.Body)!.AsObject();
        body.Select(property => property.Key).ShouldBe(["repositories", "permissions"]);
        body["repositories"]!.AsArray().Select(node => node!.GetValue<string>()).ShouldBe(["first-repo", "second-repo"]);
        var permissions = body["permissions"]!.AsObject().ToDictionary(property => property.Key, property => property.Value!.GetValue<string>(), StringComparer.Ordinal);
        permissions.Count.ShouldBe(2, "the token is narrowed to exactly the permissions asked for");
        permissions["organization_projects"].ShouldBe("write");
        permissions["issues"].ShouldBe("read");
        AssertNoSecrets(result, key, request.Bearer);
    }

    [Test]
    public void ShouldDiscoverTheInstallationWithTheJwt_WhenNoInstallationIdIsGiven()
    {
        using var key = new GeneratedAppKey();
        using var api = new StubGitHubApi();
        var snippet = $"$token = Get-GitHubAppInstallationToken -AppId '{AppId}' -Now {Now} -Repository @('acme/first-repo') -Permission @{{ issues = 'read' }}; Write-Output ($token -ceq '{StubGitHubApi.AppToken}')";

        var result = Snippet(snippet, api, Key(key));

        result.ExitCode.ShouldBe(0, result.Transcript);
        result.Output.Trim().ShouldBe("True");
        api.Requests.Select(request => $"{request.Method} {request.PathOnly}")
            .ShouldBe(["GET /repos/acme/first-repo/installation", "POST /app/installations/4242/access_tokens"]);
        api.Requests.ShouldAllBe(request => key.Verifies(request.Bearer));
    }

    [Test]
    public void ShouldFailWithAppIdAndStatusOnly_WhenTheTokenExchangeIsRefused()
    {
        using var key = new GeneratedAppKey();
        using var api = new StubGitHubApi { MintStatus = 401 };
        var snippet = $"try {{ Get-GitHubAppInstallationToken -AppId '{AppId}' -InstallationId '777' -Now {Now} -Repository @('acme/r') -Permission @{{ issues = 'read' }} | Out-Null }} catch {{ Write-Output $_.Exception.Message }}";

        var result = Snippet(snippet, api, Key(key));

        result.Output.Trim().ShouldBe($"GitHub App {AppId}: no installation token (HTTP 401).");
        AssertNoSecrets(result, key, api.Requests[0].Bearer);
    }

    [Test]
    public void ShouldPreferThePreMintedToken_WhenARetiredVariableIsAlsoSet()
    {
        using var ghCli = new StubGhCli();

        var result = Resolve(ghCli, ("AISF_BOARD_APP_TOKEN", "pre-minted"), ("FAKE_GH_TOKEN", "cli-token"), (GitHubScriptHost.RetiredTokenVariable, "decoy"));

        result.Output.Trim().ShouldBe("app pre-minted");
    }

    [Test]
    public void ShouldUseTheCliToken_WhenNoAppVariableIsSetAndARetiredVariableIsPresent()
    {
        using var ghCli = new StubGhCli();

        var result = Resolve(ghCli, ("FAKE_GH_TOKEN", "cli-token"), (GitHubScriptHost.RetiredTokenVariable, "decoy"));

        result.Output.Trim().ShouldBe("gh cli-token");
    }

    [Test]
    public void ShouldReadGhTokenDirectlyAndReturnNothingWithoutOne_WhenGhIsNotOnPath()
    {
        using var noGh = new StubGhCli();
        File.Delete(noGh.Executable);
        var noGhDirectory = noGh.Directory;
        var snippet = "$resolved = Resolve-GitHubToken; if ($resolved) { Write-Output \"$($resolved.Source) $($resolved.Token)\" } else { Write-Output 'none' }";

        var withToken = Snippet(snippet, environment => environment["PATH"] = noGhDirectory, ("GH_TOKEN", "env-token"), (GitHubScriptHost.RetiredTokenVariable, "decoy"));
        var withoutToken = Snippet(snippet, environment => environment["PATH"] = noGhDirectory);

        withToken.Output.Trim().ShouldBe("gh env-token");
        withoutToken.Output.Trim().ShouldBe("none");
    }

    [Test]
    public void ShouldMintTheInstallationTokenAheadOfTheCli_WhenAppIdAndKeyAreSet()
    {
        using var key = new GeneratedAppKey();
        using var api = new StubGitHubApi();
        using var ghCli = new StubGhCli();
        var snippet = """
            $config = @{ appId = 5130401; installationId = 166366113; repositories = @('acme/one', 'acme/two'); permissions = @{ issues = 'read' } }
            $resolved = Resolve-GitHubToken -AppConfig $config
            Write-Output "$($resolved.Source) $($resolved.Token)"
            """;

        var result = Snippet(snippet, api, ghCli, Key(key), ("FAKE_GH_TOKEN", "cli-token"));

        result.Output.Trim().ShouldBe($"app {StubGitHubApi.AppToken}");
        api.Requests.ShouldHaveSingleItem().PathOnly.ShouldBe("/app/installations/166366113/access_tokens");
    }

    [Test]
    public void ShouldFallBackToTheCliWithoutLeaking_WhenTheMintIsRefused()
    {
        using var key = new GeneratedAppKey();
        using var api = new StubGitHubApi { MintStatus = 403 };
        using var ghCli = new StubGhCli();
        var snippet = """
            $config = @{ appId = 5130401; installationId = 166366113; repositories = @('acme/one'); permissions = @{ issues = 'read' } }
            $resolved = Resolve-GitHubToken -AppConfig $config
            Write-Output "$($resolved.Source)"
            """;

        var result = Snippet(snippet, api, ghCli, Key(key), ("FAKE_GH_TOKEN", "cli-token"));

        result.Output.ShouldContain("github-app: GitHub App 5130401: no installation token (HTTP 403). Falling back to the GitHub CLI token.");
        result.Output.TrimEnd().ShouldEndWith("gh");
        AssertNoSecrets(result, key, api.Requests[0].Bearer);
    }

    private static (string Name, string Value) Key(GeneratedAppKey key) => ("AISF_BOARD_APP_PRIVATE_KEY_PATH", key.Path);

    private static JsonNode Json(string base64Url) => JsonNode.Parse(Encoding.UTF8.GetString(System.Buffers.Text.Base64Url.DecodeFromChars(base64Url)))!;

    private static void AssertNoSecrets(ProcessResult result, GeneratedAppKey key, params string[] secrets)
    {
        var transcript = result.Transcript;
        transcript.ShouldNotContain("BEGIN");
        transcript.ShouldNotContain(key.BodyFragment);
        transcript.ShouldNotContain(StubGitHubApi.AppToken);
        foreach (var secret in secrets.Where(secret => secret.Length > 0))
        {
            transcript.ShouldNotContain(secret);
        }
    }

    private ProcessResult Resolve(StubGhCli ghCli, params (string Name, string Value)[] set) => Snippet(
        "$resolved = Resolve-GitHubToken; if ($resolved) { Write-Output \"$($resolved.Source) $($resolved.Token)\" } else { Write-Output 'none' }",
        null,
        ghCli,
        set);

    private ProcessResult Snippet(string body, params (string Name, string Value)[] set) => Snippet(body, null, null, set);

    private ProcessResult Snippet(string body, StubGitHubApi api, params (string Name, string Value)[] set) => Snippet(body, api, null, set);

    private ProcessResult Snippet(string body, Action<Dictionary<string, string?>> adjust, params (string Name, string Value)[] set)
    {
        var environment = GitHubScriptHost.Environment(null, null, set);
        adjust(environment);
        return RunSnippet(body, environment);
    }

    private ProcessResult Snippet(string body, StubGitHubApi? api, StubGhCli? ghCli, params (string Name, string Value)[] set) =>
        RunSnippet(body, GitHubScriptHost.Environment(api, ghCli, set));

    private ProcessResult RunSnippet(string body, Dictionary<string, string?> environment)
    {
        var script = Path.Join(_scratch, $"snippet-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(script, $"$ErrorActionPreference = 'Stop'\n. '{GitHubScriptHost.Script("scripts/github/GitHubAppAuth.ps1")}'\n{body}\n");
        return GitHubScriptHost.Run(script, environment);
    }
}
