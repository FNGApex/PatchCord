using System.IO;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PatchCord;

// BetterDiscord injection. It overwrites discord_desktop_core/index.js to require
// betterdiscord.asar (a different file than Vencord/Equicord's app.asar). The host platform
// resolves which version dir holds the live core: Discord's new mac/Linux updater uses the
// wrapped app-<ver>/modules/discord_desktop_core-N/discord_desktop_core/ layout, the legacy
// updater used the bare <ver>/modules/discord_desktop_core/. FindCoreIndexJs handles both.
// Logic from BetterDiscord's scripts/inject.ts.
public static class BetterDiscordEngine
{
    // What Discord ships, and what we restore to.
    private const string Vanilla = "module.exports = require('./core.asar');\n";

    // Official release asar — same source BandagedBD's own injector uses.
    private const string AsarUrl =
        "https://github.com/BetterDiscord/BetterDiscord/releases/latest/download/betterdiscord.asar";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("PatchCord");
        return c;
    }

    /// <summary>
    /// Self-heal: download the latest betterdiscord.asar from the official GitHub release to
    /// <paramref name="asarPath"/> (creating its parent dir). Used by the "Fix it" flow when the
    /// installer never placed the asar. Throws on an empty download or network failure.
    /// </summary>
    public static void DownloadAsar(string asarPath)
    {
        var data = Http.GetByteArrayAsync(AsarUrl).GetAwaiter().GetResult();
        if (data.Length == 0) throw new IOException("BetterDiscord asar download was empty");
        Directory.CreateDirectory(Path.GetDirectoryName(asarPath)!);
        File.WriteAllBytes(asarPath, data);
        Log.Write($"Downloaded BetterDiscord asar ({data.Length} bytes).", "OK");
    }

    // Newest wrapped discord_desktop_core-N first, then the legacy layout.
    public static string? FindCoreIndexJs(string appDir)
    {
        var modules = Path.Combine(appDir, "modules");
        if (!Directory.Exists(modules)) return null;

        var wrapped = Directory.GetDirectories(modules, "discord_desktop_core-*")
            .Select(d => (dir: d, n: ParseSuffix(Path.GetFileName(d))))
            .Where(x => x.n >= 0)
            .OrderByDescending(x => x.n);
        foreach (var (dir, _) in wrapped)
        {
            var p = Path.Combine(dir, "discord_desktop_core", "index.js");
            if (File.Exists(p)) return p;
        }
        var legacy = Path.Combine(modules, "discord_desktop_core", "index.js");
        return File.Exists(legacy) ? legacy : null;
    }

    private static int ParseSuffix(string name)
    {
        var dash = name.LastIndexOf('-');
        return dash >= 0 && int.TryParse(name[(dash + 1)..], out var n) ? n : -1;
    }

    public static bool IsInjected(string appDir)
    {
        var idx = FindCoreIndexJs(appDir);
        if (idx == null) return false;
        try { return File.ReadAllText(idx).Contains("betterdiscord.asar", StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private static readonly JsonSerializerOptions JsonOpts =
        new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string InjectContent(string asarPath)
    {
        // JS-escape the path the same way the installer's require("...") does.
        var json = JsonSerializer.Serialize(asarPath, JsonOpts);
        return $"require({json});\nmodule.exports = require(\"./core.asar\");\n";
    }

    // asarPath must exist and Discord must be stopped first.
    public static void Inject(string appDir, string asarPath)
    {
        var idx = FindCoreIndexJs(appDir)
            ?? throw new FileNotFoundException($"discord_desktop_core/index.js not found under {appDir}");
        File.WriteAllText(idx, InjectContent(asarPath));
    }

    // --bd-test hook: reports the index.js path, detection, and what would be written.
    public static string DryRun(string appDir, string asarPath)
    {
        var idx = FindCoreIndexJs(appDir);
        var cur = idx != null && File.Exists(idx) ? File.ReadAllText(idx).Trim() : "(none)";
        return $"index.js: {idx ?? "(NOT FOUND)"}\ncurrent: {cur}\ninjected={IsInjected(appDir)}\n" +
               $"would write:\n{InjectContent(asarPath)}";
    }

    public static void Restore(string appDir)
    {
        var idx = FindCoreIndexJs(appDir);
        if (idx == null) return;
        File.WriteAllText(idx, Vanilla);
    }
}
