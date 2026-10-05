using Shouldly;

namespace ClearMeasure.Bootcamp.UnitTests.BuildGates;

[TestFixture]
public class CodecovUploadTests
{
    private const string CodecovAction = "uses: codecov/codecov-action@";

    [Test]
    public void ShouldUploadCoverageToCodecov_WhenBuildWorkflowIsRead()
    {
        ReadCodecovSteps().ShouldNotBeEmpty();
    }

    [Test]
    public void ShouldNotFailTheJob_WhenCodecovUploadFails()
    {
        var blockingSteps = ReadCodecovSteps()
            .Where(step => !step.Any(line => line.Trim() == "continue-on-error: true"))
            .Select(step => step[0].Trim())
            .ToArray();

        blockingSteps.ShouldBeEmpty();
    }

    private static List<string[]> ReadCodecovSteps()
    {
        var workflow = Path.Combine(FindRepoRoot(), ".github", "workflows", "build.yml");
        var lines = File.ReadAllLines(workflow);
        var steps = new List<string[]>();
        for (var index = 0; index < lines.Length; index++)
        {
            if (!lines[index].TrimStart().StartsWith(CodecovAction, StringComparison.Ordinal))
            {
                continue;
            }

            var start = index;
            while (start > 0 && !lines[start].TrimStart().StartsWith("- ", StringComparison.Ordinal))
            {
                start--;
            }

            var stepIndent = lines[start].Length - lines[start].TrimStart().Length;
            var end = index + 1;
            while (end < lines.Length && !IsNextItemOrOutdent(lines[end], stepIndent))
            {
                end++;
            }

            steps.Add(lines[start..end]);
        }

        return steps;
    }

    private static bool IsNextItemOrOutdent(string line, int stepIndent)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var indent = line.Length - line.TrimStart().Length;
        return indent < stepIndent || (indent == stepIndent && !line.TrimStart().StartsWith('#'));
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
