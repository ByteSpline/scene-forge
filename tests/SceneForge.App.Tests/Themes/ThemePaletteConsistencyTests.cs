using System.IO;
using System.Xml.Linq;

namespace SceneForge.App.Tests.Themes;

// The light/dark-safe theming contract (Phase 10, kept through the Phase 17
// UI polish): every color the app uses resolves through a shared semantic
// brush key that is defined in BOTH palette dictionaries. If the two files
// ever drift apart, a DynamicResource lookup silently falls through to
// nothing under one theme - these tests fail fast instead.
public class ThemePaletteConsistencyTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void LightAndDarkPalettes_DefineExactlyTheSameBrushKeys()
    {
        var light = BrushKeys("Colors.Light.xaml");
        var dark = BrushKeys("Colors.Dark.xaml");

        Assert.Equal(light, dark);
    }

    [Fact]
    public void BothPalettes_DefineEveryKeyReferencedByStyles()
    {
        var defined = BrushKeys("Colors.Light.xaml");
        var referenced = ReferencedBrushKeys("Styles.xaml");

        var missing = referenced.Except(defined).ToList();

        Assert.True(missing.Count == 0, $"Styles.xaml references undefined brush keys: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Palettes_AreNotEmpty()
    {
        Assert.NotEmpty(BrushKeys("Colors.Light.xaml"));
    }

    private static SortedSet<string> BrushKeys(string themeFile)
    {
        var doc = XDocument.Load(ThemeFilePath(themeFile));
        var keys = doc.Descendants()
            .Where(e => e.Name.LocalName == "SolidColorBrush")
            .Select(e => (string?)e.Attribute(Xaml + "Key"))
            .Where(k => k is not null)
            .Select(k => k!);
        return new SortedSet<string>(keys, StringComparer.Ordinal);
    }

    private static SortedSet<string> ReferencedBrushKeys(string styleFile)
    {
        var text = File.ReadAllText(ThemeFilePath(styleFile));
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        const string marker = "DynamicResource Brush.";
        var index = text.IndexOf(marker, StringComparison.Ordinal);
        while (index >= 0)
        {
            var start = index + "DynamicResource ".Length;
            var end = start;
            while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '.'))
            {
                end++;
            }

            keys.Add(text[start..end]);
            index = text.IndexOf(marker, end, StringComparison.Ordinal);
        }

        return keys;
    }

    private static string ThemeFilePath(string fileName)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "SceneForge.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!, "src", "SceneForge.App", "Themes", fileName);
    }
}
