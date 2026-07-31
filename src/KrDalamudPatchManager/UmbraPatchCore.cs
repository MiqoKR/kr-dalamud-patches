using Mono.Cecil;
using Mono.Cecil.Cil;

namespace KrDalamudPatchManager.Modules;

internal static class UmbraPatchCore
{
    public static void Patch(string pluginDirectory, string hookDirectory, string outputDirectory)
    {
        if (!Directory.Exists(pluginDirectory))
            throw new DirectoryNotFoundException($"Umbra 플러그인 폴더를 찾지 못했습니다: {pluginDirectory}");
        if (!Directory.Exists(hookDirectory))
            throw new DirectoryNotFoundException($"Dalamud Hooks 폴더를 찾지 못했습니다: {hookDirectory}");

        CopyDirectory(pluginDirectory, outputDirectory);
        PatchDrawing(Path.Combine(outputDirectory, "Una.Drawing.dll"));
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

    private static void PatchDrawing(string drawingPath)
    {
        RequireFile(drawingPath);
        var temporaryPath = drawingPath + ".krpatch.tmp";
        try
        {
            using var assembly = AssemblyDefinition.ReadAssembly(drawingPath, new ReaderParameters { InMemory = true });
            var provider = AllTypes(assembly.MainModule.Types)
                .FirstOrDefault(type => type.FullName == "Una.Drawing.Clipping.ClipRectProvider")
                ?? throw Unsupported("Una.Drawing.Clipping.ClipRectProvider를 찾지 못했습니다.");
            var updateRects = provider.Methods.FirstOrDefault(method =>
                method.Name == "UpdateRects" && method.Parameters.Count == 0 && method.ReturnType.MetadataType == MetadataType.Void)
                ?? throw Unsupported("ClipRectProvider.UpdateRects()를 찾지 못했습니다.");

            if (HasKoreanFallback(updateRects))
                return;

            var rectList = provider.Fields.FirstOrDefault(field => field.Name == "RectList")
                ?? throw Unsupported("ClipRectProvider.RectList를 찾지 못했습니다.");
            var clear = updateRects.Body.Instructions
                .Select(instruction => instruction.Operand)
                .OfType<MethodReference>()
                .FirstOrDefault(method => method.Name == "Clear" && method.DeclaringType.FullName.StartsWith("System.Collections.Generic.List`1", StringComparison.Ordinal))
                ?? throw Unsupported("ClipRectProvider.RectList.Clear()를 찾지 못했습니다.");

            updateRects.Body.ExceptionHandlers.Clear();
            updateRects.Body.Variables.Clear();
            updateRects.Body.InitLocals = false;
            updateRects.Body.Instructions.Clear();
            var il = updateRects.Body.GetILProcessor();
            il.Append(il.Create(OpCodes.Ldsfld, rectList));
            il.Append(il.Create(OpCodes.Callvirt, assembly.MainModule.ImportReference(clear)));
            il.Append(il.Create(OpCodes.Ret));

            assembly.Write(temporaryPath);
            File.Copy(temporaryPath, drawingPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static void Verify(string pluginDirectory, string hookDirectory)
    {
        RequireFile(Path.Combine(pluginDirectory, "Umbra.dll"));
        var drawingPath = Path.Combine(pluginDirectory, "Una.Drawing.dll");
        RequireFile(drawingPath);

        using var assembly = AssemblyDefinition.ReadAssembly(drawingPath, new ReaderParameters { InMemory = true });
        var provider = AllTypes(assembly.MainModule.Types)
            .FirstOrDefault(type => type.FullName == "Una.Drawing.Clipping.ClipRectProvider")
            ?? throw Unsupported("Una.Drawing.Clipping.ClipRectProvider를 찾지 못했습니다.");
        var updateRects = provider.Methods.FirstOrDefault(method =>
            method.Name == "UpdateRects" && method.Parameters.Count == 0 && method.ReturnType.MetadataType == MetadataType.Void)
            ?? throw Unsupported("ClipRectProvider.UpdateRects()를 찾지 못했습니다.");

        if (!HasKoreanFallback(updateRects))
            throw new InvalidOperationException("Una.Drawing AtkResNode.IsVisible fallback이 적용되지 않았습니다.");
    }

    private static bool HasKoreanFallback(MethodDefinition method)
    {
        var instructions = method.Body?.Instructions;
        return instructions is { Count: 3 } &&
               instructions[0].OpCode == OpCodes.Ldsfld && instructions[0].Operand is FieldReference { Name: "RectList" } &&
               instructions[1].OpCode == OpCodes.Callvirt && instructions[1].Operand is MethodReference { Name: "Clear" } &&
               instructions[2].OpCode == OpCodes.Ret;
    }

    private static void CopyDirectory(string sourceDirectory, string outputDirectory)
    {
        if (Directory.Exists(outputDirectory))
            Directory.Delete(outputDirectory, true);

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
                yield return nested;
        }
    }

    private static void RequireFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("필수 파일을 찾지 못했습니다.", path);
    }

    private static InvalidOperationException Unsupported(string detail)
        => new($"지원하지 않는 Umbra/Una.Drawing 구조입니다. 해당 버전에 맞는 별도 검증이 필요합니다.\r\n\r\n{detail}");
}
