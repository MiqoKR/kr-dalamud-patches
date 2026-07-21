using Mono.Cecil;
using Mono.Cecil.Cil;

namespace GlamourerKrActorPatcher;

internal static class GlamourerPatchCore
{
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
