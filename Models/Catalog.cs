using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Apathy.Models;

// Shape of programs.json: category (icon tab) -> subcategory (group box) -> program (checkbox).

[DataContract]
public sealed class Category
{
    [DataMember(Name = "category")] public string Name { get; set; } = "";
    /// <summary>Segoe MDL2 Assets glyph as hex, e.g. "E80F".</summary>
    [DataMember(Name = "icon")] public string Icon { get; set; } = "E71D";
    [DataMember(Name = "groups")] public List<Subcategory> Groups { get; set; } = new();
    /// <summary>Shows the "This PC" group (detected CPU / GPU / motherboard) on this tab.</summary>
    [DataMember(Name = "hardware")] public bool DetectHardware { get; set; }
}

[DataContract]
public sealed class Subcategory
{
    [DataMember(Name = "name")] public string Name { get; set; } = "";
    [DataMember(Name = "programs")] public List<Program> Programs { get; set; } = new();
}

[DataContract]
public sealed class Program
{
    [DataMember(Name = "name")] public string Name { get; set; } = "";
    /// <summary>winget package id, e.g. "Google.Chrome".</summary>
    [DataMember(Name = "id")] public string Id { get; set; } = "";
    [DataMember(Name = "recommended")] public bool Recommended { get; set; }
    /// <summary>Installer refuses to run elevated (Spotify), so it is started with a normal user token.</summary>
    [DataMember(Name = "noAdmin")] public bool NoAdmin { get; set; }
    /// <summary>
    /// Official download page. Opened in the browser when the program isn't in winget, or when a direct
    /// download ("file" / "page") fails.
    /// </summary>
    [DataMember(Name = "url")] public string? Url { get; set; }
    /// <summary>Hardware this is for, as "part:word" (part = cpu, gpu or board), e.g. "gpu:nvidia".</summary>
    [DataMember(Name = "detect")] public List<string>? Detect { get; set; }

    // Direct downloads, for programs that aren't in winget (NVIDIA app, AMD Adrenalin, Windows, Linux ISOs).

    /// <summary>Fixed link to the installer or ISO.</summary>
    [DataMember(Name = "file")] public string? File { get; set; }
    /// <summary>Vendor page that links to the newest installer; "find" says which link to take.</summary>
    [DataMember(Name = "page")] public string? Page { get; set; }
    /// <summary>
    /// Regexes applied one after another, starting on "page". Each one picks the link with the highest version
    /// (group 1 if the regex has one, otherwise the whole match); the last one is the file.
    /// </summary>
    [DataMember(Name = "find")] public List<string>? Find { get; set; }
    /// <summary>Only save the file (ISO images) instead of running it.</summary>
    [DataMember(Name = "save")] public bool Save { get; set; }
    /// <summary>Command line for the downloaded installer, e.g. silent switches.</summary>
    [DataMember(Name = "args")] public string? Args { get; set; }

    public bool IsDirect => !string.IsNullOrEmpty(File) || !string.IsNullOrEmpty(Page);
}

public static class Catalog
{
    public const string FileName = "programs.json";

    /// <summary>programs.json next to the exe if there is one (so the list can be edited), otherwise the built-in list.</summary>
    public static List<Category> Load(out string source, out string? error)
    {
        error = null;
        string external = Path.Combine(AppFolder, FileName);
        if (File.Exists(external))
        {
            try
            {
                using var file = File.OpenRead(external);
                var list = Read(file);
                if (list.Count > 0)
                {
                    source = external;
                    return list;
                }
                error = "the file is empty";
            }
            catch (Exception ex) when (ex is IOException or SerializationException or UnauthorizedAccessException)
            {
                error = ex.Message;
            }
        }

        source = "built-in list";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(FileName)!;
        return Read(stream);
    }

    public static string AppFolder => AppDomain.CurrentDomain.BaseDirectory;

    private static List<Category> Read(Stream stream)
    {
        // Re-encode without a BOM (Notepad adds one) before handing the text to the serializer.
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        byte[] json = new UTF8Encoding(false).GetBytes(reader.ReadToEnd());
        var serializer = new DataContractJsonSerializer(typeof(List<Category>));
        using var memory = new MemoryStream(json);
        var list = (List<Category>?)serializer.ReadObject(memory) ?? new List<Category>();
        // Members missing from the file come back null: the serializer skips constructors.
        foreach (var category in list)
        {
            category.Groups ??= new List<Subcategory>();
            category.Icon ??= "E71D";
            foreach (var group in category.Groups) group.Programs ??= new List<Program>();
        }
        return list;
    }
}
