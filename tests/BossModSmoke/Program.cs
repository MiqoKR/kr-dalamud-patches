using System.Reflection;

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
