using Mono.Cecil;
using Mono.Cecil.Cil;

namespace GlamourerKrActorPatcher;

internal static class GlamourerPatchCore
{
    private static readonly (ushort Id, string Name)[] KoreanWorldNames =
    [
        (2050, "바하무트"), (2051, "이프리트"), (2052, "가루다"), (2053, "라무"),
        (2054, "오딘"), (2055, "알테마"), (2056, "만드라고라"), (2057, "구 톤베리"),
        (2058, "엑스칼리버"), (2059, "피닉스"), (2060, "알렉산더"), (2061, "타이탄"),
        (2062, "리바이어선"), (2063, "시바"), (2064, "베히모스"), (2065, "길가메시"),
        (2066, "사보텐더"), (2067, "유니콘"), (2068, "라그나로크"), (2069, "라미아"),
        (2075, "카벙클"), (2076, "초코보"), (2077, "모그리"), (2078, "톤베리"),
        (2079, "캐트시"), (2080, "펜리르"), (2081, "오메가"),
    ];

    public static void Patch(string pluginDirectory, string hookDirectory, string outputDirectory)
    {
        RequireDirectory(pluginDirectory);
        RequireDirectory(hookDirectory);
        CopyDirectory(pluginDirectory, outputDirectory);

        var glamourerDll = Path.Combine(outputDirectory, "Glamourer.dll");
        var gameDataDll = Path.Combine(outputDirectory, "Penumbra.GameData.dll");
        RequireFile(glamourerDll);
        RequireFile(gameDataDll);

        var dependencyDirectories = new[] { outputDirectory, pluginDirectory, hookDirectory };
        PatchKoreanActorValidation(gameDataDll, dependencyDirectories);
        PatchKoreanWorldDisplay(gameDataDll, dependencyDirectories);
        PatchCreateNewModel(glamourerDll, dependencyDirectories);
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
        var glamourerDll = Path.Combine(pluginDirectory, "Glamourer.dll");
        var gameDataDll = Path.Combine(pluginDirectory, "Penumbra.GameData.dll");
        RequireFile(glamourerDll);
        RequireFile(gameDataDll);

        var dependencies = new[] { pluginDirectory, hookDirectory };
        VerifyCreateNewModel(glamourerDll, dependencies);
        VerifyKoreanActorValidation(gameDataDll, dependencies);
        VerifyKoreanWorldDisplay(gameDataDll, dependencies);
    }

    public static bool NeedsWorldDisplayUpgrade(string pluginDirectory, string hookDirectory)
    {
        try
        {
            var glamourerDll = Path.Combine(pluginDirectory, "Glamourer.dll");
            var gameDataDll = Path.Combine(pluginDirectory, "Penumbra.GameData.dll");
            var dependencies = new[] { pluginDirectory, hookDirectory };
            VerifyCreateNewModel(glamourerDll, dependencies);
            VerifyKoreanActorValidation(gameDataDll, dependencies);
            return !IsKoreanWorldDisplayPatched(gameDataDll, dependencies);
        }
        catch
        {
            return false;
        }
    }

    public static void UpgradeWorldDisplay(string pluginDirectory, string hookDirectory)
    {
        var gameDataDll = Path.Combine(pluginDirectory, "Penumbra.GameData.dll");
        var dependencies = new[] { pluginDirectory, hookDirectory };
        PatchKoreanWorldDisplay(gameDataDll, dependencies);
        VerifyKoreanWorldDisplay(gameDataDll, dependencies);
    }

    private static void PatchCreateNewModel(string dllPath, string[] dependencyDirectories)
    {
        using var resolver = CreateResolver(dependencyDirectories);
        using var assembly = AssemblyDefinition.ReadAssembly(dllPath, CreateReaderParameters(resolver));
        var module = assembly.MainModule;
        var tempPath = dllPath + ".krpatch.tmp";

        try
        {
            var createNewModel = AllTypes(module.Types)
                .FirstOrDefault(type => type.FullName == "Glamourer.Interop.Material.CreateNewModel")
                ?? throw Unsupported("CreateNewModel type was not found.");
            var delegateType = createNewModel.NestedTypes.FirstOrDefault(type => type.Name == "Delegate")
                ?? throw Unsupported("CreateNewModel delegate was not found.");
            var detour = createNewModel.Methods.FirstOrDefault(method => method.Name == "Detour")
                ?? throw Unsupported("CreateNewModel detour was not found.");
            var invoke = delegateType.Methods.FirstOrDefault(method => method.Name == "Invoke")
                ?? throw Unsupported("CreateNewModel delegate Invoke was not found.");
            var beginInvoke = delegateType.Methods.FirstOrDefault(method => method.Name == "BeginInvoke")
                ?? throw Unsupported("CreateNewModel delegate BeginInvoke was not found.");
            var endInvoke = delegateType.Methods.FirstOrDefault(method => method.Name == "EndInvoke")
                ?? throw Unsupported("CreateNewModel delegate EndInvoke was not found.");

            var intPtr = module.ImportReference(typeof(IntPtr));
            var characterBase = FindTypeReference(module, "FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CharacterBase")
                ?? throw Unsupported("FFXIVClientStructs CharacterBase reference was not found.");
            var characterBasePointer = new PointerType(characterBase);
            var modelType = FindTypeReference(module, "Penumbra.GameData.Interop.Model")
                ?? throw Unsupported("Penumbra model reference was not found.");
            var modelFromCharacterBasePointer = FindModelImplicitOperator(module, modelType, characterBasePointer);

            PatchDelegateSignature(invoke, intPtr, characterBasePointer);
            PatchDelegateSignature(beginInvoke, beginInvoke.ReturnType, characterBasePointer);
            endInvoke.ReturnType = intPtr;
            detour.ReturnType = intPtr;
            if (detour.Parameters.Count == 0)
            {
                throw Unsupported("CreateNewModel detour has no parameters.");
            }

            detour.Parameters[0].ParameterType = characterBasePointer;
            RewriteCreateNewModelDetour(detour, modelType, modelFromCharacterBasePointer);
            WriteAssembly(assembly, tempPath, dllPath);
        }
        finally
        {
            DeleteIfExists(tempPath);
        }
    }

    private static void PatchKoreanActorValidation(string dllPath, string[] dependencyDirectories)
    {
        using var resolver = CreateResolver(dependencyDirectories);
        using var assembly = AssemblyDefinition.ReadAssembly(dllPath, CreateReaderParameters(resolver));
        var module = assembly.MainModule;
        var tempPath = dllPath + ".krpatch.tmp";

        try
        {
            var factory = AllTypes(module.Types)
                .FirstOrDefault(type => type.FullName == "Penumbra.GameData.Actors.ActorIdentifierFactory")
                ?? throw Unsupported("ActorIdentifierFactory was not found.");
            var verifyPlayerNameMethods = factory.Methods
                .Where(method => method.Name == "VerifyPlayerName" && method.Parameters.Count == 1 && method.ReturnType.FullName == "System.Boolean")
                .ToArray();

            if (verifyPlayerNameMethods.Length < 2)
            {
                throw Unsupported($"Expected two VerifyPlayerName overloads, found {verifyPlayerNameMethods.Length}.");
            }

            foreach (var method in verifyPlayerNameMethods)
            {
                RewriteReturnTrue(method);
            }

            var verifyWorld = factory.Methods.FirstOrDefault(method =>
                method.Name == "VerifyWorld" && method.Parameters.Count == 1 && method.ReturnType.FullName == "System.Boolean")
                ?? throw Unsupported("VerifyWorld was not found.");
            RewriteReturnTrue(verifyWorld);
            WriteAssembly(assembly, tempPath, dllPath);
        }
        finally
        {
            DeleteIfExists(tempPath);
        }
    }

    private static void VerifyCreateNewModel(string dllPath, string[] dependencyDirectories)
    {
        using var resolver = CreateResolver(dependencyDirectories);
        using var assembly = AssemblyDefinition.ReadAssembly(dllPath, CreateReaderParameters(resolver));
        var createNewModel = AllTypes(assembly.MainModule.Types)
            .FirstOrDefault(type => type.FullName == "Glamourer.Interop.Material.CreateNewModel")
            ?? throw Unsupported("CreateNewModel type was not found during verification.");
        var delegateType = createNewModel.NestedTypes.FirstOrDefault(type => type.Name == "Delegate")
            ?? throw Unsupported("CreateNewModel delegate was not found during verification.");
        var invoke = delegateType.Methods.FirstOrDefault(method => method.Name == "Invoke")
            ?? throw Unsupported("CreateNewModel delegate Invoke was not found during verification.");
        var detour = createNewModel.Methods.FirstOrDefault(method => method.Name == "Detour")
            ?? throw Unsupported("CreateNewModel detour was not found during verification.");

        if (invoke.ReturnType.FullName != "System.IntPtr" || detour.ReturnType.FullName != "System.IntPtr" ||
            invoke.Parameters.Count == 0 || detour.Parameters.Count == 0 ||
            invoke.Parameters[0].ParameterType.FullName != "FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CharacterBase*" ||
            detour.Parameters[0].ParameterType.FullName != "FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CharacterBase*")
        {
            throw new InvalidOperationException("CreateNewModel KR signature patch is not present.");
        }
    }

    private static void VerifyKoreanActorValidation(string dllPath, string[] dependencyDirectories)
    {
        using var resolver = CreateResolver(dependencyDirectories);
        using var assembly = AssemblyDefinition.ReadAssembly(dllPath, CreateReaderParameters(resolver));
        var factory = AllTypes(assembly.MainModule.Types)
            .FirstOrDefault(type => type.FullName == "Penumbra.GameData.Actors.ActorIdentifierFactory")
            ?? throw Unsupported("ActorIdentifierFactory was not found during verification.");
        var methods = factory.Methods
            .Where(method => (method.Name == "VerifyPlayerName" || method.Name == "VerifyWorld") &&
                method.Parameters.Count == 1 && method.ReturnType.FullName == "System.Boolean")
            .ToArray();

        if (methods.Length < 3 || methods.Any(method => !ReturnsTrue(method)))
        {
            throw new InvalidOperationException("Korean actor validation patch is not present.");
        }
    }

    private static void PatchKoreanWorldDisplay(string dllPath, string[] dependencyDirectories)
    {
        using var resolver = CreateResolver(dependencyDirectories);
        using var assembly = AssemblyDefinition.ReadAssembly(dllPath, CreateReaderParameters(resolver));
        var module = assembly.MainModule;
        var tempPath = dllPath + ".krpatch.tmp";

        try
        {
            if (IsKoreanWorldDisplayPatched(module))
                return;

            var changed = false;
            var dictWorld = AllTypes(module.Types)
                .FirstOrDefault(type => type.FullName == "Penumbra.GameData.DataContainers.DictWorld")
                ?? throw Unsupported("DictWorld was not found.");
            var isValid = dictWorld.Methods.FirstOrDefault(method =>
                method.Name == "IsValid" && method.IsStatic && method.Parameters.Count == 1 &&
                method.ReturnType.FullName == "System.Boolean")
                ?? throw Unsupported("DictWorld.IsValid was not found.");

            if (!IsKoreanWorldDictionaryPatched(isValid))
            {
                var upperCaseCheck = isValid.Body.Instructions.FirstOrDefault(instruction =>
                    instruction.OpCode == OpCodes.Call && instruction.Operand is MethodReference method &&
                    method.DeclaringType.FullName == "System.Char" && method.Name == "IsUpper" &&
                    method.Parameters.Count == 1);
                if (upperCaseCheck == null)
                    throw Unsupported("DictWorld.IsValid upper-case world-name filter was not found.");

                upperCaseCheck.OpCode = OpCodes.Pop;
                upperCaseCheck.Operand = null;
                isValid.Body.GetILProcessor().InsertAfter(upperCaseCheck, Instruction.Create(OpCodes.Ldc_I4_1));
                changed = true;
            }

            changed |= PatchKoreanWorldNameFallback(module);
            if (!changed || !IsKoreanWorldDisplayPatched(module))
                throw Unsupported("Korean world display patch verification failed before writing.");

            WriteAssembly(assembly, tempPath, dllPath);
        }
        finally
        {
            DeleteIfExists(tempPath);
        }
    }

    private static void VerifyKoreanWorldDisplay(string dllPath, string[] dependencyDirectories)
    {
        using var resolver = CreateResolver(dependencyDirectories);
        using var assembly = AssemblyDefinition.ReadAssembly(dllPath, CreateReaderParameters(resolver));
        if (!IsKoreanWorldDisplayPatched(assembly.MainModule))
            throw new InvalidOperationException("Korean world display patch is not present.");
    }

    private static bool IsKoreanWorldDisplayPatched(string dllPath, string[] dependencyDirectories)
    {
        using var resolver = CreateResolver(dependencyDirectories);
        using var assembly = AssemblyDefinition.ReadAssembly(dllPath, CreateReaderParameters(resolver));
        return IsKoreanWorldDisplayPatched(assembly.MainModule);
    }

    private static bool IsKoreanWorldDisplayPatched(ModuleDefinition module)
    {
        var dictWorld = AllTypes(module.Types)
            .FirstOrDefault(type => type.FullName == "Penumbra.GameData.DataContainers.DictWorld");
        var isValid = dictWorld?.Methods.FirstOrDefault(method =>
            method.Name == "IsValid" && method.IsStatic && method.Parameters.Count == 1 &&
            method.ReturnType.FullName == "System.Boolean");
        return isValid != null && IsKoreanWorldDictionaryPatched(isValid) && IsKoreanWorldNameFallbackPatched(module);
    }

    private static bool IsKoreanWorldDictionaryPatched(MethodDefinition isValid)
    {
        var instructions = isValid.Body?.Instructions;
        if (instructions == null || instructions.Any(instruction =>
                instruction.Operand is MethodReference method && method.DeclaringType.FullName == "System.Char" &&
                method.Name == "IsUpper"))
        {
            return false;
        }

        return instructions.Zip(instructions.Skip(1), (first, second) =>
            first.OpCode == OpCodes.Pop && second.OpCode == OpCodes.Ldc_I4_1).Any(pair => pair);
    }

    private static bool PatchKoreanWorldNameFallback(ModuleDefinition module)
    {
        var nameDicts = AllTypes(module.Types)
            .FirstOrDefault(type => type.FullName == "Penumbra.GameData.Data.NameDicts")
            ?? throw Unsupported("NameDicts was not found.");
        var existingFallback = nameDicts.Methods.FirstOrDefault(method =>
            method.Name == "GetKoreanWorldNameFallback" && method.IsStatic && method.Parameters.Count == 1 &&
            method.ReturnType.FullName == "System.String");
        if (existingFallback != null)
            return PatchKoreanDefaultWorldFallback(existingFallback);

        var toWorldName = nameDicts.Methods.FirstOrDefault(method =>
            method.Name == "ToWorldName" && method.Parameters.Count == 1 && method.ReturnType.FullName == "System.String")
            ?? throw Unsupported("NameDicts.ToWorldName was not found.");
        var worldIdType = AllTypes(module.Types)
            .FirstOrDefault(type => type.FullName == "Penumbra.GameData.Structs.WorldId")
            ?? throw Unsupported("WorldId was not found.");
        var toUInt16 = worldIdType.Methods.FirstOrDefault(method =>
            method.Name == "op_Implicit" && method.Parameters.Count == 1 && method.ReturnType.MetadataType == MetadataType.UInt16)
            ?? throw Unsupported("WorldId to UInt16 implicit conversion was not found.");

        var fallback = new MethodDefinition(
            "GetKoreanWorldNameFallback",
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
            module.TypeSystem.String);
        fallback.Parameters.Add(new ParameterDefinition("worldId", ParameterAttributes.None, module.ImportReference(worldIdType)));
        nameDicts.Methods.Add(fallback);

        var il = fallback.Body.GetILProcessor();
        var labels = KoreanWorldNames.Select(_ => Instruction.Create(OpCodes.Nop)).ToArray();
        for (var i = 0; i < KoreanWorldNames.Length; ++i)
        {
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Call, module.ImportReference(toUInt16)));
            il.Append(il.Create(OpCodes.Ldc_I4, (int)KoreanWorldNames[i].Id));
            il.Append(il.Create(OpCodes.Beq, labels[i]));
        }

        il.Append(il.Create(OpCodes.Ldstr, "KR"));
        il.Append(il.Create(OpCodes.Ret));
        for (var i = 0; i < KoreanWorldNames.Length; ++i)
        {
            il.Append(labels[i]);
            il.Append(il.Create(OpCodes.Ldstr, KoreanWorldNames[i].Name));
            il.Append(il.Create(OpCodes.Ret));
        }

        var invalidFallback = toWorldName.Body.Instructions.SingleOrDefault(instruction =>
            instruction.OpCode == OpCodes.Ldstr && string.Equals(instruction.Operand as string, "Invalid", StringComparison.Ordinal));
        if (invalidFallback == null)
            throw Unsupported("NameDicts.ToWorldName Invalid fallback was not found.");

        invalidFallback.OpCode = OpCodes.Ldarg_1;
        invalidFallback.Operand = null;
        toWorldName.Body.GetILProcessor().InsertAfter(invalidFallback, Instruction.Create(OpCodes.Call, fallback));
        return true;
    }

    private static bool IsKoreanWorldNameFallbackPatched(ModuleDefinition module)
    {
        var nameDicts = AllTypes(module.Types)
            .FirstOrDefault(type => type.FullName == "Penumbra.GameData.Data.NameDicts");
        return nameDicts != null && IsKoreanWorldNameFallbackPatched(nameDicts);
    }

    private static bool IsKoreanWorldNameFallbackPatched(TypeDefinition nameDicts)
    {
        var fallback = nameDicts.Methods.FirstOrDefault(method =>
            method.Name == "GetKoreanWorldNameFallback" && method.IsStatic && method.Parameters.Count == 1 &&
            method.ReturnType.FullName == "System.String");
        var toWorldName = nameDicts.Methods.FirstOrDefault(method =>
            method.Name == "ToWorldName" && method.Parameters.Count == 1 && method.ReturnType.FullName == "System.String");
        return fallback != null && toWorldName?.Body?.Instructions.Any(instruction =>
            instruction.Operand is MethodReference method && method.Name == fallback.Name &&
            method.DeclaringType.FullName == nameDicts.FullName) == true &&
            fallback.Body.Instructions.Any(instruction =>
                instruction.OpCode == OpCodes.Ldstr && string.Equals(instruction.Operand as string, "KR", StringComparison.Ordinal)) &&
            KoreanWorldNames.All(world => fallback.Body.Instructions.Any(instruction =>
                instruction.OpCode == OpCodes.Ldstr && string.Equals(instruction.Operand as string, world.Name, StringComparison.Ordinal)));
    }

    private static bool PatchKoreanDefaultWorldFallback(MethodDefinition fallback)
    {
        var invalidFallback = fallback.Body.Instructions.SingleOrDefault(instruction =>
            instruction.OpCode == OpCodes.Ldstr && string.Equals(instruction.Operand as string, "Invalid", StringComparison.Ordinal));
        if (invalidFallback == null)
            return false;

        invalidFallback.Operand = "KR";
        return true;
    }

    private static bool ReturnsTrue(MethodDefinition method)
        => method.HasBody && method.Body.Instructions.Count == 2 &&
           method.Body.Instructions[0].OpCode == OpCodes.Ldc_I4_1 &&
           method.Body.Instructions[1].OpCode == OpCodes.Ret;

    private static void RewriteReturnTrue(MethodDefinition method)
    {
        method.Body ??= new MethodBody(method);
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        method.Body.InitLocals = false;
        method.Body.Instructions.Clear();
        var processor = method.Body.GetILProcessor();
        processor.Append(processor.Create(OpCodes.Ldc_I4_1));
        processor.Append(processor.Create(OpCodes.Ret));
    }

    private static void PatchDelegateSignature(MethodDefinition method, TypeReference returnType, TypeReference firstParameterType)
    {
        method.ReturnType = returnType;
        if (method.Parameters.Count == 0)
        {
            throw Unsupported($"{method.FullName} has no parameters.");
        }

        method.Parameters[0].ParameterType = firstParameterType;
    }

    private static void RewriteCreateNewModelDetour(MethodDefinition detour, TypeReference modelType, MethodReference modelFromCharacterBasePointer)
    {
        if (!detour.HasBody)
        {
            throw Unsupported("CreateNewModel detour has no body.");
        }

        var body = detour.Body;
        var oldInstructions = body.Instructions.ToArray();
        var updatingModelField = FindField(oldInstructions, "_updatingModel");
        var setValue = FindMethod(oldInstructions, method => method.Name == "set_Value" && method.DeclaringType.FullName.StartsWith("System.Threading.ThreadLocal`1", StringComparison.Ordinal), "ThreadLocal<Model>.set_Value");
        var getTask = FindMethod(oldInstructions, method => method.Name == "get_Task", "FastHook<Task>.get_Task");
        var getResult = FindMethod(oldInstructions, method => method.Name == "get_Result", "Task<Result>.get_Result");
        var getOriginal = FindMethod(oldInstructions, method => method.Name == "get_Original", "Hook<T>.get_Original");
        var invoke = FindMethod(oldInstructions, method => method.Name == "Invoke" && method.DeclaringType.FullName == "Glamourer.Interop.Material.CreateNewModel/Delegate", "CreateNewModel.Delegate.Invoke");
        var modelNull = oldInstructions.Select(instruction => instruction.Operand).OfType<FieldReference>()
            .FirstOrDefault(field => field.Name == "Null" && field.DeclaringType.FullName == modelType.FullName)
            ?? throw Unsupported("Model.Null field reference was not found.");

        body.Variables.Clear();
        body.Variables.Add(new VariableDefinition(detour.ReturnType));
        body.InitLocals = true;
        body.Instructions.Clear();
        var processor = body.GetILProcessor();
        processor.Append(processor.Create(OpCodes.Ldarg_0));
        processor.Append(processor.Create(OpCodes.Ldfld, updatingModelField));
        processor.Append(processor.Create(OpCodes.Ldarg_1));
        processor.Append(processor.Create(OpCodes.Call, modelFromCharacterBasePointer));
        processor.Append(processor.Create(OpCodes.Callvirt, setValue));
        processor.Append(processor.Create(OpCodes.Ldarg_0));
        processor.Append(processor.Create(OpCodes.Call, getTask));
        processor.Append(processor.Create(OpCodes.Callvirt, getResult));
        processor.Append(processor.Create(OpCodes.Callvirt, getOriginal));
        processor.Append(processor.Create(OpCodes.Ldarg_1));
        processor.Append(processor.Create(OpCodes.Ldarg_2));
        processor.Append(processor.Create(OpCodes.Callvirt, invoke));
        processor.Append(processor.Create(OpCodes.Stloc_0));
        processor.Append(processor.Create(OpCodes.Ldarg_0));
        processor.Append(processor.Create(OpCodes.Ldfld, updatingModelField));
        processor.Append(processor.Create(OpCodes.Ldsfld, modelNull));
        processor.Append(processor.Create(OpCodes.Callvirt, setValue));
        processor.Append(processor.Create(OpCodes.Ldloc_0));
        processor.Append(processor.Create(OpCodes.Ret));
    }

    private static FieldReference FindField(IEnumerable<Instruction> instructions, string name)
        => instructions.Select(instruction => instruction.Operand).OfType<FieldReference>()
               .FirstOrDefault(field => field.Name == name)
           ?? throw Unsupported($"{name} field reference was not found.");

    private static MethodReference FindMethod(IEnumerable<Instruction> instructions, Func<MethodReference, bool> predicate, string label)
        => instructions.Select(instruction => instruction.Operand).OfType<MethodReference>().FirstOrDefault(predicate)
           ?? throw Unsupported($"{label} reference was not found.");

    private static TypeReference? FindTypeReference(ModuleDefinition module, string fullName)
        => module.GetTypeReferences().FirstOrDefault(type => type.FullName == fullName);

    private static MethodReference FindModelImplicitOperator(ModuleDefinition module, TypeReference modelType, TypeReference characterBasePointer)
    {
        var existing = module.GetMemberReferences().OfType<MethodReference>().FirstOrDefault(method =>
            method.Name == "op_Implicit" && method.DeclaringType.FullName == modelType.FullName &&
            method.ReturnType.FullName == modelType.FullName && method.Parameters.Count == 1 &&
            method.Parameters[0].ParameterType.FullName == characterBasePointer.FullName);
        if (existing != null)
        {
            return existing;
        }

        var reference = new MethodReference("op_Implicit", modelType, modelType) { HasThis = false };
        reference.Parameters.Add(new ParameterDefinition(characterBasePointer));
        return module.ImportReference(reference);
    }

    private static DefaultAssemblyResolver CreateResolver(IEnumerable<string> searchDirectories)
    {
        var resolver = new DefaultAssemblyResolver();
        foreach (var directory in searchDirectories.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            resolver.AddSearchDirectory(directory);
        }

        return resolver;
    }

    private static ReaderParameters CreateReaderParameters(DefaultAssemblyResolver resolver)
        => new()
        {
            AssemblyResolver = resolver,
            InMemory = true,
            ReadWrite = false,
            ReadingMode = ReadingMode.Immediate,
        };

    private static void WriteAssembly(AssemblyDefinition assembly, string tempPath, string destinationPath)
    {
        DeleteIfExists(tempPath);
        assembly.Write(tempPath);
        File.Copy(tempPath, destinationPath, true);
    }

    private static void CopyDirectory(string sourceDirectory, string outputDirectory)
    {
        if (Directory.Exists(outputDirectory))
        {
            Directory.Delete(outputDirectory, true);
        }

        Directory.CreateDirectory(outputDirectory);
        foreach (var sourcePath in Directory.EnumerateFileSystemEntries(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var outputPath = Path.Combine(outputDirectory, Path.GetRelativePath(sourceDirectory, sourcePath));
            if (Directory.Exists(sourcePath))
            {
                Directory.CreateDirectory(outputPath);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                File.Copy(sourcePath, outputPath, true);
            }
        }
    }

    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
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

    private static InvalidOperationException Unsupported(string detail)
        => new($"지원하지 않는 Glamourer DLL 구조입니다. 공식 업데이트에 맞는 새 패처가 필요합니다.\r\n\r\n{detail}");

    private static void RequireFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("필수 파일을 찾을 수 없습니다.", path);
        }
    }

    private static void RequireDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"폴더를 찾을 수 없습니다: {path}");
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
