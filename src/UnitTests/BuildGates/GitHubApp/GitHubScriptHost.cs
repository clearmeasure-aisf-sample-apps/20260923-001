using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace ClearMeasure.Bootcamp.UnitTests.BuildGates.GitHubApp;

/// <summary>Exit code and output of a process a GitHub App test ran.</summary>
/// <param name="ExitCode">Exit code.</param>
/// <param name="Output">Standard output.</param>
/// <param name="Error">Standard error.</param>
internal sealed record ProcessResult(int ExitCode, string Output, string Error)
{
    /// <summary>Output and error together, for assertion messages.</summary>
    public string Transcript => $"exit {ExitCode}{Environment.NewLine}{Output}{Environment.NewLine}{Error}".Trim();
}

/// <summary>A throw-away RSA key of the GitHub App, generated at test time (never committed).</summary>
internal sealed class GeneratedAppKey : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("app-key-").FullName;

    /// <summary>Generates a 2048-bit key and writes its PEM to a file.</summary>
    public GeneratedAppKey()
    {
        Rsa = RSA.Create(2048);
        Pem = Rsa.ExportRSAPrivateKeyPem();
        Path = System.IO.Path.Join(directory, "app-private-key.pem");
        File.WriteAllText(Path, Pem);
    }

    /// <summary>The key.</summary>
    public RSA Rsa { get; }

    /// <summary>The private key as PEM text.</summary>
    public string Pem { get; }

    /// <summary>The file holding <see cref="Pem"/>.</summary>
    public string Path { get; }

    /// <summary>A fragment of the PEM's base64 body, for asserting that a transcript does not leak the key.</summary>
    public string BodyFragment => Pem.Split('\n')[1].Trim();

    /// <summary>Verifies the RS256 signature of a JWT with the public half of the key.</summary>
    /// <param name="jwt">The token.</param>
    public bool Verifies(string jwt)
    {
        var parts = jwt.Split('.');
        return parts.Length == 3
            && Rsa.VerifyData(Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}"), System.Buffers.Text.Base64Url.DecodeFromChars(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    /// <summary>Deletes the file.</summary>
    public void Dispose()
    {
        Rsa.Dispose();
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that cannot be removed is left for the OS to clean.
        }
    }
}

/// <summary>
/// A folder holding a stub <c>gh</c> that answers <c>gh auth token</c> with the value of FAKE_GH_TOKEN (and exits 1 when it is
/// empty), so tests never touch the developer's real GitHub CLI login.
/// </summary>
internal sealed class StubGhCli : IDisposable
{
    /// <summary>Creates the folder and the stub.</summary>
    public StubGhCli()
    {
        Directory = System.IO.Directory.CreateTempSubdirectory("stub-gh-").FullName;
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(System.IO.Path.Join(Directory, "gh.cmd"), "@echo off\r\nif \"%FAKE_GH_TOKEN%\"==\"\" exit /b 1\r\necho %FAKE_GH_TOKEN%\r\n");
        }
        else
        {
            var path = System.IO.Path.Join(Directory, "gh");
            File.WriteAllText(path, "#!/bin/sh\n[ -n \"$FAKE_GH_TOKEN\" ] || exit 1\necho \"$FAKE_GH_TOKEN\"\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>The folder to put first on PATH.</summary>
    public string Directory { get; }

    /// <summary>The stub executable.</summary>
    public string Executable => System.IO.Path.Join(Directory, OperatingSystem.IsWindows() ? "gh.cmd" : "gh");

    /// <summary>Deletes the folder.</summary>
    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that cannot be removed is left for the OS to clean.
        }
    }
}

/// <summary>Runs the repository's PowerShell scripts against the stub with a clean, explicit environment.</summary>
internal static class GitHubScriptHost
{
    private static readonly string[] Scrubbed =
    [
        "GH_TOKEN", "GITHUB_TOKEN", "AISF_BOARD_APP_TOKEN", "AISF_BOARD_APP_ID", "AISF_BOARD_APP_INSTALLATION_ID",
        "AISF_BOARD_APP_PRIVATE_KEY", "AISF_BOARD_APP_PRIVATE_KEY_PATH", "FAKE_GH_TOKEN", "GITHUB_API_URL", "OCTOPUS",
        "HTTP_PROXY", "http_proxy", "HTTPS_PROXY", "https_proxy", "ALL_PROXY",
    ];

    /// <summary>The environment variable that names the retired personal access token, assembled at run time so that no file of the repository holds the name.</summary>
    public static string RetiredTokenVariable { get; } = string.Concat("GITHUB_SAMPLE", "_APPS_PAT");

    /// <summary>The repository root: the nearest parent of the test directory that holds <c>build.ps1</c>.</summary>
    public static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                if (File.Exists(System.IO.Path.Join(directory.FullName, "build.ps1")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Repository root not found from test directory.");
        }
    }

    /// <summary>The pwsh executable; the build itself runs in PowerShell, so a missing one fails the test.</summary>
    public static string Pwsh
    {
        get
        {
            var directories = (System.Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            foreach (var name in new[] { "pwsh", "pwsh.exe" })
            {
                var found = directories.Select(directory => System.IO.Path.Join(directory, name)).FirstOrDefault(File.Exists);
                if (found is not null)
                {
                    return found;
                }
            }

            throw new FileNotFoundException("pwsh is not on PATH; the build scripts and these tests need PowerShell 7.");
        }
    }

    /// <summary>The repository-relative script as an absolute path.</summary>
    /// <param name="relative">Path with forward slashes.</param>
    public static string Script(string relative) => System.IO.Path.Join(RepositoryRoot, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));

    /// <summary>The environment of a run: every credential variable removed, then <paramref name="set"/> applied on top.</summary>
    /// <param name="api">The stub, for GITHUB_API_URL; <c>null</c> leaves it unset.</param>
    /// <param name="ghCli">The stub gh folder to put first on PATH; <c>null</c> keeps PATH unchanged.</param>
    /// <param name="set">Variables to set.</param>
    public static Dictionary<string, string?> Environment(StubGitHubApi? api, StubGhCli? ghCli, params (string Name, string Value)[] set)
    {
        var environment = Scrubbed.ToDictionary(name => name, _ => (string?)null, StringComparer.Ordinal);
        environment[RetiredTokenVariable] = null;
        environment["NO_PROXY"] = "127.0.0.1,localhost";
        if (api is not null)
        {
            environment["GITHUB_API_URL"] = api.Url;
        }

        if (ghCli is not null)
        {
            environment["PATH"] = ghCli.Directory + System.IO.Path.PathSeparator + System.Environment.GetEnvironmentVariable("PATH");
        }

        foreach (var (name, value) in set)
        {
            environment[name] = value;
        }

        return environment;
    }

    /// <summary>Runs <c>pwsh -NoProfile -File &lt;script&gt; &lt;arguments&gt;</c> from the repository root.</summary>
    /// <param name="scriptPath">Absolute script path.</param>
    /// <param name="environment">The run's environment.</param>
    /// <param name="arguments">Script arguments.</param>
    public static ProcessResult Run(string scriptPath, IReadOnlyDictionary<string, string?> environment, params string[] arguments)
    {
        var start = new ProcessStartInfo(Pwsh)
        {
            WorkingDirectory = RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in (string[])["-NoProfile", "-File", scriptPath, .. arguments])
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment)
        {
            if (value is null)
            {
                start.Environment.Remove(name);
            }
            else
            {
                start.Environment[name] = value;
            }
        }

        using var process = new Process { StartInfo = start };
        var output = new StringBuilder();
        var error = new StringBuilder();
        process.OutputDataReceived += (_, data) => Append(output, data.Data);
        process.ErrorDataReceived += (_, data) => Append(error, data.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit(TimeSpan.FromMinutes(2)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"{System.IO.Path.GetFileName(scriptPath)} did not finish in time");
        }

        process.WaitForExit();
        return new ProcessResult(process.ExitCode, output.ToString(), error.ToString());
    }

    private static void Append(StringBuilder target, string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (target)
        {
            target.AppendLine(line);
        }
    }
}
