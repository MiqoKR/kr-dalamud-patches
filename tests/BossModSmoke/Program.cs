using System.Reflection;

if (args.Length != 2)
    throw new ArgumentException("Usage: BossModSmoke <patched-plugin-directory> <hook-directory>");

var type = Assembly.Load("KR.Dalamud.PatchManager")
    .GetType("KrDalamudPatchManager.Modules.BossModPatchCore", throwOnError: true)!;
var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
type.GetMethod("Verify", flags)!.Invoke(null, new object[] { args[0], args[1] });

Console.WriteLine("BossMod patched DLL verification passed.");
