using System.Reflection;

if (args.Length == 2 && args[0] == "--install-latest")
{
    var assembly = Assembly.Load("KR.Dalamud.PatchManager");
    var patchModuleType = assembly.GetType("KrDalamudPatchManager.PatchModule", throwOnError: true)!;
    var modules = ((System.Collections.IEnumerable)patchModuleType.GetMethod("CreateAll", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, null)!)
        .Cast<object>();
    var bossMod = modules.Single(module => (string)patchModuleType.GetProperty("Id")!.GetValue(module)! == "bossmodreborn");
    var installerType = assembly.GetType("KrDalamudPatchManager.OfficialPluginInstaller", throwOnError: true)!;
    try
    {
        var result = installerType.GetMethod("InstallLatest", BindingFlags.Static | BindingFlags.Public)!
            .Invoke(null, new[] { bossMod, args[1] });
        Console.WriteLine(result);
    }
    catch (TargetInvocationException ex) when (ex.InnerException is not null)
    {
        Console.Error.WriteLine(ex.InnerException);
        Environment.ExitCode = 1;
    }

    return;
}

var apply = args.Length == 3 && args[0] == "--apply";
if ((!apply && args.Length != 2) || (apply && args.Length != 3))
    throw new ArgumentException("Usage: BossModSmoke [--apply] <plugin-directory> <hook-directory>");

var pluginDirectory = args[apply ? 1 : 0];
var hookDirectory = args[apply ? 2 : 1];

var type = Assembly.Load("KR.Dalamud.PatchManager")
    .GetType("KrDalamudPatchManager.Modules.BossModPatchCore", throwOnError: true)!;
var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
if (apply)
    type.GetMethod("Patch", flags)!.Invoke(null, new object[] { pluginDirectory, hookDirectory });

type.GetMethod("Verify", flags)!.Invoke(null, new object[] { pluginDirectory, hookDirectory });

Console.WriteLine("BossMod patched DLL verification passed.");
