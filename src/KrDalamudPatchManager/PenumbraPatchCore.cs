using GlamourerKrActorPatcher;

namespace KrDalamudPatchManager.Modules;

internal static class PenumbraPatchCore
{
    public static void Patch(string pluginDirectory, string hookDirectory, string outputDirectory)
        => GlamourerPatchCore.PatchGameDataCompatibility(pluginDirectory, hookDirectory, outputDirectory);

    public static bool IsPatched(string pluginDirectory, string hookDirectory)
        => GlamourerPatchCore.IsGameDataCompatibilityPatched(pluginDirectory, hookDirectory);

    // Penumbra.GameData is shared by several plugins and often keeps the same
    // actor/world layout across a Penumbra version bump.  Validate an unknown
    // version in a disposable copy instead of trusting the version number.
    // Any IL/layout change makes Patch or its post-patch verification fail, so
    // the manager keeps the module blocked until it receives a dedicated patch.
    public static void ValidatePatchShape(string pluginDirectory, string hookDirectory)
    {
        var validationDirectory = Path.Combine(
            Path.GetTempPath(),
            "KR-Dalamud-PatchManager",
            "penumbra-shape-validation",
            Guid.NewGuid().ToString("N"));
        try
        {
            Patch(pluginDirectory, hookDirectory, validationDirectory);
        }
        finally
        {
            TryDeleteDirectory(validationDirectory);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // The validation copy is disposable and never contains user data.
        }
    }
}
