using System.IO;
using System.Net.Http;

namespace PatchCord;

/// <summary>
/// Fetches the Vencord / Equicord desktop <c>dist</c> payload directly from the mod's GitHub
/// release, so PatchCord does not depend on the mod's own installer being able to detect Discord.
/// <para>
/// PatchCord's Layer-A patch swaps the bundle <c>app.asar</c> for a stub that
/// <c>require()</c>s <c>&lt;mod&gt;/dist/patcher.js</c> — but it never creates that file; the official
/// installer normally does. When that installer breaks (e.g. the 2026 macOS/Linux <c>app-X.Y.Z</c>
/// updater rollout broke Discord detection for several mod installers), the dist is unreachable.
/// This engine closes that gap the same way <see cref="BetterDiscordEngine.DownloadAsar"/> and
/// <see cref="OpenAsarEngine"/> already do for their payloads.
/// </para>
/// <para>
/// The four files are exactly what <c>patcher.js</c> loads at runtime via <c>__dirname</c>
/// (verified against the live <c>devbuild</c> asset): <c>patcher.js</c> (entry) plus
/// <c>preload.js</c>, <c>renderer.js</c>, <c>renderer.css</c>. Platform-neutral (BCL + HttpClient);
/// the same paths work on Windows (<c>%APPDATA%\Vencord\dist</c>) and macOS
/// (<c>~/Library/Application Support/Vencord/dist</c>).
/// </para>
/// </summary>
public static class VencordEngine
{
    // Vencord ships a rolling "devbuild" release; Equicord ships date-tagged releases exposed as
    // "latest". Both carry identically-named desktop dist assets.
    private const string VencordBase  = "https://github.com/Vendicated/Vencord/releases/download/devbuild/";
    private const string EquicordBase = "https://github.com/Equicord/Equicord/releases/latest/download/";

    /// <summary>
    /// The minimal desktop dist file set. <c>patcher.js</c> is the stub's entry point; it loads the
    /// other three from its own directory at runtime. Order is irrelevant (all four are required).
    /// </summary>
    public static readonly string[] DistFiles = { "patcher.js", "preload.js", "renderer.js", "renderer.css" };

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("PatchCord");
        return c;
    }

    /// <summary>True for the two mods this engine can fetch.</summary>
    public static bool CanFetch(string mod) => mod is "vencord" or "equicord";

    private static string BaseUrl(string mod) => mod switch
    {
        "vencord"  => VencordBase,
        "equicord" => EquicordBase,
        _ => throw new ArgumentException($"VencordEngine cannot fetch '{mod}'.", nameof(mod)),
    };

    /// <summary>
    /// True when <paramref name="distDir"/> holds a usable dist — i.e. <c>patcher.js</c> exists and
    /// is non-empty. Mirrors the "mod installed" check (a present patcher.js is what the Layer-A stub
    /// requires). The sibling files are written atomically with it by <see cref="DownloadDist"/>.
    /// </summary>
    public static bool IsDistInstalled(string distDir)
    {
        var patcher = Path.Combine(distDir, "patcher.js");
        return File.Exists(patcher) && new FileInfo(patcher).Length > 0;
    }

    /// <summary>
    /// Download the full desktop dist for <paramref name="mod"/> ("vencord" | "equicord") into
    /// <paramref name="distDir"/> (e.g. <c>~/Library/Application Support/Vencord/dist</c>).
    /// <para>
    /// All four files are fetched into memory FIRST; only if every download succeeds are they
    /// written. A network failure therefore leaves any prior dist untouched rather than a partial
    /// one. Throws on an unknown mod, an empty/failed download, or a write error.
    /// </para>
    /// </summary>
    public static void DownloadDist(string mod, string distDir)
    {
        var baseUrl = BaseUrl(mod);

        // Fetch all into memory before touching disk (atomic-ish: no partial dist on failure).
        var payload = new Dictionary<string, byte[]>(DistFiles.Length);
        foreach (var file in DistFiles)
        {
            byte[] data;
            try
            {
                data = Http.GetByteArrayAsync(baseUrl + file).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to download {mod} dist file '{file}': {ex.Message}", ex);
            }
            if (data.Length == 0)
                throw new IOException($"{mod} dist file '{file}' downloaded empty.");
            payload[file] = data;
        }

        Directory.CreateDirectory(distDir);
        foreach (var (file, data) in payload)
            File.WriteAllBytes(Path.Combine(distDir, file), data);

        var total = payload.Values.Sum(b => b.Length);
        Log.Write($"Downloaded {MonitorService.ModShort(mod)} dist ({DistFiles.Length} files, {total} bytes) to {distDir}.", "OK");
    }
}
