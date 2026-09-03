using System.Text.RegularExpressions;
using Xunit;

namespace Captail.Tests;

public sealed class XamlResourceTests
{
    [Fact]
    public void StaticResourcesHaveMatchingKeys()
    {
        string sourceDirectory = FindSourceDirectory();
        string[] xamlFiles = Directory.GetFiles(
            sourceDirectory,
            "*.xaml",
            SearchOption.AllDirectories);

        var definedKeys = xamlFiles
            .SelectMany(path => Regex.Matches(
                File.ReadAllText(path),
                "x:Key=\"([^\"]+)\""))
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        string[] missingKeys = xamlFiles
            .SelectMany(path => Regex.Matches(
                    File.ReadAllText(path),
                    "\\{StaticResource\\s+([^}\\s]+)\\}")
                .Select(match => match.Groups[1].Value))
            .Where(key => !definedKeys.Contains(key))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(missingKeys);
    }

    private static string FindSourceDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "Captail");
            if (Directory.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate src/Captail.");
    }
}
