using Mono.Cecil;
using Mono.Cecil.Cil;

namespace KrDalamudPatchManager.Modules;

internal static class BossModPatchCore
{
    public static void Patch(string pluginDirectory, string hookDirectory)
    {
        pluginDirectory = Path.GetFullPath(pluginDirectory);
        hookDirectory = Path.GetFullPath(hookDirectory);
        var dllPath = Path.Combine(pluginDirectory, "BossModReborn.dll");
        RequireFile(dllPath);
        RequireFile(Path.Combine(hookDirectory, "Dalamud.dll"));
        RequireFile(Path.Combine(hookDirectory, "Lumina.Excel.dll"));
        PatchBossMod(dllPath, pluginDirectory, hookDirectory);
        Verify(pluginDirectory, hookDirectory);
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
        var dllPath = Path.Combine(pluginDirectory, "BossModReborn.dll");
        RequireFile(dllPath);
        RequireFile(Path.Combine(hookDirectory, "Dalamud.dll"));
        RequireFile(Path.Combine(hookDirectory, "Lumina.Excel.dll"));
        if (InspectBossMod(dllPath, pluginDirectory, hookDirectory) != 0)
        {
            throw new InvalidOperationException("BossMod KR 패치 검증에 실패했습니다.");
        }
    }

static void RequireFile(string path)
{
    if (!File.Exists(path))
    {
        throw new FileNotFoundException("Required file was not found.", path);
    }
}

static void PatchBossMod(string dllPath, string pluginDirectory, string hookDirectory)
{
    var tempPath = dllPath + ".patched";
    AssemblyDefinition? assembly = null;
    try
    {
        assembly = AssemblyDefinition.ReadAssembly(dllPath, CreateReaderParameters(pluginDirectory, hookDirectory));
        var module = assembly.MainModule;
        var service = RequireType(module, "BossMod.Service");
        var method = service.Methods.FirstOrDefault(m => m.Name == "LuminaSheet" && m.HasGenericParameters)
            ?? throw new InvalidOperationException("BossMod.Service.LuminaSheet<T> was not found.");
        var dataManagerGetter = service.Methods.FirstOrDefault(m => m.Name == "get_DataManager")
            ?? throw new InvalidOperationException("BossMod.Service.DataManager getter was not found.");

        var dataManagerType = dataManagerGetter.ReturnType.Resolve()
            ?? throw new InvalidOperationException("Could not resolve IDataManager.");
        var getExcelSheet = dataManagerType.Methods.FirstOrDefault(m => m.Name == "GetExcelSheet" && m.HasGenericParameters)
            ?? throw new InvalidOperationException("IDataManager.GetExcelSheet<T> was not found.");

        var importedGetter = module.ImportReference(dataManagerGetter);
        var importedGetExcelSheet = module.ImportReference(getExcelSheet);
        var genericGetExcelSheet = new GenericInstanceMethod(importedGetExcelSheet);
        genericGetExcelSheet.GenericArguments.Add(method.GenericParameters[0]);

        var nullableLanguageType = module.ImportReference(getExcelSheet.Parameters[0].ParameterType);

        ResetBody(method);
        method.Body.InitLocals = true;
        var languageVariable = new VariableDefinition(nullableLanguageType);
        method.Body.Variables.Add(languageVariable);

        var il = method.Body.GetILProcessor();
        il.Append(il.Create(OpCodes.Call, importedGetter));
        il.Append(il.Create(OpCodes.Ldloca_S, languageVariable));
        il.Append(il.Create(OpCodes.Initobj, nullableLanguageType));
        il.Append(il.Create(OpCodes.Ldloc_0));
        il.Append(il.Create(OpCodes.Ldnull));
        il.Append(il.Create(OpCodes.Callvirt, genericGetExcelSheet));
        il.Append(il.Create(OpCodes.Ret));

        PatchLegacyMapEffectHook(module);
        assembly.Write(tempPath);
    }
    finally
    {
        assembly?.Dispose();
    }

    File.Copy(tempPath, dllPath, overwrite: true);
    File.Delete(tempPath);
}

static void PatchLegacyMapEffectHook(ModuleDefinition module)
{
    var worldStateGameSync = RequireType(module, "BossMod.WorldStateGameSync");
    var legacyField = worldStateGameSync.Fields.FirstOrDefault(f => f.Name == "_processLegacyMapEffectHook")
        ?? throw new InvalidOperationException("BossMod.WorldStateGameSync._processLegacyMapEffectHook was not found.");
    var ctor = worldStateGameSync.Methods.FirstOrDefault(m => m.IsConstructor && m.Parameters.Count == 2)
        ?? throw new InvalidOperationException("BossMod.WorldStateGameSync constructor was not found.");
    var dispose = worldStateGameSync.Methods.FirstOrDefault(m => m.Name == "Dispose")
        ?? throw new InvalidOperationException("BossMod.WorldStateGameSync.Dispose was not found.");
    var detour = worldStateGameSync.Methods.FirstOrDefault(m => m.Name == "ProcessLegacyMapEffectDetour")
        ?? throw new InvalidOperationException("BossMod.WorldStateGameSync.ProcessLegacyMapEffectDetour was not found.");

    RemoveLegacyHookConstructorBlock(ctor, legacyField);
    RemoveLegacyHookDisposeCall(dispose, legacyField);
    ResetVoidBody(detour);
    worldStateGameSync.Fields.Remove(legacyField);
}

static int InspectBossMod(string dllPath, string pluginDirectory, string hookDirectory)
{
    using var assembly = AssemblyDefinition.ReadAssembly(dllPath, CreateReaderParameters(pluginDirectory, hookDirectory));
    var matches = new List<string>();

    foreach (var type in AllTypes(assembly.MainModule.Types))
    {
        if (ContainsLegacyReference(type.BaseType))
        {
            matches.Add($"TYPE base {type.FullName} -> {Describe(type.BaseType)}");
        }

        foreach (var field in type.Fields)
        {
            if (ContainsLegacyReference(field) || ContainsLegacyReference(field.FieldType))
            {
                matches.Add($"FIELD {field.FullName} : {Describe(field.FieldType)}");
            }
        }

        foreach (var method in type.Methods)
        {
            if (ContainsLegacyReference(method.ReturnType))
            {
                matches.Add($"METHOD return {method.FullName}");
            }

            foreach (var parameter in method.Parameters)
            {
                if (ContainsLegacyReference(parameter.ParameterType))
                {
                    matches.Add($"PARAM {method.FullName} :: {parameter.Name} {Describe(parameter.ParameterType)}");
                }
            }

            if (!method.HasBody)
            {
                continue;
            }

            foreach (var variable in method.Body.Variables)
            {
                if (ContainsLegacyReference(variable.VariableType))
                {
                    matches.Add($"LOCAL {method.FullName} :: {Describe(variable.VariableType)}");
                }
            }

            foreach (var instruction in method.Body.Instructions)
            {
                if (ContainsLegacyReference(instruction.Operand))
                {
                    matches.Add($"IL {method.FullName} {instruction.Offset:X4}: {instruction.OpCode} {Describe(instruction.Operand)}");
                }
            }
        }
    }

    if (matches.Count == 0)
    {
        Console.WriteLine("Inspection OK: no SetDirectorData/legacy map-effect references were found.");
        return 0;
    }

    Console.WriteLine("Inspection found legacy references:");
    foreach (var match in matches)
    {
        Console.WriteLine(match);
    }

    return 1;
}

static void RemoveLegacyHookConstructorBlock(MethodDefinition ctor, FieldDefinition legacyField)
{
    var instructions = ctor.Body.Instructions;
    var storeIndex = instructions.Select((Instruction, Index) => (Instruction, Index))
        .FirstOrDefault(x => x.Instruction.OpCode == OpCodes.Stfld && IsSameMember(x.Instruction.Operand, legacyField)).Index;

    if (storeIndex <= 0)
    {
        throw new InvalidOperationException("Could not find legacy map-effect hook assignment.");
    }

    var legacyReferenceIndex = instructions.Select((Instruction, Index) => (Instruction, Index))
        .FirstOrDefault(x => x.Index <= storeIndex && ContainsLegacyReference(x.Instruction.Operand)).Index;

    var startIndex = legacyReferenceIndex;
    if (startIndex > 0)
    {
        while (startIndex > 0)
        {
            startIndex--;
            if (instructions[startIndex].OpCode == OpCodes.Ldarg_0)
            {
                while (startIndex > 0 && instructions[startIndex - 1].OpCode == OpCodes.Ldarg_0)
                {
                    startIndex--;
                }

                break;
            }
        }
    }

    if (startIndex <= 0)
    {
        startIndex = storeIndex;
        while (startIndex > 0)
        {
            startIndex--;
            if (instructions[startIndex].OpCode == OpCodes.Ldarg_0)
            {
                while (startIndex > 0 && instructions[startIndex - 1].OpCode == OpCodes.Ldarg_0)
                {
                    startIndex--;
                }

                break;
            }
        }
    }

    var endIndex = storeIndex;
    var seenLogCall = false;
    while (endIndex + 1 < instructions.Count)
    {
        endIndex++;
        var instruction = instructions[endIndex];
        if (instruction.OpCode == OpCodes.Call && instruction.Operand is MethodReference method && method.FullName.Contains("BossMod.Service::Log(", StringComparison.Ordinal))
        {
            seenLogCall = true;
            break;
        }
    }

    if (!seenLogCall)
    {
        throw new InvalidOperationException("Could not find legacy map-effect hook log call boundary.");
    }

    NopRange(instructions, startIndex, endIndex);
}

static void RemoveLegacyHookDisposeCall(MethodDefinition dispose, FieldDefinition legacyField)
{
    var instructions = dispose.Body.Instructions;
    var loadIndex = instructions.Select((Instruction, Index) => (Instruction, Index))
        .FirstOrDefault(x => x.Instruction.OpCode == OpCodes.Ldfld && IsSameMember(x.Instruction.Operand, legacyField)).Index;

    if (loadIndex <= 0)
    {
        throw new InvalidOperationException("Could not find legacy map-effect hook dispose load.");
    }

    var startIndex = loadIndex;
    while (startIndex > 0 && instructions[startIndex].OpCode != OpCodes.Ldarg_0)
    {
        startIndex--;
    }

    var endIndex = loadIndex;
    while (endIndex + 1 < instructions.Count)
    {
        endIndex++;
        var instruction = instructions[endIndex];
        if ((instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
            instruction.Operand is MethodReference method &&
            method.Name == "Dispose")
        {
            break;
        }
    }

    NopRange(instructions, startIndex, endIndex);
}

static bool IsSameMember(object? operand, MemberReference member)
{
    return operand is MemberReference candidate &&
        candidate.Name == member.Name &&
        candidate.DeclaringType.FullName == member.DeclaringType.FullName;
}

static bool ContainsLegacyReference(object? value)
{
    return Describe(value).Contains("SetDirectorData", StringComparison.Ordinal) ||
        Describe(value).Contains("ProcessLegacyMapEffect", StringComparison.Ordinal);
}

static string Describe(object? value)
{
    return value switch
    {
        null => string.Empty,
        TypeReference type => type.FullName,
        MemberReference member => member.FullName,
        _ => value.ToString() ?? string.Empty,
    };
}

static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
{
    foreach (var type in types)
    {
        yield return type;

        foreach (var nested in AllTypes(type.NestedTypes))
        {
            yield return nested;
        }
    }
}

static void NopRange(Mono.Collections.Generic.Collection<Instruction> instructions, int startIndex, int endIndex)
{
    for (var i = startIndex; i <= endIndex; i++)
    {
        instructions[i].OpCode = OpCodes.Nop;
        instructions[i].Operand = null;
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
        ReadWrite = false,
        ReadingMode = ReadingMode.Immediate,
    };
}

static TypeDefinition RequireType(ModuleDefinition module, string fullName)
{
    return module.GetType(fullName) ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static void ResetBody(MethodDefinition method)
{
    method.Body.ExceptionHandlers.Clear();
    method.Body.Variables.Clear();
    method.Body.Instructions.Clear();
}

static void ResetVoidBody(MethodDefinition method)
{
    if (method.ReturnType.MetadataType != MetadataType.Void)
    {
        throw new InvalidOperationException($"Method does not return void: {method.FullName}");
    }

    ResetBody(method);
    method.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
}
}
