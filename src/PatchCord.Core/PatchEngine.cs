using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PatchCord;

// Live state of one install. AsarMod is the app.asar-layer mod
// (none/vencord/equicord/other); BdActive is the BetterDiscord core patch.
public sealed record InstallState(
    bool Running, bool Patched, string? AppName, string? Resources, string? AppDir, bool Installed,
    bool OpenAsarPresent = false, string AsarMod = "none", bool BdActive = false)
{
    public string InjectedMod => AsarMod is "vencord" or "equicord" ? AsarMod
        : BdActive ? "betterdiscord" : (AsarMod == "other" ? "other" : "none");
}

// Vencord/Equicord asar patch: rename app.asar to _app.asar, write a stub app.asar
// that requires the mod's patcher.js. Logic from the Vencord installer.
public static class PatchEngine
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    public static byte[] BuildStubAsar(string patcher)
    {
        var patcherJson = JsonSerializer.Serialize(patcher,
            new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var indexJs = $"require({patcherJson})";
        var packageJson = "{\n\t\"name\": \"discord\",\n\t\"main\": \"index.js\"\n}";

        int indexLen = Utf8.GetByteCount(indexJs);
        int pkgLen = Utf8.GetByteCount(packageJson);
        var fileContents = indexJs + packageJson;

        var header = "{\"files\":{\"index.js\":{\"size\":" + indexLen + ",\"offset\":\"0\"}," +
                     "\"package.json\":{\"size\":" + pkgLen + ",\"offset\":\"" + indexLen + "\"}}}";

        int headerStringSize = Utf8.GetByteCount(header);
        const int dataSize = 4;
        int alignedSize = (headerStringSize + dataSize - 1) & ~(dataSize - 1);
        int headerSize = alignedSize + 8;
        int headerObjectSize = alignedSize + dataSize;
        int diff = alignedSize - headerStringSize;
        if (diff > 0) header += new string('0', diff);

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        foreach (var n in new[] { dataSize, headerSize, headerObjectSize, headerStringSize })
            bw.Write(n); // little-endian int32
        bw.Write(Utf8.GetBytes(header));
        bw.Write(Utf8.GetBytes(fileContents));
        bw.Flush();
        return ms.ToArray();
    }

    public static string BranchFromLeaf(string path)
    {
        var leaf = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)).ToLowerInvariant();
        if (leaf.Contains("canary")) return "DiscordCanary";
        if (leaf.Contains("ptb")) return "DiscordPTB";
        if (leaf.Contains("development") || leaf.Contains("dev")) return "DiscordDevelopment";
        return "Discord";
    }

    /// <summary>
    /// Compute install state from pre-resolved platform values.
    /// <paramref name="resourcesDir"/> and <paramref name="coreAppDir"/> come from
    /// <see cref="IDiscordPlatform.ResolveResourcesDir"/> /
    /// <see cref="IDiscordPlatform.ResolveCoreAppDir"/> respectively.
    /// <paramref name="running"/> comes from <see cref="IDiscordPlatform.IsRunning"/>.
    /// </summary>
    public static InstallState GetState(
        bool running,
        string? resourcesDir,
        string? coreAppDir,
        string? versionLabel,
        bool checkOpenAsar = false)
    {
        bool patched = false, openAsar = false, bd = false;
        string asarMod = "none";
        bool installed = resourcesDir != null && coreAppDir != null;
        if (installed)
        {
            patched = File.Exists(Path.Combine(resourcesDir!, "_app.asar"));
            asarMod = DetectMod(resourcesDir!);
            bd = BetterDiscordEngine.IsInjected(coreAppDir!);
            if (checkOpenAsar) openAsar = OpenAsarEngine.IsInstalled(resourcesDir!);
        }
        return new InstallState(running, patched, versionLabel, resourcesDir, coreAppDir, installed, openAsar, asarMod, bd);
    }

    // Which mod the current stub points at: none / vencord / equicord / other.
    public static string DetectMod(string resourcesDir)
    {
        var appAsar = Path.Combine(resourcesDir, "app.asar");
        var bak = Path.Combine(resourcesDir, "_app.asar");
        if (!File.Exists(bak) || !File.Exists(appAsar)) return "none";
        try
        {
            if (new FileInfo(appAsar).Length > 64 * 1024) return "other"; // our stub is tiny
            var bytes = File.ReadAllBytes(appAsar);
            if (ContainsAscii(bytes, "Equicord")) return "equicord";
            if (ContainsAscii(bytes, "Vencord")) return "vencord";
            return "other";
        }
        catch { return "none"; }
    }

    private static bool ContainsAscii(byte[] hay, string needleStr)
    {
        var needle = Encoding.ASCII.GetBytes(needleStr);
        for (int i = 0; i <= hay.Length - needle.Length; i++)
        {
            int j = 0;
            while (j < needle.Length && hay[i + j] == needle[j]) j++;
            if (j == needle.Length) return true;
        }
        return false;
    }

    // Reverts a mod patch (used when switching mods). No-op if not patched.
    public static void Unpatch(string resourcesDir)
    {
        var appAsar = Path.Combine(resourcesDir, "app.asar");
        var bak = Path.Combine(resourcesDir, "_app.asar");
        if (!File.Exists(bak)) return;
        if (File.Exists(appAsar)) File.Delete(appAsar);
        File.Move(bak, appAsar);
    }

    // Throws if app.asar is missing or already patched.
    public static void Patch(string resourcesDir, byte[] stubBytes)
    {
        var appAsar = Path.Combine(resourcesDir, "app.asar");
        var bakAsar = Path.Combine(resourcesDir, "_app.asar");
        if (!File.Exists(appAsar)) throw new FileNotFoundException($"app.asar missing in {resourcesDir}");
        if (File.Exists(bakAsar)) throw new IOException($"_app.asar already present in {resourcesDir}");

        File.Move(appAsar, bakAsar);
        try
        {
            File.WriteAllBytes(appAsar, stubBytes);
        }
        catch
        {
            // roll back the rename if writing the stub failed
            if (!File.Exists(appAsar) && File.Exists(bakAsar))
            {
                try { File.Move(bakAsar, appAsar); } catch { }
            }
            throw;
        }
    }
}
