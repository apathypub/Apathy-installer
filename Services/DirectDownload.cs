using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using Apathy.Models;

namespace Apathy.Services;

/// <summary>
/// Downloads programs that aren't in winget straight from the vendor: a fixed link ("file"), or the newest
/// link found on the vendor's download page ("page" + "find").
/// </summary>
public static class DirectDownload
{
    // Some vendor sites (AMD) refuse requests that don't look like a browser.
    private const string Browser =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36";
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);
    private static readonly Regex VersionNumber = new(@"\d+(?:\.\d+)*");

    // Redirects are followed by hand: HttpClient on .NET Framework won't follow https -> http (Kali mirrors).
    private static readonly HttpClient Client = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
    })
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    /// <summary>The file to download, and the page it was found on (sent as Referer; AMD requires it).</summary>
    public static async Task<(Uri File, Uri? Referer)> ResolveAsync(Program program, CancellationToken token)
    {
        if (string.IsNullOrEmpty(program.Page)) return (new Uri(program.File!), null);

        var steps = program.Find ?? new List<string>();
        if (steps.Count == 0) throw new InvalidDataException($"\"{program.Name}\" has a \"page\" but no \"find\".");

        var page = new Uri(program.Page!);
        Uri? referer = null;
        for (int i = 0; ; i++)
        {
            var (response, final) = await SendAsync(page, referer, token).ConfigureAwait(false);
            string html;
            using (response) html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            var link = Newest(html, steps[i], final)
                       ?? throw new InvalidDataException($"No download link found on {final}");
            if (i == steps.Count - 1) return (link, final);
            referer = final;
            page = link;
        }
    }

    /// <summary>The link with the highest version number in its last part, e.g. ubuntu-26.04.1 over ubuntu-26.04.</summary>
    private static Uri? Newest(string html, string pattern, Uri page)
    {
        Uri? best = null;
        long[] bestVersion = Array.Empty<long>();
        foreach (Match match in Regex.Matches(html, pattern, RegexOptions.IgnoreCase))
        {
            string text = WebUtility.HtmlDecode(match.Groups.Count > 1 && match.Groups[1].Success
                ? match.Groups[1].Value
                : match.Value);
            if (!Uri.TryCreate(page, text, out var link)) continue;

            long[] version = VersionOf(link);
            if (best == null || Compare(version, bestVersion) > 0)
            {
                best = link;
                bestVersion = version;
            }
        }
        return best;
    }

    private static long[] VersionOf(Uri link)
    {
        string last = link.AbsolutePath.TrimEnd('/');
        last = last.Substring(last.LastIndexOf('/') + 1);
        var number = VersionNumber.Match(last);
        return number.Success
            ? number.Value.Split('.').Select(p => long.TryParse(p, out long n) ? n : 0).ToArray()
            : Array.Empty<long>();
    }

    private static int Compare(long[] a, long[] b)
    {
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            if (a[i] != b[i]) return a[i].CompareTo(b[i]);
        }
        return a.Length.CompareTo(b.Length);
    }

    /// <summary>
    /// Saves the file into <paramref name="folder"/> and returns its path. A complete copy from an earlier run is
    /// reused. The download goes to a .part file first, so a broken one never looks finished.
    /// </summary>
    public static async Task<string> SaveAsync(Uri url, Uri? referer, string folder, Action<Winget.Progress>? onProgress,
        CancellationToken token)
    {
        var (response, final) = await SendAsync(url, referer, token).ConfigureAwait(false);
        using (response)
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, FileNameOf(response, final));
            long total = response.Content.Headers.ContentLength ?? 0;
            if (total > 0 && File.Exists(path) && new FileInfo(path).Length == total)
            {
                onProgress?.Invoke(new Winget.Progress { Fraction = 1, Done = total, Total = total });
                return path;
            }

            string part = path + ".part";
            // Closing the response is the only reliable way to break a read that hangs.
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
            using (stop.Token.Register(response.Dispose))
            {
                try
                {
                    using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var target = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true))
                    {
                        var buffer = new byte[1 << 16];
                        long done = 0;
                        int read;
                        var clock = Stopwatch.StartNew();
                        long lastReport = 0;
                        stop.CancelAfter(StallTimeout);
                        while ((read = await source.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                        {
                            stop.CancelAfter(StallTimeout);
                            await target.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                            done += read;
                            // An ISO is ~100 000 reads: report a few times a second, not on every one.
                            if (clock.ElapsedMilliseconds - lastReport < 100 && done != total) continue;
                            lastReport = clock.ElapsedMilliseconds;
                            onProgress?.Invoke(new Winget.Progress
                            {
                                Done = done,
                                Total = total,
                                Fraction = total > 0 ? Math.Min(1, (double)done / total) : 0,
                            });
                        }
                        if (total > 0 && done < total) throw new IOException("The download was cut off.");
                    }
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(part, path);
                }
                catch
                {
                    TryDelete(part);
                    token.ThrowIfCancellationRequested();
                    if (stop.IsCancellationRequested) throw new IOException("The download stopped responding.");
                    throw;
                }
            }
            return path;
        }
    }

    /// <summary>Runs a downloaded installer in its own window and waits until it closes.</summary>
    public static async Task<int> RunAsync(string path, string? args, CancellationToken token)
    {
        using var process = Process.Start(new ProcessStartInfo(path, args ?? "")
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(path)!,
        });
        // Handed to an already running process (e.g. msiexec): nothing to wait for.
        if (process == null) return 0;

        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => exited.TrySetResult(true);
        if (process.HasExited) exited.TrySetResult(true);
        using (token.Register(() => Winget.KillTree(process.Id)))
        {
            await exited.Task.ConfigureAwait(false);
        }
        token.ThrowIfCancellationRequested();
        return process.ExitCode;
    }

    private static async Task<(HttpResponseMessage Response, Uri Final)> SendAsync(Uri url, Uri? referer,
        CancellationToken token)
    {
        for (int hop = 0; hop < 10; hop++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", Browser);
            request.Headers.TryAddWithoutValidation("Accept", "*/*");
            if (referer != null) request.Headers.Referrer = referer;

            HttpResponseMessage response;
            try
            {
                response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException($"{url.Host} did not answer.");
            }

            int code = (int)response.StatusCode;
            if (code is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                response.Dispose();
                url = location.IsAbsoluteUri ? location : new Uri(url, location);
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                throw new HttpRequestException($"{url.Host} answered {code} {response.ReasonPhrase}.");
            }
            return (response, url);
        }
        throw new HttpRequestException("Too many redirects.");
    }

    private static string FileNameOf(HttpResponseMessage response, Uri final)
    {
        var disposition = response.Content.Headers.ContentDisposition;
        string? name = disposition?.FileNameStar ?? disposition?.FileName;
        if (string.IsNullOrWhiteSpace(name))
        {
            string last = final.AbsolutePath.TrimEnd('/');
            name = Uri.UnescapeDataString(last.Substring(last.LastIndexOf('/') + 1));
        }
        name = string.Concat(name!.Trim('"', ' ').Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return name.Length > 0 ? name : "download.exe";
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
