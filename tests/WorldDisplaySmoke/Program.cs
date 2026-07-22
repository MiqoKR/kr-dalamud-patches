using System.Reflection;

if (args.Length != 2)
    throw new ArgumentException("Usage: WorldDisplaySmoke <glamourer-plugin-directory> <hook-directory>");

var type = Assembly.Load("KR.Dalamud.PatchManager")
    .GetType("GlamourerKrActorPatcher.GlamourerPatchCore", throwOnError: true)!;
var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
var parameters = new object[] { args[0], args[1] };
var needsUpgrade = (bool)type.GetMethod("NeedsWorldDisplayUpgrade", flags)!.Invoke(null, parameters)!;
if (!needsUpgrade)
    throw new InvalidOperationException("The staged old patch was not recognized as eligible for the display upgrade.");

type.GetMethod("UpgradeWorldDisplay", flags)!.Invoke(null, parameters);
var isPatched = (bool)type.GetMethod("IsPatched", flags)!.Invoke(null, parameters)!;
if (!isPatched)
    throw new InvalidOperationException("The display upgrade did not pass full Glamourer patch verification.");

Console.WriteLine("World display upgrade smoke test passed.");
