using System.Text.Json;
using KrDalamudPatchManager.Modules;

namespace KrDalamudPatchManager;

/// <summary>
/// Keeps the Lite distribution on the ordinary Patch Manager update channel while
/// limiting its UI and command-line module set. The marker lives next to the EXE,
/// and is intentionally not part of ordinary update ZIPs, so it survives updates.
/// </summary>
internal static class PatchManagerEdition
{
    internal const string LiteMarkerFileName = "KR.Dalamud.PatchManager.Lite.json";

    private static readonly HashSet<string> LiteHiddenModuleIds = new(StringComparer.Ordinal)
    {
        "bossmodreborn",
        "gatherbuddyreborn",
    };

    public static List<PatchModule> VisibleModules()
    {
        var modules = PatchModule.CreateAll();
        return IsLiteInstall()
            ? modules.Where(module => !LiteHiddenModuleIds.Contains(module.Id)).ToList()
            : modules;
    }

    private static bool IsLiteInstall()
    {
        var markerPath = Path.Combine(AppContext.BaseDirectory, LiteMarkerFileName);
        if (!File.Exists(markerPath))
        {
            return false;
        }

        try
        {
            using var marker = JsonDocument.Parse(File.ReadAllText(markerPath));
            return marker.RootElement.TryGetProperty("edition", out var edition) &&
                string.Equals(edition.GetString(), "lite", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
