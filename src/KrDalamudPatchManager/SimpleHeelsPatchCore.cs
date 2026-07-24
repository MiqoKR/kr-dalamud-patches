using Mono.Cecil;

namespace KrDalamudPatchManager.Modules;

internal static class SimpleHeelsPatchCore
{
    private const string EffectContainerType = "FFXIVClientStructs.FFXIV.Client.Game.Character.EffectContainer";
    private const string SignatureAttribute = "Dalamud.Utility.Signatures.SignatureAttribute";
    private const string FloatHeightHookField = "calculateFloatHeightHook";
    private const int ExpectedFieldReferencesPerReplacement = 12;

    private static readonly IReadOnlyDictionary<string, string> FieldReplacements = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["MountGroundTiltAngle"] = "TiltParam1Value",
        ["MountGroundTiltSpeed"] = "TiltParam2Value",
    };

    public static void Patch(string pluginDirectory, string hookDirectory)
    {
        pluginDirectory = Path.GetFullPath(pluginDirectory);
        hookDirectory = Path.GetFullPath(hookDirectory);
        var dllPath = Path.Combine(pluginDirectory, "SimpleHeels.dll");
        RequireFile(dllPath);
        RequireFile(Path.Combine(hookDirectory, "Dalamud.dll"));
        RequireFile(Path.Combine(hookDirectory, "FFXIVClientStructs.dll"));

        var temporaryPath = dllPath + ".patched";
        try
        {
            using var assembly = AssemblyDefinition.ReadAssembly(dllPath, CreateReaderParameters(pluginDirectory, hookDirectory));
            var plugin = assembly.MainModule.GetType("SimpleHeels.Plugin")
                ?? throw new InvalidOperationException("SimpleHeels.Plugin type was not found.");
            DisableFloatHeightHook(plugin);
            ReplaceMountTiltFields(assembly.MainModule);
            assembly.Write(temporaryPath);
            File.Copy(temporaryPath, dllPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

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
        pluginDirectory = Path.GetFullPath(pluginDirectory);
        hookDirectory = Path.GetFullPath(hookDirectory);
        var dllPath = Path.Combine(pluginDirectory, "SimpleHeels.dll");
        RequireFile(dllPath);
        RequireFile(Path.Combine(hookDirectory, "Dalamud.dll"));
        RequireFile(Path.Combine(hookDirectory, "FFXIVClientStructs.dll"));

        using var assembly = AssemblyDefinition.ReadAssembly(dllPath, CreateReaderParameters(pluginDirectory, hookDirectory));
        var plugin = assembly.MainModule.GetType("SimpleHeels.Plugin")
            ?? throw new InvalidOperationException("SimpleHeels.Plugin type was not found.");
        var floatHeightHook = plugin.Fields.FirstOrDefault(field => field.Name == FloatHeightHookField)
            ?? throw new InvalidOperationException("SimpleHeels calculateFloatHeightHook field was not found.");
        if (floatHeightHook.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == SignatureAttribute))
        {
            throw new InvalidOperationException("SimpleHeels CalculateFloatHeight signature hook is still enabled.");
        }

        var expectedCounts = FieldReplacements.Keys.ToDictionary(field => field, _ => 0, StringComparer.Ordinal);
        foreach (var type in AllTypes(assembly.MainModule.Types))
        {
            foreach (var method in type.Methods.Where(method => method.HasBody))
            {
                foreach (var reference in method.Body.Instructions.Select(instruction => instruction.Operand).OfType<FieldReference>())
                {
                    if (reference.DeclaringType.FullName != EffectContainerType)
                    {
                        continue;
                    }

                    if (FieldReplacements.ContainsKey(reference.Name))
                    {
                        throw new InvalidOperationException($"SimpleHeels still references unsupported field {reference.Name}.");
                    }

                    foreach (var (source, replacement) in FieldReplacements)
                    {
                        if (reference.Name == replacement)
                        {
                            expectedCounts[source]++;
                        }
                    }
                }
            }
        }

        foreach (var (source, count) in expectedCounts)
        {
            if (count != ExpectedFieldReferencesPerReplacement)
            {
                throw new InvalidOperationException($"SimpleHeels {source} fallback count was {count}, expected {ExpectedFieldReferencesPerReplacement}.");
            }
        }
    }

    private static void DisableFloatHeightHook(TypeDefinition plugin)
    {
        var field = plugin.Fields.FirstOrDefault(field => field.Name == FloatHeightHookField)
            ?? throw new InvalidOperationException("SimpleHeels calculateFloatHeightHook field was not found.");
        var signature = field.CustomAttributes.FirstOrDefault(attribute => attribute.AttributeType.FullName == SignatureAttribute)
            ?? throw new InvalidOperationException("SimpleHeels CalculateFloatHeight signature attribute was not found.");
        field.CustomAttributes.Remove(signature);
    }

    private static void ReplaceMountTiltFields(ModuleDefinition module)
    {
        var replacementCounts = FieldReplacements.Keys.ToDictionary(field => field, _ => 0, StringComparer.Ordinal);
        foreach (var type in AllTypes(module.Types))
        {
            foreach (var method in type.Methods.Where(method => method.HasBody))
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is not FieldReference field ||
                        field.DeclaringType.FullName != EffectContainerType ||
                        !FieldReplacements.TryGetValue(field.Name, out var replacement))
                    {
                        continue;
                    }

                    instruction.Operand = module.ImportReference(new FieldReference(replacement, field.FieldType, field.DeclaringType));
                    replacementCounts[field.Name]++;
                }
            }
        }

        foreach (var (field, count) in replacementCounts)
        {
            if (count != ExpectedFieldReferencesPerReplacement)
            {
                throw new InvalidOperationException($"SimpleHeels {field} reference count was {count}, expected {ExpectedFieldReferencesPerReplacement}.");
            }
        }
    }

    private static ReaderParameters CreateReaderParameters(params string[] directories)
    {
        var resolver = new DefaultAssemblyResolver();
        foreach (var directory in directories)
        {
            resolver.AddSearchDirectory(directory);
        }

        return new ReaderParameters
        {
            AssemblyResolver = resolver,
            InMemory = true,
            ReadingMode = ReadingMode.Immediate,
        };
    }

    private static void RequireFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Required file was not found.", path);
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
}
