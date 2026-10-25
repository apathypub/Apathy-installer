using System.IO;
using Apathy.Models;

namespace Apathy.Services;

/// <summary>apathy.log next to the exe (or in %TEMP% when that folder is read-only).</summary>
public static class Log
{
    public static string FilePath { get; private set; } = Path.Combine(Catalog.AppFolder, "apathy.log");

    public static void Write(string text)
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}{Environment.NewLine}";
        try
        {
            File.AppendAllText(FilePath, line);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FilePath = Path.Combine(Path.GetTempPath(), "apathy.log");
            try { File.AppendAllText(FilePath, line); } catch (IOException) { }
        }
    }
}
