using System.Management;

namespace Apathy.Services;

/// <summary>What's inside this PC, read from WMI, so the Drivers tab can pick the right drivers.</summary>
public sealed class Hardware
{
    public List<string> Processors { get; } = new();
    public List<string> GraphicsCards { get; } = new();
    public string Motherboard { get; private set; } = "";

    public static Task<Hardware> DetectAsync() => Task.Run(() =>
    {
        var pc = new Hardware();
        pc.Processors.AddRange(Query("SELECT Name FROM Win32_Processor", o => o["Name"]));
        // Skip virtual adapters (Remote Desktop, Parsec, Hyper-V...) that have no real driver.
        pc.GraphicsCards.AddRange(Query("SELECT Name FROM Win32_VideoController", o => o["Name"])
            .Where(n => !n.ToLowerInvariant().Contains("basic display") && !n.ToLowerInvariant().Contains("remote")));
        pc.Motherboard = Query("SELECT Manufacturer, Product FROM Win32_BaseBoard",
            o => $"{o["Manufacturer"]} {o["Product"]}").FirstOrDefault() ?? "";
        return pc;
    });

    /// <summary>True when any "part:word" rule matches, e.g. "gpu:nvidia" and an NVIDIA GeForce card.</summary>
    public bool Matches(IEnumerable<string>? rules)
    {
        if (rules == null) return false;
        foreach (string rule in rules)
        {
            int colon = rule.IndexOf(':');
            if (colon < 0) continue;
            string part = rule.Substring(0, colon).Trim().ToLowerInvariant();
            string word = rule.Substring(colon + 1).Trim().ToLowerInvariant();
            IEnumerable<string> names = part switch
            {
                "cpu" => Processors,
                "gpu" => GraphicsCards,
                "board" => new[] { Motherboard },
                _ => Array.Empty<string>(),
            };
            if (word.Length > 0 && names.Any(n => n.ToLowerInvariant().Contains(word))) return true;
        }
        return false;
    }

    private static IEnumerable<string> Query(string wql, Func<ManagementBaseObject, object?> pick)
    {
        var results = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher(wql);
            foreach (var obj in searcher.Get())
            {
                using (obj)
                {
                    string text = (pick(obj)?.ToString() ?? "").Trim();
                    if (text.Length > 0 && !results.Contains(text)) results.Add(text);
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException
                                       or System.Runtime.InteropServices.COMException)
        {
            // WMI turned off or broken: the tab just shows "unknown".
        }
        return results;
    }
}
