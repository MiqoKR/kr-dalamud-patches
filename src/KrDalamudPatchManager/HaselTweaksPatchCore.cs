using Mono.Cecil;
using Mono.Cecil.Cil;

namespace KrDalamudPatchManager.Modules;

internal static class HaselTweaksPatchCore
{
    private const string ClientStructsFileName = "FFXIVClientStructs.dll";
    private const string HaselCommonFileName = "HaselCommon.dll";
    private const string RaptureAtkModuleTypeName = "FFXIVClientStructs.FFXIV.Client.UI.RaptureAtkModule";
    private const string AddonObserverTypeName = "HaselCommon.Services.AddonObserver";

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

    public static void ValidatePatchShape(string pluginDirectory, string hookDirectory)
    {
        var validationDirectory = Path.Combine(
            Path.GetTempPath(),
            "KR-Dalamud-PatchManager",
            "haseltweaks-shape-validation",
            Guid.NewGuid().ToString("N"));
        try
        {
            Patch(pluginDirectory, hookDirectory, validationDirectory);
        }
        finally
        {
            TryDeleteDirectory(validationDirectory);
        }
    }

    private static void PatchClientStructs(string originalPath, string compatibleHookPath, string outputPath)
    {
        using var original = AssemblyDefinition.ReadAssembly(originalPath, new ReaderParameters { InMemory = true });
        using var compatible = AssemblyDefinition.ReadAssembly(compatibleHookPath, new ReaderParameters { InMemory = true });
        _ = GetRaptureAtkUnitManagerOffset(compatible);

        // HaselTweaks references its bundled ClientStructs assembly identity.
        // Preserve that identity while using the already validated KR Hook layout.
        compatible.Name.Version = original.Name.Version;
        compatible.Write(outputPath);
    }

    private static void PatchAddonObserver(string sourcePath, string outputPath)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(sourcePath, new ReaderParameters { InMemory = true });
        var observer = assembly.MainModule.Types.SingleOrDefault(type => type.FullName == AddonObserverTypeName)
            ?? throw Unsupported("HaselCommon.Services.AddonObserver type was not found.");
        var update = observer.Methods.SingleOrDefault(method => method.Name == "OnFrameworkUpdate" && method.HasBody);
        if (update is null)
        {
            if (!HasModernVisibilityDetour(observer))
            {
                throw Unsupported("AddonObserver did not contain the legacy update loop or the modern visibility detour.");
            }

            assembly.Write(outputPath);
            return;
        }

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
        using (var hookStructs = AssemblyDefinition.ReadAssembly(
                   Path.Combine(hookDirectory, ClientStructsFileName),
                   new ReaderParameters { InMemory = true }))
        {
            var actualOffset = GetRaptureAtkUnitManagerOffset(structs);
            var expectedOffset = GetRaptureAtkUnitManagerOffset(hookStructs);
            if (actualOffset != expectedOffset)
            {
                throw new InvalidOperationException(
                    $"Expected current KR Hook RaptureAtkUnitManager offset 0x{expectedOffset:X}, actual 0x{actualOffset:X}.");
            }
        }

        using var common = AssemblyDefinition.ReadAssembly(commonPath, new ReaderParameters { InMemory = true });
        var observer = common.MainModule.Types.SingleOrDefault(type => type.FullName == AddonObserverTypeName)
            ?? throw Unsupported("Patched AddonObserver type was not found.");
        var update = observer.Methods.SingleOrDefault(method => method.Name == "OnFrameworkUpdate" && method.HasBody);
        if (update is null)
        {
            if (!HasModernVisibilityDetour(observer))
            {
                throw new InvalidOperationException("HaselTweaks modern AddonObserver visibility detour was not verified.");
            }

            return;
        }

        var getValueCount = update.Body.Instructions.Count(instruction =>
            IsAtkUnitPointerValueGetter(instruction.Operand as MethodReference));
        if (getValueCount != 2)
        {
            throw new InvalidOperationException($"HaselTweaks AddonObserver pointer cache patch was not verified (get_Value={getValueCount}).");
        }
    }

    private static int GetRaptureAtkUnitManagerOffset(AssemblyDefinition assembly)
    {
        var module = assembly.MainModule.Types.SingleOrDefault(type => type.FullName == RaptureAtkModuleTypeName)
            ?? throw Unsupported("FFXIVClientStructs RaptureAtkModule type was not found.");
        var manager = module.Fields.SingleOrDefault(field => field.Name == "RaptureAtkUnitManager")
            ?? throw Unsupported("RaptureAtkUnitManager field was not found.");
        if (manager.Offset <= 0)
        {
            throw Unsupported($"RaptureAtkUnitManager offset was invalid: 0x{manager.Offset:X}.");
        }

        return manager.Offset;
    }

    private static bool HasModernVisibilityDetour(TypeDefinition observer)
    {
        var detour = observer.Methods.SingleOrDefault(method =>
            method.Name == "UpdateAppliedVisibilityStateDetour" &&
            method.HasBody &&
            method.ReturnType.FullName == "System.Boolean" &&
            method.Parameters.Count == 1 &&
            method.Parameters[0].ParameterType.FullName ==
                "FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*");
        return detour is not null && !detour.Body.Instructions.Any(instruction =>
            IsAtkUnitPointerValueGetter(instruction.Operand as MethodReference));
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

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Validation data is disposable and never contains user files.
        }
    }

    private static InvalidOperationException Unsupported(string detail)
        => new($"Unsupported HaselTweaks/HaselCommon structure. A separate verification is required for this version.\r\n\r\n{detail}");
}
