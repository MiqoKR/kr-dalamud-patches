using System.Reflection;

if (args.Length != 2)
    throw new ArgumentException("Usage: WorldDisplaySmoke <glamourer-plugin-directory> <hook-directory>");

var type = Assembly.Load("KR.Dalamud.PatchManager")
    .GetType("GlamourerKrActorPatcher.GlamourerPatchCore", throwOnError: true)!;
var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
var parameters = new object[] { args[0], args[1] };
var needsUpgrade = (bool)type.GetMethod("NeedsWorldDisplayUpgrade", flags)!.Invoke(null, parameters)!;
if (needsUpgrade)
    type.GetMethod("UpgradeWorldDisplay", flags)!.Invoke(null, parameters);

var isPatched = (bool)type.GetMethod("IsPatched", flags)!.Invoke(null, parameters)!;
if (!isPatched)
    throw new InvalidOperationException("The display upgrade did not pass full Glamourer patch verification.");

var gameData = Assembly.LoadFrom(Path.Combine(args[0], "Penumbra.GameData.dll"));
var worldIdType = gameData.GetType("Penumbra.GameData.Structs.WorldId", throwOnError: true)!;
var fromUShort = worldIdType.GetMethods(flags).Single(method => method.Name == "op_Implicit" &&
    method.ReturnType == worldIdType && method.GetParameters() is [{ ParameterType: var parameterType }] &&
    parameterType == typeof(ushort));
var worldId = fromUShort.Invoke(null, [(ushort)2077]);
var nameDictsType = gameData.GetType("Penumbra.GameData.Data.NameDicts", throwOnError: true)!;
var fallback = nameDictsType.GetMethod("GetKoreanWorldNameFallback", BindingFlags.Static | BindingFlags.NonPublic)!;
var worldName = (string)fallback.Invoke(null, [worldId])!;
if (worldName != "모그리")
    throw new InvalidOperationException($"Expected 모그리 for world 2077, received {worldName}.");

Console.WriteLine("World display upgrade smoke test passed.");
