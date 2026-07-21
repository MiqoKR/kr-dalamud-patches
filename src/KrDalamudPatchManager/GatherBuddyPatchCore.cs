using Mono.Cecil;
using Mono.Cecil.Cil;

namespace KrDalamudPatchManager.Modules;

internal static class GatherBuddyPatchCore
{
    public static void Patch(string sourceDirectory, string outputDirectory, string hookDirectory)
    {
        sourceDirectory = Path.GetFullPath(sourceDirectory);
        outputDirectory = Path.GetFullPath(outputDirectory);
        hookDirectory = Path.GetFullPath(hookDirectory);
        RequireDirectory(sourceDirectory);
        CopyDirectory(sourceDirectory, outputDirectory);

        var outputGameDataDll = Path.Combine(outputDirectory, "GatherBuddy.GameData.dll");
        var outputPluginDll = Path.Combine(outputDirectory, "GatherBuddyReborn.dll");
        RequireFile(outputGameDataDll);
        RequireFile(outputPluginDll);

        var dependencyDirectories = new[] { outputDirectory, hookDirectory };
        PatchMultiStringLanguageFallback(outputGameDataDll, dependencyDirectories);
        PatchFishingRegexLanguageFallback(outputPluginDll, dependencyDirectories);
        Verify(outputDirectory, hookDirectory);
    }

    public static bool IsPatched(string pluginDirectory, string hookDirectory)
    {
        try
        {
            Verify(pluginDirectory, hookDirectory);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void Verify(string pluginDirectory, string hookDirectory)
    {
        pluginDirectory = Path.GetFullPath(pluginDirectory);
        hookDirectory = Path.GetFullPath(hookDirectory);
        RequireDirectory(pluginDirectory);
        var gameDataDllPath = Path.Combine(pluginDirectory, "GatherBuddy.GameData.dll");
        var pluginDllPath = Path.Combine(pluginDirectory, "GatherBuddyReborn.dll");
        RequireFile(gameDataDllPath);
        RequireFile(pluginDllPath);

        var dependencyDirectories = new[] { pluginDirectory, hookDirectory };
        VerifyMultiStringLanguageFallback(gameDataDllPath, dependencyDirectories);
        VerifyFishingRegexLanguageFallback(pluginDllPath, dependencyDirectories);
    }

static void RequireFile(string path)
{
    if (!File.Exists(path))
    {
        throw new FileNotFoundException("Required file was not found.", path);
    }
}

static void RequireDirectory(string path)
{
    if (!Directory.Exists(path))
    {
        throw new DirectoryNotFoundException($"Required directory was not found: {path}");
    }
}

static void CopyDirectory(string sourceDirectory, string outputDirectory)
{
    if (Directory.Exists(outputDirectory))
    {
        Directory.Delete(outputDirectory, recursive: true);
    }

    foreach (var sourcePath in Directory.EnumerateFileSystemEntries(sourceDirectory, "*", SearchOption.AllDirectories))
    {
        var relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
        var outputPath = Path.Combine(outputDirectory, relativePath);
        if (Directory.Exists(sourcePath))
        {
            Directory.CreateDirectory(outputPath);
            continue;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.Copy(sourcePath, outputPath, overwrite: true);
    }
}

static ReaderParameters CreateReaderParameters(params string[] searchDirectories)
{
    var resolver = new DefaultAssemblyResolver();
    foreach (var directory in searchDirectories)
    {
        resolver.AddSearchDirectory(directory);
    }

    return new ReaderParameters
    {
        AssemblyResolver = resolver,
        InMemory = true,
        ReadWrite = false,
        ReadingMode = ReadingMode.Immediate,
    };
}

static void PatchMultiStringLanguageFallback(string dllPath, string[] dependencyDirectories)
{
    var readerParameters = CreateReaderParameters(dependencyDirectories);
    var assembly = AssemblyDefinition.ReadAssembly(dllPath, readerParameters);
    var module = assembly.MainModule;
    var tempPath = dllPath + ".patched.tmp";

    try
    {
        var multiString = module.Types.FirstOrDefault(type => type.FullName == "GatherBuddy.Utility.MultiString")
            ?? throw new InvalidOperationException("GatherBuddy.Utility.MultiString was not found.");
        var name = multiString.Methods.FirstOrDefault(method =>
            method.Name == "Name" &&
            method.Parameters.Count == 1 &&
            method.Parameters[0].ParameterType.FullName == "Dalamud.Game.ClientLanguage")
            ?? throw new InvalidOperationException("MultiString.Name(ClientLanguage) was not found.");

        var english = multiString.Fields.FirstOrDefault(field => field.Name == "English")
            ?? throw new InvalidOperationException("MultiString.English field was not found.");
        var german = multiString.Fields.FirstOrDefault(field => field.Name == "German")
            ?? throw new InvalidOperationException("MultiString.German field was not found.");
        var japanese = multiString.Fields.FirstOrDefault(field => field.Name == "Japanese")
            ?? throw new InvalidOperationException("MultiString.Japanese field was not found.");
        var french = multiString.Fields.FirstOrDefault(field => field.Name == "French")
            ?? throw new InvalidOperationException("MultiString.French field was not found.");

        RewriteNameMethod(name, english, german, japanese, french);

        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        assembly.Write(tempPath);
        File.Copy(tempPath, dllPath, overwrite: true);
    }
    finally
    {
        assembly.Dispose();
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }
    }
}

static void RewriteNameMethod(MethodDefinition method, FieldReference english, FieldReference german, FieldReference japanese, FieldReference french)
{
    method.Body.ExceptionHandlers.Clear();
    method.Body.Variables.Clear();
    method.Body.InitLocals = false;
    method.Body.Instructions.Clear();

    var processor = method.Body.GetILProcessor();
    var loadEnglish = Instruction.Create(OpCodes.Ldarg_0);
    var loadGerman = Instruction.Create(OpCodes.Ldarg_0);
    var loadJapanese = Instruction.Create(OpCodes.Ldarg_0);
    var loadFrench = Instruction.Create(OpCodes.Ldarg_0);

    processor.Append(Instruction.Create(OpCodes.Ldarg_1));
    processor.Append(Instruction.Create(OpCodes.Switch, new[] { loadEnglish, loadGerman, loadJapanese, loadFrench }));

    // Unknown language values, including KR's extra ClientLanguage value, fall back to English.
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Ldfld, english));
    processor.Append(Instruction.Create(OpCodes.Ret));

    processor.Append(loadEnglish);
    processor.Append(Instruction.Create(OpCodes.Ldfld, english));
    processor.Append(Instruction.Create(OpCodes.Ret));

    processor.Append(loadGerman);
    processor.Append(Instruction.Create(OpCodes.Ldfld, german));
    processor.Append(Instruction.Create(OpCodes.Ret));

    processor.Append(loadJapanese);
    processor.Append(Instruction.Create(OpCodes.Ldfld, japanese));
    processor.Append(Instruction.Create(OpCodes.Ret));

    processor.Append(loadFrench);
    processor.Append(Instruction.Create(OpCodes.Ldfld, french));
    processor.Append(Instruction.Create(OpCodes.Ret));

    method.Body.MaxStackSize = 2;
}

static void PatchFishingRegexLanguageFallback(string dllPath, string[] dependencyDirectories)
{
    var readerParameters = CreateReaderParameters(dependencyDirectories);
    var assembly = AssemblyDefinition.ReadAssembly(dllPath, readerParameters);
    var module = assembly.MainModule;
    var tempPath = dllPath + ".patched.tmp";

    try
    {
        var regexes = GetAllTypes(module).FirstOrDefault(type =>
            type.Name == "Regexes" &&
            type.DeclaringType?.FullName == "GatherBuddy.FishTimer.Parser.FishingParser")
            ?? throw new InvalidOperationException("FishingParser.Regexes was not found.");
        var fromLanguage = regexes.Methods.FirstOrDefault(method =>
            method.Name == "FromLanguage" &&
            method.Parameters.Count == 1 &&
            method.Parameters[0].ParameterType.FullName == "Dalamud.Game.ClientLanguage")
            ?? throw new InvalidOperationException("FishingParser.Regexes.FromLanguage(ClientLanguage) was not found.");
        var english = regexes.Fields.FirstOrDefault(field => field.Name == "English")
            ?? throw new InvalidOperationException("FishingParser.Regexes.English field was not found.");
        var getValue = fromLanguage.Body.Instructions
            .Select(instruction => instruction.Operand)
            .OfType<MethodReference>()
            .FirstOrDefault(method => method.Name == "get_Value")
            ?? throw new InvalidOperationException("FishingParser.Regexes Lazy.Value getter was not found.");

        RewriteFishingRegexMethod(fromLanguage, english, getValue);

        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        assembly.Write(tempPath);
        File.Copy(tempPath, dllPath, overwrite: true);
    }
    finally
    {
        assembly.Dispose();
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }
    }
}

static void RewriteFishingRegexMethod(MethodDefinition method, FieldReference english, MethodReference getValue)
{
    method.Body.ExceptionHandlers.Clear();
    method.Body.Variables.Clear();
    method.Body.InitLocals = false;
    method.Body.Instructions.Clear();

    var processor = method.Body.GetILProcessor();
    processor.Append(Instruction.Create(OpCodes.Ldsfld, english));
    processor.Append(Instruction.Create(OpCodes.Callvirt, getValue));
    processor.Append(Instruction.Create(OpCodes.Ret));
    method.Body.MaxStackSize = 1;
}

static IEnumerable<TypeDefinition> GetAllTypes(ModuleDefinition module)
{
    foreach (var type in module.Types)
    {
        yield return type;
        foreach (var nested in GetAllNestedTypes(type))
        {
            yield return nested;
        }
    }
}

static IEnumerable<TypeDefinition> GetAllNestedTypes(TypeDefinition type)
{
    foreach (var nested in type.NestedTypes)
    {
        yield return nested;
        foreach (var child in GetAllNestedTypes(nested))
        {
            yield return child;
        }
    }
}

static void VerifyMultiStringLanguageFallback(string dllPath, string[] dependencyDirectories)
{
    using var assembly = AssemblyDefinition.ReadAssembly(dllPath, CreateReaderParameters(dependencyDirectories));
    var multiString = assembly.MainModule.Types.FirstOrDefault(type => type.FullName == "GatherBuddy.Utility.MultiString")
        ?? throw new InvalidOperationException("GatherBuddy.Utility.MultiString was not found.");
    var name = multiString.Methods.FirstOrDefault(method =>
        method.Name == "Name" &&
        method.Parameters.Count == 1 &&
        method.Parameters[0].ParameterType.FullName == "Dalamud.Game.ClientLanguage")
        ?? throw new InvalidOperationException("MultiString.Name(ClientLanguage) was not found.");

    var instructions = name.Body.Instructions;
    if (instructions.Any(instruction => instruction.OpCode == OpCodes.Throw))
    {
        throw new InvalidOperationException("MultiString.Name still contains a throw instruction.");
    }

    if (!instructions.Any(instruction => instruction.OpCode == OpCodes.Switch))
    {
        throw new InvalidOperationException("MultiString.Name no longer contains the expected language switch.");
    }

    if (instructions.Count(instruction => instruction.OpCode == OpCodes.Ldfld &&
        instruction.Operand is FieldReference field &&
        field.Name == "English") < 2)
    {
        throw new InvalidOperationException("MultiString.Name does not appear to include the English fallback.");
    }
}

static void VerifyFishingRegexLanguageFallback(string dllPath, string[] dependencyDirectories)
{
    using var assembly = AssemblyDefinition.ReadAssembly(dllPath, CreateReaderParameters(dependencyDirectories));
    var regexes = GetAllTypes(assembly.MainModule).FirstOrDefault(type =>
        type.Name == "Regexes" &&
        type.DeclaringType?.FullName == "GatherBuddy.FishTimer.Parser.FishingParser")
        ?? throw new InvalidOperationException("FishingParser.Regexes was not found.");
    var fromLanguage = regexes.Methods.FirstOrDefault(method =>
        method.Name == "FromLanguage" &&
        method.Parameters.Count == 1 &&
        method.Parameters[0].ParameterType.FullName == "Dalamud.Game.ClientLanguage")
        ?? throw new InvalidOperationException("FishingParser.Regexes.FromLanguage(ClientLanguage) was not found.");

    var instructions = fromLanguage.Body.Instructions;
    if (instructions.Count != 3 ||
        instructions[0].OpCode != OpCodes.Ldsfld ||
        instructions[0].Operand is not FieldReference field || field.Name != "English" ||
        instructions[1].OpCode != OpCodes.Callvirt ||
        instructions[1].Operand is not MethodReference getter || getter.Name != "get_Value" ||
        instructions[2].OpCode != OpCodes.Ret)
    {
        throw new InvalidOperationException("FishingParser.Regexes.FromLanguage is not the expected English fallback.");
    }
}
}
