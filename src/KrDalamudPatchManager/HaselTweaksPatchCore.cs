using Mono.Cecil;
using Mono.Cecil.Cil;

namespace KrDalamudPatchManager.Modules;

internal static class HaselTweaksPatchCore
{
    private const string ClientStructsFileName = "FFXIVClientStructs.dll";
    private const string HaselCommonFileName = "HaselCommon.dll";
    private const string RaptureAtkModuleTypeName = "FFXIVClientStructs.FFXIV.Client.UI.RaptureAtkModule";
    private const string AddonObserverTypeName = "HaselCommon.Services.AddonObserver";

    // The Korean 7.55 UI layout places the embedded manager 0x10 bytes earlier.
    private const int KoreanRaptureAtkUnitManagerOffset = 0x13420;

    public static void Patch(string pluginDirectory, string hookDirectory, string outputDirectory)
    {
        RequireFile(Path.Combine(pluginDirectory, ClientStructsFileName));
        RequireFile(Path.Combine(pluginDirectory, HaselCommonFileName));
        RequireFile(Path.Combine(hookDirectory, ClientStructsFileName));

        Directory.CreateDirectory(outputDirectory);
        PatchClientStructs(
            Path.Combine(pluginDirectory, ClientStructsFileName),
            Path.Combine(hookDirectory, ClientStructsFileName),
            Path.Combine(outputDirectory, ClientStructsFileName));
        PatchAddonObserver(
            Path.Combine(pluginDirectory, HaselCommonFileName),
            Path.Combine(outputDirectory, HaselCommonFileName));
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

    private static void PatchClientStructs(string originalPath, string compatibleHookPath, string outputPath)
    {
        using var original = AssemblyDefinition.ReadAssembly(originalPath, new ReaderParameters { InMemory = true });
        using var compatible = AssemblyDefinition.ReadAssembly(compatibleHookPath, new ReaderParameters { InMemory = true });
        RequireKoreanUiLayout(compatible);

        // HaselTweaks 49.2.1 references its bundled 7.51.0.0 assembly identity.
        // Preserve that identity while using the already validated KR Hook layout.
        compatible.Name.Version = original.Name.Version;
        compatible.Write(outputPath);
    }

    private static void PatchAddonObserver(string sourcePath, string outputPath)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(sourcePath, new ReaderParameters { InMemory = true });
        var observer = assembly.MainModule.Types.SingleOrDefault(type => type.FullName == AddonObserverTypeName)
            ?? throw Unsupported("HaselCommon.Services.AddonObserver type was not found.");
        var update = observer.Methods.SingleOrDefault(method => method.Name == "OnFrameworkUpdate" && method.HasBody)
            ?? throw Unsupported("AddonObserver.OnFrameworkUpdate method was not found.");

        var getValueCalls = update.Body.Instructions
            .Where(instruction => IsAtkUnitPointerValueGetter(instruction.Operand as MethodReference))
            .ToArray();
        if (getValueCalls.Length == 2)
        {
            assembly.Write(outputPath);
            return;
        }

        if (getValueCalls.Length != 4)
        {
            throw Unsupported($"AddonObserver pointer read count was {getValueCalls.Length}, expected 4.");
        }

        var valueGetter = (MethodReference)getValueCalls[0].Operand;
        var pointerType = valueGetter.DeclaringType as GenericInstanceType
            ?? throw Unsupported("AddonObserver pointer getter was not a generic AtkUnitBase pointer.");
        var unitPointer = new VariableDefinition(
            new PointerType(assembly.MainModule.ImportReference(pointerType.GenericArguments.Single())));
        update.Body.Variables.Add(unitPointer);
        update.Body.InitLocals = true;
        var il = update.Body.GetILProcessor();

        // Cache the first list-entry pointer. The original code reads the same
        // unmanaged entry three times, which can become null while the UI list is
        // updated between instructions.
        il.InsertAfter(getValueCalls[0], il.Create(OpCodes.Stloc, unitPointer));
        il.InsertAfter(getValueCalls[0].Next!, il.Create(OpCodes.Ldloc, unitPointer));

        foreach (var getValue in getValueCalls.Skip(1).Take(2))
        {
            var loadAddress = getValue.Previous
                ?? throw Unsupported("AddonObserver pointer load instruction was not found.");
            loadAddress.OpCode = OpCodes.Ldloc;
            loadAddress.Operand = unitPointer;
            il.Remove(getValue);
        }

        assembly.Write(outputPath);
    }

    private static void Verify(string pluginDirectory, string hookDirectory)
    {
        var structsPath = Path.Combine(pluginDirectory, ClientStructsFileName);
        var commonPath = Path.Combine(pluginDirectory, HaselCommonFileName);
        RequireFile(structsPath);
        RequireFile(commonPath);

        using (var structs = AssemblyDefinition.ReadAssembly(structsPath, new ReaderParameters { InMemory = true }))
        {
            RequireKoreanUiLayout(structs);
            if (structs.Name.Version != new Version(7, 51, 0, 0))
            {
                throw new InvalidOperationException($"HaselTweaks FFXIVClientStructs assembly identity changed: {structs.Name.Version}.");
            }
        }

        using var common = AssemblyDefinition.ReadAssembly(commonPath, new ReaderParameters { InMemory = true });
        var observer = common.MainModule.Types.SingleOrDefault(type => type.FullName == AddonObserverTypeName)
            ?? throw Unsupported("Patched AddonObserver type was not found.");
        var update = observer.Methods.SingleOrDefault(method => method.Name == "OnFrameworkUpdate" && method.HasBody)
            ?? throw Unsupported("Patched AddonObserver.OnFrameworkUpdate method was not found.");
        var getValueCount = update.Body.Instructions.Count(instruction =>
            IsAtkUnitPointerValueGetter(instruction.Operand as MethodReference));
        if (getValueCount != 2)
        {
            throw new InvalidOperationException($"HaselTweaks AddonObserver pointer cache patch was not verified (get_Value={getValueCount}).");
        }
    }

    private static void RequireKoreanUiLayout(AssemblyDefinition assembly)
    {
        var module = assembly.MainModule.Types.SingleOrDefault(type => type.FullName == RaptureAtkModuleTypeName)
            ?? throw Unsupported("FFXIVClientStructs RaptureAtkModule type was not found.");
        var manager = module.Fields.SingleOrDefault(field => field.Name == "RaptureAtkUnitManager")
            ?? throw Unsupported("RaptureAtkUnitManager field was not found.");
        if (manager.Offset != KoreanRaptureAtkUnitManagerOffset)
        {
            throw new InvalidOperationException(
                $"Expected KR RaptureAtkUnitManager offset 0x{KoreanRaptureAtkUnitManagerOffset:X}, actual 0x{manager.Offset:X}.");
        }
    }

    private static bool IsAtkUnitPointerValueGetter(MethodReference? reference)
        => reference is { Name: "get_Value" } &&
           reference.DeclaringType.FullName.StartsWith(
               "FFXIVClientStructs.Interop.Pointer`1<FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase>",
               StringComparison.Ordinal);

    private static void RequireFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Required HaselTweaks file was not found.", path);
    }

    private static InvalidOperationException Unsupported(string detail)
        => new($"Unsupported HaselTweaks/HaselCommon structure. A separate verification is required for this version.\r\n\r\n{detail}");
}
