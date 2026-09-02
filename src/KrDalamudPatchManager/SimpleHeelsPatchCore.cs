using Mono.Cecil;

namespace KrDalamudPatchManager.Modules;

internal static class SimpleHeelsPatchCore
{
    private const string EffectContainerType = "FFXIVClientStructs.FFXIV.Client.Game.Character.EffectContainer";
    private const string CharacterType = "FFXIVClientStructs.FFXIV.Client.Game.Character.Character";
    private const string TransformationContainerType = "FFXIVClientStructs.FFXIV.Client.Game.Character.TransformationContainer";
    private const string ReaperShroudContainerType = "FFXIVClientStructs.FFXIV.Client.Game.Character.ReaperShroudContainer";
    private const string SignatureAttribute = "Dalamud.Utility.Signatures.SignatureAttribute";
    private const string FloatHeightHookField = "calculateFloatHeightHook";
    private const int ExpectedFieldReferencesPerReplacement = 12;

    private static readonly IReadOnlyDictionary<string, string> FieldReplacements = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["MountGroundTiltAngle"] = "TiltParam1Value",
        ["MountGroundTiltSpeed"] = "TiltParam2Value",
    };

    private static readonly (string SourceType, string SourceField, string TargetType, string TargetField, int ExpectedCount)[]
        ClientStructFieldAliases =
        [
            (CharacterType, "Transformation", CharacterType, "ReaperShroud", 2),
            (TransformationContainerType, "Flags", ReaperShroudContainerType, "Flags", 1),
        ];

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
            using var clientStructs = AssemblyDefinition.ReadAssembly(
                Path.Combine(hookDirectory, "FFXIVClientStructs.dll"),
                CreateReaderParameters(hookDirectory));
            var plugin = assembly.MainModule.GetType("SimpleHeels.Plugin")
                ?? throw new InvalidOperationException("SimpleHeels.Plugin type was not found.");
            DisableFloatHeightHook(plugin);
            ReplaceMountTiltFields(assembly.MainModule);
            ReplaceClientStructFieldAliases(assembly.MainModule, clientStructs.MainModule);
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

    public static bool NeedsClientStructAliasUpgrade(string pluginDirectory, string hookDirectory)
    {
        try
        {
            pluginDirectory = Path.GetFullPath(pluginDirectory);
            hookDirectory = Path.GetFullPath(hookDirectory);
            using var assembly = AssemblyDefinition.ReadAssembly(
                Path.Combine(pluginDirectory, "SimpleHeels.dll"),
                CreateReaderParameters(pluginDirectory, hookDirectory));
            var plugin = assembly.MainModule.GetType("SimpleHeels.Plugin")
                ?? throw new InvalidOperationException("SimpleHeels.Plugin type was not found.");
            var floatHeightHook = plugin.Fields.FirstOrDefault(field => field.Name == FloatHeightHookField)
                ?? throw new InvalidOperationException("SimpleHeels calculateFloatHeightHook field was not found.");
            if (floatHeightHook.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == SignatureAttribute))
                return false;

            var references = AllTypes(assembly.MainModule.Types)
                .SelectMany(type => type.Methods.Where(method => method.HasBody))
                .SelectMany(method => method.Body.Instructions.Select(instruction => instruction.Operand).OfType<FieldReference>())
                .ToArray();
            foreach (var (source, target) in FieldReplacements)
            {
                if (references.Count(reference => reference.DeclaringType.FullName == EffectContainerType && reference.Name == source) != 0 ||
                    references.Count(reference => reference.DeclaringType.FullName == EffectContainerType && reference.Name == target) != ExpectedFieldReferencesPerReplacement)
                    return false;
            }

            return ClientStructFieldAliases.All(alias =>
                references.Count(reference => reference.DeclaringType.FullName == alias.SourceType && reference.Name == alias.SourceField) == alias.ExpectedCount &&
                references.Count(reference => reference.DeclaringType.FullName == alias.TargetType && reference.Name == alias.TargetField) == 0);
        }
        catch
        {
            return false;
        }
    }

    public static void UpgradeClientStructAliases(string pluginDirectory, string hookDirectory)
        => Patch(pluginDirectory, hookDirectory);

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

        VerifyClientStructFieldReferences(assembly.MainModule);
    }

    public static void ValidatePatchShape(string pluginDirectory, string hookDirectory)
    {
        var validationDirectory = Path.Combine(
            Path.GetTempPath(),
            "KR-Dalamud-PatchManager",
            "simpleheels-shape-validation",
            Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(pluginDirectory, validationDirectory);
            Patch(validationDirectory, hookDirectory);
            Verify(validationDirectory, hookDirectory);
        }
        finally
        {
            TryDeleteDirectory(validationDirectory);
        }
    }

    private static void DisableFloatHeightHook(TypeDefinition plugin)
    {
        var field = plugin.Fields.FirstOrDefault(field => field.Name == FloatHeightHookField)
            ?? throw new InvalidOperationException("SimpleHeels calculateFloatHeightHook field was not found.");
        var signature = field.CustomAttributes.FirstOrDefault(attribute => attribute.AttributeType.FullName == SignatureAttribute);
        if (signature != null)
            field.CustomAttributes.Remove(signature);
    }

    private static void ReplaceMountTiltFields(ModuleDefinition module)
    {
        var finalCounts = FieldReplacements.Keys.ToDictionary(field => field, _ => 0, StringComparer.Ordinal);
        foreach (var type in AllTypes(module.Types))
        {
            foreach (var method in type.Methods.Where(method => method.HasBody))
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is not FieldReference field || field.DeclaringType.FullName != EffectContainerType)
                        continue;

                    if (FieldReplacements.TryGetValue(field.Name, out var replacement))
                    {
                        instruction.Operand = module.ImportReference(new FieldReference(replacement, field.FieldType, field.DeclaringType));
                        finalCounts[field.Name]++;
                        continue;
                    }

                    foreach (var (source, target) in FieldReplacements)
                    {
                        if (field.Name == target)
                            finalCounts[source]++;
                    }
                }
            }
        }

        foreach (var (field, count) in finalCounts)
        {
            if (count != ExpectedFieldReferencesPerReplacement)
            {
                throw new InvalidOperationException($"SimpleHeels {field} reference count was {count}, expected {ExpectedFieldReferencesPerReplacement}.");
            }
        }
    }

    private static void ReplaceClientStructFieldAliases(ModuleDefinition module, ModuleDefinition clientStructs)
    {
        var finalCounts = ClientStructFieldAliases.ToDictionary(
            alias => (alias.SourceType, alias.SourceField),
            _ => 0);
        var targetTypes = AllTypes(clientStructs.Types).ToDictionary(type => type.FullName, StringComparer.Ordinal);

        foreach (var type in AllTypes(module.Types))
        {
            foreach (var method in type.Methods.Where(method => method.HasBody))
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is not FieldReference field)
                        continue;

                    foreach (var alias in ClientStructFieldAliases)
                    {
                        if (field.DeclaringType.FullName == alias.TargetType && field.Name == alias.TargetField)
                        {
                            finalCounts[(alias.SourceType, alias.SourceField)]++;
                            break;
                        }

                        if (field.DeclaringType.FullName != alias.SourceType || field.Name != alias.SourceField)
                            continue;

                        if (!targetTypes.TryGetValue(alias.TargetType, out var targetType))
                            throw new InvalidOperationException($"KR ClientStructs type {alias.TargetType} was not found.");
                        var targetField = targetType.Fields.FirstOrDefault(candidate => candidate.Name == alias.TargetField)
                            ?? throw new InvalidOperationException($"KR ClientStructs field {alias.TargetType}::{alias.TargetField} was not found.");
                        instruction.Operand = module.ImportReference(targetField);
                        finalCounts[(alias.SourceType, alias.SourceField)]++;
                        break;
                    }
                }
            }
        }

        foreach (var alias in ClientStructFieldAliases)
        {
            var count = finalCounts[(alias.SourceType, alias.SourceField)];
            if (count != alias.ExpectedCount)
            {
                throw new InvalidOperationException(
                    $"SimpleHeels {alias.SourceType}::{alias.SourceField} reference count was {count}, expected {alias.ExpectedCount}.");
            }
        }
    }

    private static void VerifyClientStructFieldReferences(ModuleDefinition module)
    {
        var unresolved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in AllTypes(module.Types))
        {
            foreach (var method in type.Methods.Where(method => method.HasBody))
            {
                foreach (var reference in method.Body.Instructions
                    .Select(instruction => instruction.Operand)
                    .OfType<FieldReference>()
                    .Where(reference => reference.DeclaringType.FullName.StartsWith("FFXIVClientStructs.", StringComparison.Ordinal)))
                {
                    try
                    {
                        if (reference.Resolve() == null)
                            unresolved.Add(reference.FullName);
                    }
                    catch
                    {
                        unresolved.Add(reference.FullName);
                    }
                }
            }
        }

        if (unresolved.Count != 0)
        {
            throw new InvalidOperationException(
                "SimpleHeels contains unresolved KR ClientStructs field references: " + string.Join(", ", unresolved.OrderBy(name => name, StringComparer.Ordinal)));
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

    private static void CopyDirectory(string sourceDirectory, string outputDirectory)
    {
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
                File.Copy(sourcePath, outputPath, overwrite: true);
            }
        }
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
}
