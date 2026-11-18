using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace MultiBlox;

/// <summary>
/// Roblox's own settings file (%LOCALAPPDATA%\Roblox\GlobalBasicSettings_13.xml) - the same values as the in-game
/// Settings menu: graphics quality, volume, max frame rate. Every Roblox window reads it when it starts.
/// </summary>
static class RobloxSettings
{
    // XML element type for each value MultiBlox touches.
    static readonly Dictionary<string, string> Types = new()
    {
        ["SavedQualityLevel"] = "token",
        ["GraphicsQualityLevel"] = "int",
        ["MasterVolume"] = "float",
        ["FramerateCap"] = "int",
    };

    public static string FilePath()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox");
        if (!Directory.Exists(dir)) return null;
        return Directory.GetFiles(dir, "GlobalBasicSettings_*.xml")
            .Where(f => !Path.GetFileName(f).Contains("Studio", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }

    /// <summary>Sets values (null = remove the setting so Roblox uses its default). Returns what each had before (null = wasn't there).</summary>
    public static Dictionary<string, string> Set(IReadOnlyDictionary<string, string> values)
    {
        string path = FilePath() ?? throw new Exception("Roblox's settings file wasn't found - open Roblox once, then try again.");
        var doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var props = doc.Descendants("Properties").FirstOrDefault() ?? throw new Exception("Roblox's settings file isn't in the expected format.");
        var old = new Dictionary<string, string>();
        foreach (var (name, value) in values)
        {
            var el = props.Elements().FirstOrDefault(e => (string)e.Attribute("name") == name);
            old[name] = el?.Value;
            if (value == null) el?.Remove();
            else if (el != null) el.Value = value;
            else props.Add(new XElement(Types.TryGetValue(name, out var t) ? t : "string", new XAttribute("name", name), value));
        }

        // Some people make the file read-only to lock their settings; respect that by putting the flag back.
        var attrs = File.GetAttributes(path);
        bool readOnly = attrs.HasFlag(FileAttributes.ReadOnly);
        if (readOnly) File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
        try
        {
            string tmp = path + ".multiblox.tmp";
            var ws = new XmlWriterSettings { OmitXmlDeclaration = true, Encoding = new UTF8Encoding(false), Indent = false, NewLineHandling = NewLineHandling.None };
            using (var w = XmlWriter.Create(tmp, ws)) doc.Save(w);
            File.Move(tmp, path, overwrite: true);
        }
        finally { if (readOnly) File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly); }
        return old;
    }
}
