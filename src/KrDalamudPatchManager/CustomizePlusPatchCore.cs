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
