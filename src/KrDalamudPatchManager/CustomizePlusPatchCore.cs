using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace CustomizePlusKrActorPatcher;

internal static class CustomizePlusPatchCore
{
    public const string SupportedVersion = "2.2.0.3";
    public const string OriginalCustomizePlusSha256 = "3F4396A2A83E392E69517EBCAA19B8AEF57FA3604F7A5E188A4306B6738134C7";
    public const string OriginalGameDataSha256 = "7CE2D315AA10292FA3A241107FB9DE6CD38C1BE71EB25E1099BC0613BD8682A4";

    public static void Patch(string sourcePluginDirectory, string hookDirectory, string outputDirectory)
    {
        RequireDirectory(sourcePluginDirectory);
        RequireDirectory(hookDirectory);
        ValidateSupportedOriginal(sourcePluginDirectory);
        CopyDirectory(sourcePluginDirectory, outputDirectory);

        var gameDataDll = Path.Combine(outputDirectory, "Penumbra.GameData.dll");
        PatchKoreanActorValidation(gameDataDll, new[] { outputDirectory, sourcePluginDirectory, hookDirectory });
        PatchLoggedInLobbyFallback(gameDataDll, new[] { outputDirectory, sourcePluginDirectory, hookDirectory });
        PatchIncognitoSingleNameFallback(gameDataDll, new[] { outputDirectory, sourcePluginDirectory, hookDirectory });
        Verify(outputDirectory, hookDirectory);
    }

    public static void Verify(string pluginDirectory, string hookDirectory)
    {
        RequireDirectory(pluginDirectory);
        RequireDirectory(hookDirectory);
        var gameDataDll = Path.Combine(pluginDirectory, "Penumbra.GameData.dll");
        RequireFile(gameDataDll);

        using var resolver = CreateResolver(new[] { pluginDirectory, hookDirectory });
        using var assembly = AssemblyDefinition.ReadAssembly(gameDataDll, CreateReaderParameters(resolver));
        var factory = AllTypes(assembly.MainModule.Types)
            .FirstOrDefault(type => type.FullName == "Penumbra.GameData.Actors.ActorIdentifierFactory")
            ?? throw Unsupported("ActorIdentifierFactory 형식을 찾지 못했습니다.");

        var verifyPlayerNameMethods = factory.Methods
            .Where(method => method.Name == "VerifyPlayerName" &&
                method.Parameters.Count == 1 && method.ReturnType.FullName == "System.Boolean")
            .ToArray();
        if (verifyPlayerNameMethods.Length != 2 || verifyPlayerNameMethods.Any(method => !ReturnsTrue(method)))
        {
            throw new InvalidOperationException("한섭 캐릭터 이름 인식 패치가 없습니다.");
        }

        var verifyWorld = factory.Methods.FirstOrDefault(method =>
            method.Name == "VerifyWorld" && method.Parameters.Count == 1 &&
            method.ReturnType.FullName == "System.Boolean")
            ?? throw Unsupported("VerifyWorld 메서드를 찾지 못했습니다.");
        if (!ReturnsTrue(verifyWorld))
        {
            throw new InvalidOperationException("한섭 월드 ID 인식 패치가 없습니다.");
        }

        var nameDicts = AllTypes(assembly.MainModule.Types)
            .FirstOrDefault(type => type.FullName == "Penumbra.GameData.Data.NameDicts")
            ?? throw Unsupported("NameDicts 형식을 찾지 못했습니다.");
        var toWorldName = nameDicts.Methods.FirstOrDefault(method =>
            method.Name == "ToWorldName" && method.Parameters.Count == 1 &&
            method.ReturnType.FullName == "System.String")
            ?? throw Unsupported("NameDicts.ToWorldName 메서드를 찾지 못했습니다.");
        if (!toWorldName.HasBody ||
            !toWorldName.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Ldstr &&
                string.Equals(instruction.Operand as string, "KR World", StringComparison.Ordinal)) ||
            toWorldName.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Ldstr &&
                string.Equals(instruction.Operand as string, "Invalid", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("한섭 월드 표시 패치가 없습니다.");
        }

        VerifyLoggedInLobbyFallback(assembly.MainModule);
        VerifyIncognitoSingleNameFallback(assembly.MainModule);
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

    public static bool NeedsActorRuntimeUpgrade(string pluginDirectory, string hookDirectory)
    {
        try
        {
            var gameDataDll = Path.Combine(pluginDirectory, "Penumbra.GameData.dll");
            using var resolver = CreateResolver(new[] { pluginDirectory, hookDirectory });
            using var assembly = AssemblyDefinition.ReadAssembly(gameDataDll, CreateReaderParameters(resolver));
            var module = assembly.MainModule;
            return HasKoreanActorValidation(module) &&
                (!HasLoggedInLobbyFallback(GetAddLobbyCharacters(module)) ||
                 !HasIncognitoSingleNameFallback(GetIncognito(module)));
        }
        catch
        {
            return false;
        }
    }

    public static void UpgradeActorRuntime(string pluginDirectory, string hookDirectory)
    {
        var gameDataDll = Path.Combine(pluginDirectory, "Penumbra.GameData.dll");
        var dependencies = new[] { pluginDirectory, hookDirectory };
        PatchLoggedInLobbyFallback(gameDataDll, dependencies);
        PatchIncognitoSingleNameFallback(gameDataDll, dependencies);
        Verify(pluginDirectory, hookDirectory);
    }

    public static void ValidateSupportedOriginal(string pluginDirectory)
    {
        var customizePlusDll = Path.Combine(pluginDirectory, "CustomizePlus.dll");
        var gameDataDll = Path.Combine(pluginDirectory, "Penumbra.GameData.dll");
        RequireFile(customizePlusDll);
        RequireFile(gameDataDll);

        AssertSha256(customizePlusDll, OriginalCustomizePlusSha256, "CustomizePlus.dll");
        AssertSha256(gameDataDll, OriginalGameDataSha256, "Penumbra.GameData.dll");
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
                ?? throw Unsupported("ActorIdentifierFactory 형식을 찾지 못했습니다.");
            var verifyPlayerNameMethods = factory.Methods
                .Where(method => method.Name == "VerifyPlayerName" &&
                    method.Parameters.Count == 1 && method.ReturnType.FullName == "System.Boolean")
                .ToArray();
            if (verifyPlayerNameMethods.Length != 2)
            {
                throw Unsupported($"VerifyPlayerName 오버로드가 2개여야 하지만 {verifyPlayerNameMethods.Length}개입니다.");
            }

            foreach (var method in verifyPlayerNameMethods)
            {
                RewriteReturnTrue(method);
            }

            var verifyWorld = factory.Methods.FirstOrDefault(method =>
                method.Name == "VerifyWorld" && method.Parameters.Count == 1 &&
                method.ReturnType.FullName == "System.Boolean")
                ?? throw Unsupported("VerifyWorld 메서드를 찾지 못했습니다.");
            RewriteReturnTrue(verifyWorld);

            var nameDicts = AllTypes(module.Types)
                .FirstOrDefault(type => type.FullName == "Penumbra.GameData.Data.NameDicts")
                ?? throw Unsupported("NameDicts 형식을 찾지 못했습니다.");
            var toWorldName = nameDicts.Methods.FirstOrDefault(method =>
                method.Name == "ToWorldName" && method.Parameters.Count == 1 &&
                method.ReturnType.FullName == "System.String")
                ?? throw Unsupported("NameDicts.ToWorldName 메서드를 찾지 못했습니다.");
            var invalidWorldText = toWorldName.Body.Instructions
                .Where(instruction => instruction.OpCode == OpCodes.Ldstr &&
                    string.Equals(instruction.Operand as string, "Invalid", StringComparison.Ordinal))
                .ToArray();
            if (invalidWorldText.Length != 1)
            {
                throw Unsupported($"월드 fallback 문자열이 1개여야 하지만 {invalidWorldText.Length}개입니다.");
            }

            invalidWorldText[0].Operand = "KR World";
            WriteAssembly(assembly, tempPath, dllPath);
        }
        finally
        {
            DeleteIfExists(tempPath);
        }
    }

    // KR Dalamud can report IsLoggedIn as false after entering the game.
    // A valid player with a home world proves that this is not the character-select lobby.
    private static void PatchLoggedInLobbyFallback(string dllPath, string[] dependencyDirectories)
    {
        using var resolver = CreateResolver(dependencyDirectories);
        using var assembly = AssemblyDefinition.ReadAssembly(dllPath, CreateReaderParameters(resolver));
        var module = assembly.MainModule;
        var tempPath = dllPath + ".krpatch.tmp";

        try
        {
            var actorObjectManager = AllTypes(module.Types)
                .FirstOrDefault(type => type.FullName == "Penumbra.GameData.Interop.ActorObjectManager")
                ?? throw Unsupported("ActorObjectManager was not found for the lobby fallback.");
            var addLobbyCharacters = actorObjectManager.Methods.FirstOrDefault(method =>
                method.Name == "AddLobbyCharacters" && method.Parameters.Count == 0)
                ?? throw Unsupported("ActorObjectManager.AddLobbyCharacters was not found for the lobby fallback.");
            var loginFalseBranch = FindLobbyLoginFalseBranch(addLobbyCharacters);
            var existingFallbackStart = FindLobbyFallbackStart(addLobbyCharacters);
            if (existingFallbackStart != null)
            {
                if (ReferenceEquals(loginFalseBranch.Operand, existingFallbackStart))
                    return;

                loginFalseBranch.Operand = existingFallbackStart;
                WriteAssembly(assembly, tempPath, dllPath);
                return;
            }

            var player = actorObjectManager.Properties.FirstOrDefault(property => property.Name == "Player")?.GetMethod
                ?? throw Unsupported("ActorObjectManager.Player was not found for the lobby fallback.");
            var actor = player.ReturnType.Resolve()
                ?? throw Unsupported("Actor type could not be resolved for the lobby fallback.");
            var valid = actor.Properties.FirstOrDefault(property => property.Name == "Valid")?.GetMethod
                ?? throw Unsupported("Actor.Valid was not found for the lobby fallback.");
            var homeWorld = actor.Properties.FirstOrDefault(property => property.Name == "HomeWorld")?.GetMethod
                ?? throw Unsupported("Actor.HomeWorld was not found for the lobby fallback.");
            var lobbyTarget = (Instruction)loginFalseBranch.Operand;

            var playerLocal = new VariableDefinition(module.ImportReference(player.ReturnType));
            addLobbyCharacters.Body.Variables.Add(playerLocal);
            addLobbyCharacters.Body.InitLocals = true;
            var il = addLobbyCharacters.Body.GetILProcessor();
            var instructions = new[]
            {
                il.Create(OpCodes.Ldarg_0),
                il.Create(OpCodes.Call, module.ImportReference(player)),
                il.Create(OpCodes.Stloc, playerLocal),
                il.Create(OpCodes.Ldloca, playerLocal),
                il.Create(OpCodes.Call, module.ImportReference(valid)),
                il.Create(OpCodes.Brfalse, lobbyTarget),
                il.Create(OpCodes.Ldloca, playerLocal),
                il.Create(OpCodes.Call, module.ImportReference(homeWorld)),
                il.Create(OpCodes.Brfalse, lobbyTarget),
                il.Create(OpCodes.Ldc_I4_0),
                il.Create(OpCodes.Ret),
            };
            foreach (var instruction in instructions)
                il.InsertBefore(lobbyTarget, instruction);
            loginFalseBranch.Operand = instructions[0];

            WriteAssembly(assembly, tempPath, dllPath);
        }
        finally
        {
            DeleteIfExists(tempPath);
        }
    }

    // ActorIdentifier.Incognito assumes a western multi-word character name.
    // Trim a trailing separator and leave a single Korean name unchanged.
    private static void PatchIncognitoSingleNameFallback(string dllPath, string[] dependencyDirectories)
    {
        using var resolver = CreateResolver(dependencyDirectories);
        using var assembly = AssemblyDefinition.ReadAssembly(dllPath, CreateReaderParameters(resolver));
        var module = assembly.MainModule;
        var tempPath = dllPath + ".krpatch.tmp";

        try
        {
            var identifier = AllTypes(module.Types)
                .FirstOrDefault(type => type.FullName == "Penumbra.GameData.Actors.ActorIdentifier")
                ?? throw Unsupported("ActorIdentifier was not found for the single-name fallback.");
            var incognito = identifier.Methods.FirstOrDefault(method =>
                method.Name == "Incognito" && method.Parameters.Count == 1 &&
                method.Parameters[0].ParameterType.FullName == "System.String")
                ?? throw Unsupported("ActorIdentifier.Incognito(string) was not found for the single-name fallback.");
            var namePresentBranch = FindIncognitoNamePresentBranch(incognito);
            var existingFallbackStart = FindIncognitoSingleNameFallbackStart(incognito);
            if (existingFallbackStart != null)
            {
                if (ReferenceEquals(namePresentBranch.Operand, existingFallbackStart))
                    return;

                namePresentBranch.Operand = existingFallbackStart;
                WriteAssembly(assembly, tempPath, dllPath);
                return;
            }

            var nameReady = (Instruction)namePresentBranch.Operand;
            var trim = module.ImportReference(typeof(string).GetMethod(nameof(string.Trim), Type.EmptyTypes)!);
            var indexOf = module.ImportReference(typeof(string).GetMethod(nameof(string.IndexOf), [typeof(char)])!);
            var il = incognito.Body.GetILProcessor();
            var returnOriginal = il.Create(OpCodes.Ldarg_1);
            var instructions = new[]
            {
                il.Create(OpCodes.Ldarg_1),
                il.Create(OpCodes.Callvirt, trim),
                il.Create(OpCodes.Starg, incognito.Parameters[0]),
                il.Create(OpCodes.Ldarg_1),
                il.Create(OpCodes.Ldc_I4_S, (sbyte)' '),
                il.Create(OpCodes.Callvirt, indexOf),
                il.Create(OpCodes.Ldc_I4_0),
                il.Create(OpCodes.Bge_S, nameReady),
                returnOriginal,
                il.Create(OpCodes.Ret),
            };
            foreach (var instruction in instructions)
                il.InsertBefore(nameReady, instruction);
            namePresentBranch.Operand = instructions[0];

            WriteAssembly(assembly, tempPath, dllPath);
        }
        finally
        {
            DeleteIfExists(tempPath);
        }
    }

    private static void VerifyLoggedInLobbyFallback(ModuleDefinition module)
    {
        var addLobbyCharacters = GetAddLobbyCharacters(module);
        if (!HasLoggedInLobbyFallback(addLobbyCharacters))
            throw new InvalidOperationException("Customize+ logged-in lobby fallback is not present.");
    }

    private static void VerifyIncognitoSingleNameFallback(ModuleDefinition module)
    {
        var incognito = GetIncognito(module);
        if (!HasIncognitoSingleNameFallback(incognito))
            throw new InvalidOperationException("Customize+ Korean single-name fallback is not present.");
    }

    private static bool HasLoggedInLobbyFallback(MethodDefinition method)
    {
        var fallbackStart = FindLobbyFallbackStart(method);
        return fallbackStart != null && ReferenceEquals(FindLobbyLoginFalseBranch(method).Operand, fallbackStart);
    }

    private static bool HasIncognitoSingleNameFallback(MethodDefinition method)
    {
        var fallbackStart = FindIncognitoSingleNameFallbackStart(method);
        return fallbackStart != null && ReferenceEquals(FindIncognitoNamePresentBranch(method).Operand, fallbackStart);
    }

    private static Instruction FindIncognitoNamePresentBranch(MethodDefinition method)
        => method.Body.Instructions.FirstOrDefault(instruction =>
               instruction.OpCode.Code is Code.Brtrue or Code.Brtrue_S && instruction.Operand is Instruction)
           ?? throw Unsupported("ActorIdentifier.Incognito(string) name branch was not found.");

    private static Instruction? FindIncognitoSingleNameFallbackStart(MethodDefinition method)
        => method.Body.Instructions.FirstOrDefault(instruction =>
            instruction.OpCode == OpCodes.Ldarg_1 &&
            instruction.Next?.OpCode == OpCodes.Callvirt &&
            instruction.Next.Operand is MethodReference { DeclaringType.FullName: "System.String", Name: nameof(string.Trim), Parameters.Count: 0 });

    private static MethodDefinition GetAddLobbyCharacters(ModuleDefinition module)
        => AllTypes(module.Types)
               .FirstOrDefault(type => type.FullName == "Penumbra.GameData.Interop.ActorObjectManager")?.Methods
               .FirstOrDefault(method => method.Name == "AddLobbyCharacters" && method.Parameters.Count == 0)
           ?? throw Unsupported("ActorObjectManager.AddLobbyCharacters was not found during lobby fallback verification.");

    private static MethodDefinition GetIncognito(ModuleDefinition module)
        => AllTypes(module.Types)
               .FirstOrDefault(type => type.FullName == "Penumbra.GameData.Actors.ActorIdentifier")?.Methods
               .FirstOrDefault(method => method.Name == "Incognito" && method.Parameters.Count == 1 &&
                   method.Parameters[0].ParameterType.FullName == "System.String")
           ?? throw Unsupported("ActorIdentifier.Incognito(string) was not found during single-name fallback verification.");

    private static bool HasKoreanActorValidation(ModuleDefinition module)
    {
        var factory = AllTypes(module.Types)
            .FirstOrDefault(type => type.FullName == "Penumbra.GameData.Actors.ActorIdentifierFactory");
        if (factory == null)
            return false;
        var nameMethods = factory.Methods.Where(method => method.Name == "VerifyPlayerName" &&
            method.Parameters.Count == 1 && method.ReturnType.FullName == "System.Boolean").ToArray();
        var verifyWorld = factory.Methods.FirstOrDefault(method => method.Name == "VerifyWorld" &&
            method.Parameters.Count == 1 && method.ReturnType.FullName == "System.Boolean");
        var worldName = AllTypes(module.Types).FirstOrDefault(type => type.FullName == "Penumbra.GameData.Data.NameDicts")?.Methods
            .FirstOrDefault(method => method.Name == "ToWorldName" && method.Parameters.Count == 1 &&
                method.ReturnType.FullName == "System.String");
        return nameMethods.Length == 2 && nameMethods.All(ReturnsTrue) && verifyWorld != null && ReturnsTrue(verifyWorld) &&
            worldName?.HasBody == true && worldName.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Ldstr &&
                string.Equals(instruction.Operand as string, "KR World", StringComparison.Ordinal)) &&
            !worldName.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Ldstr &&
                string.Equals(instruction.Operand as string, "Invalid", StringComparison.Ordinal));
    }

    private static Instruction FindLobbyLoginFalseBranch(MethodDefinition method)
    {
        var loginCheckIndex = method.Body.Instructions
            .Select((instruction, index) => (instruction, index))
            .Where(pair => pair.instruction.Operand is MethodReference reference && reference.Name == "get_IsLoggedIn")
            .Select(pair => pair.index)
            .DefaultIfEmpty(-1)
            .First();
        if (loginCheckIndex < 0)
            throw Unsupported("ActorObjectManager lobby login check was not found.");
        return method.Body.Instructions.Skip(loginCheckIndex + 1).FirstOrDefault(instruction =>
                   instruction.OpCode.Code is Code.Brfalse or Code.Brfalse_S && instruction.Operand is Instruction)
               ?? throw Unsupported("ActorObjectManager lobby login false branch was not found.");
    }

    private static Instruction? FindLobbyFallbackStart(MethodDefinition method)
    {
        var instructions = method.Body.Instructions;
        for (var index = 0; index < instructions.Count; ++index)
        {
            if (instructions[index].Operand is not MethodReference { Name: "get_Player" })
                continue;
            if (!instructions.Skip(index + 1).Take(12).Any(instruction => instruction.Operand is MethodReference { Name: "get_HomeWorld" }))
                continue;
            if (instructions[index].Previous?.OpCode != OpCodes.Ldarg_0)
                throw Unsupported("ActorObjectManager existing lobby fallback has an unsupported shape.");
            return instructions[index].Previous;
        }

        return null;
    }

    private static bool ReturnsTrue(MethodDefinition method)
        => method.HasBody && method.Body.Instructions.Count == 2 &&
           method.Body.Instructions[0].OpCode == OpCodes.Ldc_I4_1 &&
           method.Body.Instructions[1].OpCode == OpCodes.Ret;

    private static void RewriteReturnTrue(MethodDefinition method)
    {
        if (!method.HasBody)
        {
            throw Unsupported($"메서드 본문이 없습니다: {method.FullName}");
        }

        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        method.Body.InitLocals = false;
        method.Body.Instructions.Clear();
        var processor = method.Body.GetILProcessor();
        processor.Append(processor.Create(OpCodes.Ldc_I4_1));
        processor.Append(processor.Create(OpCodes.Ret));
    }

    private static void AssertSha256(string path, string expected, string label)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw Unsupported($"{label} SHA-256이 지원 대상과 다릅니다.\r\n예상: {expected}\r\n실제: {actual}");
        }
    }

    private static DefaultAssemblyResolver CreateResolver(IEnumerable<string> directories)
    {
        var resolver = new DefaultAssemblyResolver();
        foreach (var directory in directories.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
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
        => new($"지원하지 않는 Customize+ DLL 구조입니다. Customize+ {SupportedVersion} 공식 설치본만 지원합니다.\r\n\r\n{detail}");

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
