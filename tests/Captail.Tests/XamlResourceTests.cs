using System.Text.RegularExpressions;
using System.Globalization;
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

    [Fact]
    public void ServiceIconsUseAConsistentLightStroke()
    {
        string sourceDirectory = FindSourceDirectory();
        string[] xamlFiles = Directory.GetFiles(sourceDirectory, "*.xaml", SearchOption.AllDirectories);
        var filledIcons = new HashSet<string>(StringComparer.Ordinal)
        {
            "IconGitHub",
            "IconRecord",
            "IconStop"
        };

        var violations = new List<string>();
        var pathPattern = new Regex("<Path\\b[^>]*Data=\"\\{StaticResource\\s+(Icon[^}\\s]+)\\}\"[^>]*/?>", RegexOptions.Singleline);

        foreach (string path in xamlFiles)
        {
            string xaml = File.ReadAllText(path);
            foreach (Match match in pathPattern.Matches(xaml))
            {
                string iconKey = match.Groups[1].Value;
                if (filledIcons.Contains(iconKey))
                    continue;

                string element = match.Value;
                Match thicknessMatch = Regex.Match(element, "StrokeThickness=\"([0-9.]+)\"");
                if (!element.Contains("Stroke=", StringComparison.Ordinal) || !thicknessMatch.Success)
                {
                    violations.Add($"{Path.GetRelativePath(sourceDirectory, path)}: {iconKey} must use an explicit stroke.");
                    continue;
                }

                double thickness = double.Parse(thicknessMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                if (thickness > 1.7)
                    violations.Add($"{Path.GetRelativePath(sourceDirectory, path)}: {iconKey} uses StrokeThickness {thickness:0.##}.");

                if (element.Contains("Fill=", StringComparison.Ordinal))
                    violations.Add($"{Path.GetRelativePath(sourceDirectory, path)}: {iconKey} must not be rendered as a filled icon.");
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void HeaderUsesThePackagedBrandIconInsteadOfReconstructedShapes()
    {
        string settingsPath = Path.Combine(FindSourceDirectory(), "SettingsWindow.xaml");
        string xaml = File.ReadAllText(settingsPath);

        Assert.Contains(
            "Source=\"{StaticResource BrandMark24}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "<Rectangle Stroke=\"{StaticResource AccentBrush}\"",
            xaml,
            StringComparison.Ordinal);
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
