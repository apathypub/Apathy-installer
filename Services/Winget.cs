using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Apathy.Services;

/// <summary>Runs winget (Windows Package Manager). Programs come straight from their official sources.</summary>
public static class Winget
{
    // winget exit codes that mean "nothing to do": no newer version / already installed.
    private const int UpdateNotApplicable = unchecked((int)0x8A15002B);
    private const int PackageAlreadyInstalled = unchecked((int)0x8A150061);

    private static readonly string[] Common = { "--accept-source-agreements", "--disable-interactivity" };

    public static bool IsAlreadyInstalled(int exitCode) =>
        exitCode is UpdateNotApplicable or PackageAlreadyInstalled;

    /// <param name="version">A version from <see cref="VersionsAsync"/>, or null for the latest.</param>
    public static Task<int> InstallAsync(string id, string? version, Action<string> onLine, Action<Progress>? onProgress,
        CancellationToken token) =>
        RunProcessAsync("winget.exe",
            new[] { "install", "--id", id, "-e", "-s", "winget", "--silent", "--accept-package-agreements" }
                .Concat(VersionArgs(version)).Concat(Common),
            onLine, token, onProgress: onProgress);

    public static Task<int> DownloadAsync(string id, string? version, string folder, Action<string> onLine,
        Action<Progress>? onProgress, CancellationToken token) =>
        RunProcessAsync("winget.exe",
            new[] { "download", "--id", id, "-e", "-s", "winget", "-d", folder, "--accept-package-agreements" }
                .Concat(VersionArgs(version)).Concat(Common),
            onLine, token, onProgress: onProgress);

    private static string[] VersionArgs(string? version) =>
        string.IsNullOrEmpty(version) ? new string[0] : new[] { "--version", version! };

    /// <summary>Every version winget has for a package, newest first.</summary>
    public static async Task<List<string>> VersionsAsync(string id, CancellationToken token)
    {
        var lines = new List<string>();
        await RunProcessAsync("winget.exe",
            new[] { "show", "--id", id, "-e", "-s", "winget", "--versions" }.Concat(Common),
            line => { lock (lines) lines.Add(line); }, token, raw: true);

        // "Found X [id]", a header in the Windows language, "-------", then one version per line.
        int dashes = lines.FindIndex(l => l.Trim().Length > 3 && l.Trim().All(c => c == '-'));
        if (dashes < 0) return new List<string>();
        return lines.Skip(dashes + 1)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && l.IndexOf(' ') < 0 && l.Any(char.IsDigit))
            .ToList();
    }

    public static Task<int> UpgradeAllAsync(Action<string> onLine, Action<Progress>? onProgress, CancellationToken token) =>
        RunProcessAsync("winget.exe",
            new[] { "upgrade", "--all", "--silent", "--include-unknown", "--accept-package-agreements" }.Concat(Common),
            onLine, token, onProgress: onProgress);

    /// <summary>One redraw of a winget progress bar: "████▒▒▒  52.0 MB / 120 MB" or "████▒▒▒  45%".</summary>
    public sealed class Progress
    {
        public double Fraction { get; set; }
        /// <summary>Bytes downloaded so far, when the bar shows sizes (0 for percent-only bars).</summary>
        public double Done { get; set; }
        public double Total { get; set; }
    }

    private static readonly Regex SizeBar = new(@"([\d.,]+)\s*(B|KB|MB|GB)\s*/\s*([\d.,]+)\s*(B|KB|MB|GB)", RegexOptions.IgnoreCase);
    private static readonly Regex PercentBar = new(@"(\d{1,3})\s*%");

    private static Progress? ParseProgress(string line)
    {
        var size = SizeBar.Match(line);
        if (size.Success)
        {
            double done = Bytes(size.Groups[1].Value, size.Groups[2].Value);
            double total = Bytes(size.Groups[3].Value, size.Groups[4].Value);
            if (total > 0) return new Progress { Fraction = Math.Min(1, done / total), Done = done, Total = total };
        }
        var percent = PercentBar.Match(line);
        if (percent.Success && int.TryParse(percent.Groups[1].Value, out int value) && value <= 100)
        {
            return new Progress { Fraction = value / 100.0 };
        }
        return null;
    }

    private static double Bytes(string number, string unit)
    {
        // winget may print "52,0" on Windows set to a comma-decimal language.
        if (!double.TryParse(number.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            return 0;
        return unit.ToUpperInvariant() switch
        {
            "KB" => value * 1024,
            "MB" => value * 1024 * 1024,
            "GB" => value * 1024 * 1024 * 1024,
            _ => value,
        };
    }

    // ---------- Fast downloads ----------

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        @"Packages\Microsoft.DesktopAppInstaller_8wekyb3d8bbwe\LocalState\settings.json");

    /// <summary>
    /// By default winget downloads through Delivery Optimization, which is often much slower than a direct download.
    /// This sets "network": { "downloader": "wininet" } in winget's settings.json (a backup is kept as settings.json.bak).
    /// A downloader the user already chose is left alone.
    /// </summary>
    public static void UseFastDownloads(Action<string> log)
    {
        try
        {
            string path = SettingsPath;
            var serializer = new JavaScriptSerializer();
            var root = new Dictionary<string, object>();
            if (File.Exists(path))
            {
                // The default file ends with a trailing comma before "}", which the serializer rejects.
                string json = Regex.Replace(StripComments(File.ReadAllText(path)), @",(\s*[}\]])", "$1");
                if (json.Trim().Length > 0 && serializer.DeserializeObject(json) is Dictionary<string, object> existing)
                {
                    root = existing;
                }
                if (root.TryGetValue("network", out object? net) && net is Dictionary<string, object> network
                    && network.TryGetValue("downloader", out object? current))
                {
                    log($"winget downloader is already set to \"{current}\".");
                    return;
                }
                File.Copy(path, path + ".bak", overwrite: true);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            }

            if (!(root.TryGetValue("network", out object? value) && value is Dictionary<string, object> settings))
            {
                settings = new Dictionary<string, object>();
                root["network"] = settings;
            }
            settings["downloader"] = "wininet";
            if (!root.ContainsKey("$schema")) root["$schema"] = "https://aka.ms/winget-settings.schema.json";
            File.WriteAllText(path, serializer.Serialize(root));
            log("Fast downloads on: winget now downloads directly instead of through Delivery Optimization.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or InvalidOperationException)
        {
            log("Could not switch winget to fast downloads: " + ex.Message);
        }
    }

    /// <summary>winget's settings.json allows // and /* */ comments; the serializer doesn't.</summary>
    private static string StripComments(string json)
    {
        var result = new StringBuilder(json.Length);
        bool inString = false;
        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];
            if (inString)
            {
                result.Append(c);
                if (c == '\\' && i + 1 < json.Length) result.Append(json[++i]);
                else if (c == '"') inString = false;
            }
            else if (c == '"')
            {
                inString = true;
                result.Append(c);
            }
            else if (c == '/' && i + 1 < json.Length && json[i + 1] == '/')
            {
                while (i < json.Length && json[i] != '\n') i++;
                result.Append('\n');
            }
            else if (c == '/' && i + 1 < json.Length && json[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < json.Length && !(json[i] == '*' && json[i + 1] == '/')) i++;
                i++;
            }
            else
            {
                result.Append(c);
            }
        }
        return result.ToString();
    }

    /// <summary>
    /// Searches the whole winget catalog. winget prints a table (headers are in the Windows language),
    /// so columns are cut at the positions of the header words above the "-----" line.
    /// </summary>
    public static async Task<List<(string Name, string Id)>> SearchAsync(string query, CancellationToken token)
    {
        var lines = new List<string>();
        await RunProcessAsync("winget.exe",
            new[] { "search", query, "-s", "winget", "--count", "40" }.Concat(Common),
            line => { lock (lines) lines.Add(line); }, token, raw: true);

        var results = new List<(string Name, string Id)>();
        int dashes = lines.FindIndex(l => l.Trim().Length > 10 && l.Trim().All(c => c == '-'));
        if (dashes < 1) return results;

        string header = lines[dashes - 1].TrimEnd();
        var starts = new List<int> { 0 };
        for (int i = 1; i < header.Length; i++)
        {
            if (header[i] != ' ' && header[i - 1] == ' ') starts.Add(i);
        }
        if (starts.Count < 2) return results;
        int idStart = starts[1];
        int idEnd = starts.Count > 2 ? starts[2] : int.MaxValue;

        foreach (string row in lines.Skip(dashes + 1))
        {
            if (row.Length <= idStart) continue;
            string name = row.Substring(0, idStart).Trim();
            string id = row.Substring(idStart, Math.Min(idEnd, row.Length) - idStart).Trim();
            // Ids cut short with "…" can't be installed.
            if (name.Length == 0 || id.Length == 0 || id.IndexOf(' ') >= 0 || id.IndexOf('…') >= 0) continue;
            results.Add((name, id));
        }
        return results;
    }

    /// <summary>
    /// Installers that refuse to run as administrator (Spotify) are started with a normal user token.
    /// runas returns at once, so the result isn't known; the installer finishes on its own.
    /// </summary>
    public static void InstallAsNormalUser(string id, string? version)
    {
        string command = $"winget install --id {id} -e -s winget --silent --accept-package-agreements --accept-source-agreements";
        if (!string.IsNullOrEmpty(version)) command += $" --version {version}";
        var info = new ProcessStartInfo("runas.exe", $"/trustlevel:0x20000 \"{command}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        Process.Start(info);
    }

    public static async Task<bool> IsAvailableAsync()
    {
        try
        {
            return await RunAsync(new[] { "--version" }, _ => { }, CancellationToken.None) == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    /// <summary>Registers or installs winget when it's missing (older Windows 10, fresh installs).</summary>
    public static async Task<bool> EnsureAsync(Action<string> onLine)
    {
        if (await IsAvailableAsync()) return true;

        onLine("winget is missing - trying to enable it...");
        await PowerShellAsync("Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe", onLine);
        if (await IsAvailableAsync()) return true;

        onLine("Installing winget from the official Microsoft module (this can take a few minutes)...");
        await PowerShellAsync(
            "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; " +
            "Install-PackageProvider -Name NuGet -Force -Scope AllUsers | Out-Null; " +
            "Install-Module -Name Microsoft.WinGet.Client -Force -Scope AllUsers -Repository PSGallery | Out-Null; " +
            "Import-Module Microsoft.WinGet.Client; Repair-WinGetPackageManager -AllUsers -Latest", onLine);
        return await IsAvailableAsync();
    }

    private static async Task PowerShellAsync(string script, Action<string> onLine)
    {
        try
        {
            await RunProcessAsync("powershell.exe",
                new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", script }, onLine, CancellationToken.None);
        }
        catch (Win32Exception ex)
        {
            onLine("Could not start PowerShell: " + ex.Message);
        }
    }

    private static Task<int> RunAsync(IEnumerable<string> args, Action<string> onLine, CancellationToken token) =>
        RunProcessAsync("winget.exe", args, onLine, token);

    private static async Task<int> RunProcessAsync(string file, IEnumerable<string> args, Action<string> onLine,
        CancellationToken token, bool raw = false, Action<Progress>? onProgress = null)
    {
        var info = new ProcessStartInfo(file, string.Join(" ", args.Select(Quote)))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.Exited += (_, _) => exited.TrySetResult(true);
        process.Start();
        var reading = Task.WhenAll(Pump(process.StandardOutput, onLine, raw, onProgress),
            Pump(process.StandardError, onLine, raw, onProgress));

        using (token.Register(() => KillTree(process.Id)))
        {
            await exited.Task;
        }
        await reading;
        process.WaitForExit();
        token.ThrowIfCancellationRequested();
        return process.ExitCode;
    }

    // Stops the current installer too, not only the queue.
    internal static void KillTree(int pid)
    {
        try
        {
            Process.Start(new ProcessStartInfo("taskkill.exe", $"/PID {pid} /T /F")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Win32Exception)
        {
        }
    }

    private static string Quote(string arg) =>
        arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '"', '\t' }) < 0 ? arg : "\"" + arg.Replace("\"", "\\\"") + "\"";

    // winget redraws progress bars and spinners with \r; keep only lines with real text.
    private static async Task Pump(StreamReader reader, Action<string> onLine, bool raw, Action<Progress>? onProgress)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            if (raw)
            {
                onLine(line);
                continue;
            }
            string text = line.Trim();
            bool bar = text.IndexOf('█') >= 0 || text.IndexOf('▒') >= 0;
            if (bar)
            {
                if (onProgress != null && ParseProgress(text) is { } progress) onProgress(progress);
            }
            else if (text.Length > 1 && text.Any(char.IsLetter))
            {
                onLine(text);
            }
        }
    }
}
