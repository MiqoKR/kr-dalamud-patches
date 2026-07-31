using GlamourerKrActorPatcher;

namespace KrDalamudPatchManager.Modules;

internal static class PenumbraPatchCore
{
    public static void Patch(string pluginDirectory, string hookDirectory, string outputDirectory)
        => GlamourerPatchCore.PatchGameDataCompatibility(pluginDirectory, hookDirectory, outputDirectory);

    public static bool IsPatched(string pluginDirectory, string hookDirectory)
        => GlamourerPatchCore.IsGameDataCompatibilityPatched(pluginDirectory, hookDirectory);
}
