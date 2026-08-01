using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace KrDalamudPatchManager.Modules;

internal static class CommonUiAddressPatchCore
{
    private const string CacheRelativePath = "cachedSigs\\cs.json";
    private const string ClientStructsFileName = "FFXIVClientStructs.dll";
    private const string MarkerFileName = "KR.Dalamud.PatchManager.common-ui.json";
    private const string BackupFolderName = "DalamudCommonUi";
    private const string AtkResNodeTypeName = "FFXIVClientStructs.FFXIV.Component.GUI.AtkResNode";
    private static readonly byte?[] KoreanIsVisibleCallerPattern =
    {
        0xE8, null, null, null, null, 0x3C, 0x01, 0x75, 0x02,
    };
    private static readonly byte?[] KoreanIsVisibleTargetPattern =
    {
        0x48, 0x85, 0xC9, 0x74, null,
        0xF7, 0x81, 0xAC, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x74, null,
        0xF7, 0x81, 0xB0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x75, null,
        0xB8, 0x01, 0x00, 0x00, 0x00, 0xC3, 0x32, 0xC0, 0xC3,
    };

    public static PatchModule.ModuleStatus GetStatus(string profileRoot)
    {
        try
        {
            var context = Discover(profileRoot);
            var marker = ReadMarker(context.MarkerPath);
            var cachedRva = ReadCachedRva(context.CachePath, context.CacheKey);
            if (marker is not null &&
                marker.CacheKey == context.CacheKey &&
                cachedRva == marker.ResolvedRva &&
                string.Equals(marker.GameExecutablePath, context.GameExecutablePath, StringComparison.OrdinalIgnoreCase) &&
                marker.GameExecutableLength == new FileInfo(context.GameExecutablePath).Length &&
                marker.GameExecutableLastWriteUtcTicks == File.GetLastWriteTimeUtc(context.GameExecutablePath).Ticks)
            {
                return new PatchModule.ModuleStatus(
                    context.DisplayVersion,
                    marker.BackupPath is null ? "적용됨" : "적용됨 · 원본 복원 가능",
                    true,
                    false,
                    false,
                    marker.BackupPath is not null);
            }

            var message = marker is null
                ? "적용 가능 · 한섭 게임 파일에서 주소 자동 계산"
                : "게임 또는 Dalamud 파일 변경 감지 · 재적용 필요";
            return new PatchModule.ModuleStatus(context.DisplayVersion, message, false, false, true, marker?.BackupPath is not null);
        }
        catch (Exception ex)
        {
            return new PatchModule.ModuleStatus(null, ex.Message, false, true, false, false);
        }
    }

    public static string Apply(string profileRoot)
    {
        var context = Discover(profileRoot);
        var resolution = ResolveIsVisible(context.GameExecutablePath);
        var currentRva = ReadCachedRva(context.CachePath, context.CacheKey);
        var existingMarker = ReadMarker(context.MarkerPath);
        if (currentRva == resolution.TargetRva &&
            existingMarker is not null &&
            existingMarker.CacheKey == context.CacheKey)
        {
            WriteMarker(context, existingMarker.BackupPath, resolution);
            return $"Dalamud 공통 UI/AtkResNode: 이미 적용되어 있습니다. (RVA 0x{resolution.TargetRva:X})";
        }

        string? backupPath = existingMarker?.BackupPath;
        if (currentRva != resolution.TargetRva)
        {
            backupPath = CreateBackup(context);
            WriteCachedRva(context.CachePath, context.CacheKey, resolution.TargetRva);
        }

        VerifyCachedRva(context.CachePath, context.CacheKey, resolution.TargetRva);
        WriteMarker(context, backupPath, resolution);
        return $"Dalamud 공통 UI/AtkResNode: 적용 완료 (RVA 0x{resolution.TargetRva:X}, 교차검증 {resolution.CallerCount}개)";
    }

    public static string Restore(string profileRoot)
    {
        var context = Discover(profileRoot);
        var marker = ReadMarker(context.MarkerPath)
            ?? throw new InvalidOperationException("공통 UI 패치 마커를 찾지 못했습니다.");
        if (string.IsNullOrWhiteSpace(marker.BackupPath))
        {
            throw new InvalidOperationException("이 매니저가 만든 원본 백업이 없어 복원할 수 없습니다.");
        }

        var backupPath = Path.GetFullPath(marker.BackupPath);
        var permittedRoot = Path.GetFullPath(Path.Combine(context.ProfileRoot, "kr-patch-backups", BackupFolderName));
        if (!backupPath.StartsWith(permittedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("허용되지 않은 공통 UI 백업 경로입니다.");
        }

        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("공통 UI 주소 캐시 백업을 찾지 못했습니다.", backupPath);
        }

        File.Copy(backupPath, context.CachePath, true);
        File.Delete(context.MarkerPath);
        return $"Dalamud 공통 UI/AtkResNode: 원본 복원 완료 (백업: {backupPath})";
    }

    private static PatchContext Discover(string profileRoot)
    {
        var root = Path.GetFullPath(profileRoot);
        var hook = PatchModule.FindHookDirectory(root);
        var clientStructsPath = Path.Combine(hook, ClientStructsFileName);
        var cachePath = Path.Combine(hook, CacheRelativePath);
        if (!File.Exists(clientStructsPath))
        {
            throw new FileNotFoundException("FFXIVClientStructs.dll을 찾지 못했습니다.", clientStructsPath);
        }

        if (!File.Exists(cachePath))
        {
            throw new FileNotFoundException("Dalamud 주소 캐시가 없습니다. 게임에 Dalamud를 한 번 적용한 뒤 다시 실행하세요.", cachePath);
        }

        var cacheKey = ReadIsVisibleCacheKey(clientStructsPath);
        var gameExecutablePath = FindGameExecutable();
        var cacheVersion = ReadCacheRoot(cachePath)["Version"]?.GetValue<string>() ?? "버전 확인 불가";
        var hookGameVersion = ReadHookGameVersion(hook);
        if (!string.Equals(cacheVersion, hookGameVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Dalamud 주소 캐시 버전이 현재 Hook과 다릅니다. Hook: {hookGameVersion}, 캐시: {cacheVersion}");
        }

        var executableGameVersion = ReadExecutableGameVersion(gameExecutablePath);
        if (!string.Equals(cacheVersion, executableGameVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"한섭 게임 실행 파일 버전이 Dalamud 주소 캐시와 다릅니다. 게임: {executableGameVersion}, 캐시: {cacheVersion}");
        }
        return new PatchContext(
            root,
            hook,
            cachePath,
            cacheKey,
            gameExecutablePath,
            Path.Combine(hook, MarkerFileName),
            $"{Path.GetFileName(hook)} / {cacheVersion}");
    }

    private static string FindGameExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("KR_FFXIV_GAME_EXE");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var configuredPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured));
            return File.Exists(configuredPath)
                ? configuredPath
                : throw new FileNotFoundException("KR_FFXIV_GAME_EXE에 지정된 실행 파일을 찾지 못했습니다.", configuredPath);
        }

        var defaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "FINAL FANTASY XIV - KOREA", "game", "ffxiv_dx11.exe");

        return File.Exists(defaultPath)
            ? defaultPath
            : throw new FileNotFoundException(
                "한섭 ffxiv_dx11.exe를 찾지 못했습니다. 사용자 지정 설치는 KR_FFXIV_GAME_EXE 환경 변수에 전체 경로를 지정하세요.");
    }

    private static string ReadIsVisibleCacheKey(string clientStructsPath)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(clientStructsPath, new ReaderParameters { InMemory = true });
        var atkResNode = assembly.MainModule.Types.SingleOrDefault(type => type.FullName == AtkResNodeTypeName)
            ?? throw new InvalidDataException("FFXIVClientStructs에서 AtkResNode를 찾지 못했습니다.");
        var addresses = atkResNode.NestedTypes.SingleOrDefault(type => type.Name == "Addresses")
            ?? throw new InvalidDataException("FFXIVClientStructs에서 AtkResNode.Addresses를 찾지 못했습니다.");
        var constructor = addresses.Methods.SingleOrDefault(method => method.Name == ".cctor" && method.HasBody)
            ?? throw new InvalidDataException("AtkResNode.Addresses 초기화 코드를 찾지 못했습니다.");
        var signatures = constructor.Body.Instructions
            .Where(instruction => instruction.OpCode == OpCodes.Ldstr)
            .Select(instruction => instruction.Operand as string)
            .Where(signature => signature?.StartsWith("E8 ?? ?? ?? ?? 3C 01 75 ", StringComparison.Ordinal) == true)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (signatures.Length != 1)
        {
            throw new InvalidDataException($"AtkResNode.IsVisible 주소 정의를 하나로 확정하지 못했습니다. 후보: {signatures.Length}개");
        }

        return signatures[0] + "+relfollow[1]";
    }

    private static AddressResolution ResolveIsVisible(string executablePath)
    {
        var bytes = File.ReadAllBytes(executablePath);
        using var stream = new MemoryStream(bytes, writable: false);
        using var peReader = new PEReader(stream);
        var headers = peReader.PEHeaders;
        if (headers.PEHeader is null)
        {
            throw new InvalidDataException("한섭 게임 실행 파일의 PE 헤더를 읽지 못했습니다.");
        }

        var targets = new List<int>();
        for (var offset = 0; offset <= bytes.Length - KoreanIsVisibleCallerPattern.Length; offset++)
        {
            if (!Matches(bytes, offset, KoreanIsVisibleCallerPattern))
            {
                continue;
            }

            var callerRva = FileOffsetToRva(headers.SectionHeaders, offset);
            var displacement = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 1, 4));
            var targetRva = checked(callerRva + 5 + displacement);
            if (IsExecutableRva(headers.SectionHeaders, targetRva))
            {
                targets.Add(targetRva);
            }
        }

        var groups = targets.GroupBy(target => target)
            .Select(group => new { Target = group.Key, Count = group.Count() })
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.Target)
            .ToArray();
        if (groups.Length == 0 || groups[0].Count < 2)
        {
            throw new InvalidDataException("AtkResNode.IsVisible 주소를 교차검증하지 못했습니다. 일치 호출이 2개 이상 필요합니다.");
        }

        if (groups.Length > 1 && groups[1].Count == groups[0].Count)
        {
            throw new InvalidDataException("AtkResNode.IsVisible 주소 후보가 둘 이상이라 안전하게 적용할 수 없습니다.");
        }

        var targetOffset = RvaToFileOffset(headers.SectionHeaders, groups[0].Target);
        if (!Matches(bytes, targetOffset, KoreanIsVisibleTargetPattern))
        {
            throw new InvalidDataException("AtkResNode.IsVisible 대상 함수 본문이 검증된 한섭 7.55 구조와 다릅니다.");
        }

        return new AddressResolution(groups[0].Target, groups[0].Count, ComputeSha256(executablePath));
    }

    private static bool Matches(byte[] bytes, int offset, IReadOnlyList<byte?> pattern)
    {
        for (var index = 0; index < pattern.Count; index++)
        {
            if (pattern[index] is byte expected && bytes[offset + index] != expected)
            {
                return false;
            }
        }

        return true;
    }

    private static int FileOffsetToRva(IEnumerable<SectionHeader> sections, int fileOffset)
    {
        foreach (var section in sections)
        {
            if (fileOffset >= section.PointerToRawData && fileOffset < section.PointerToRawData + section.SizeOfRawData)
            {
                return checked(section.VirtualAddress + fileOffset - section.PointerToRawData);
            }
        }

        throw new InvalidDataException($"실행 파일 오프셋을 RVA로 변환하지 못했습니다: 0x{fileOffset:X}");
    }

    private static int RvaToFileOffset(IEnumerable<SectionHeader> sections, int rva)
    {
        foreach (var section in sections)
        {
            if (rva >= section.VirtualAddress && rva < section.VirtualAddress + Math.Max(section.VirtualSize, section.SizeOfRawData))
            {
                return checked(section.PointerToRawData + rva - section.VirtualAddress);
            }
        }

        throw new InvalidDataException($"실행 파일 RVA를 오프셋으로 변환하지 못했습니다: 0x{rva:X}");
    }

    private static bool IsExecutableRva(IEnumerable<SectionHeader> sections, int rva)
        => sections.Any(section =>
            section.Name == ".text" &&
            rva >= section.VirtualAddress &&
            rva < section.VirtualAddress + Math.Max(section.VirtualSize, section.SizeOfRawData));

    private static JsonObject ReadCacheRoot(string cachePath)
        => JsonNode.Parse(File.ReadAllText(cachePath)) as JsonObject
           ?? throw new InvalidDataException("Dalamud 주소 캐시 JSON 형식이 잘못되었습니다.");

    private static string ReadHookGameVersion(string hookDirectory)
    {
        var path = Path.Combine(hookDirectory, "version.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Dalamud version.json을 찾지 못했습니다.", path);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.EnumerateObject()
                   .FirstOrDefault(property => property.Name.Equals("supportedGameVer", StringComparison.OrdinalIgnoreCase))
                   .Value.GetString()
               ?? throw new InvalidDataException("Dalamud 지원 게임 버전을 확인하지 못했습니다.");
    }

    private static string ReadExecutableGameVersion(string executablePath)
    {
        var path = Path.Combine(Path.GetDirectoryName(executablePath)!, "ffxivgame.ver");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("한섭 게임 버전 파일을 찾지 못했습니다.", path);
        }

        var version = File.ReadAllText(path).Trim();
        return string.IsNullOrWhiteSpace(version)
            ? throw new InvalidDataException("한섭 게임 버전 파일이 비어 있습니다.")
            : version;
    }

    private static long? ReadCachedRva(string cachePath, string cacheKey)
    {
        var cache = ReadCacheRoot(cachePath)["Cache"] as JsonObject
            ?? throw new InvalidDataException("Dalamud 주소 캐시에 Cache 객체가 없습니다.");
        return cache[cacheKey] is JsonValue value && value.TryGetValue<long>(out var rva) ? rva : null;
    }

    private static void WriteCachedRva(string cachePath, string cacheKey, long resolvedRva)
    {
        var root = ReadCacheRoot(cachePath);
        var cache = root["Cache"] as JsonObject
            ?? throw new InvalidDataException("Dalamud 주소 캐시에 Cache 객체가 없습니다.");
        cache[cacheKey] = resolvedRva;

        var temporaryPath = cachePath + ".kr-common-ui.tmp";
        try
        {
            File.WriteAllText(temporaryPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, cachePath, true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static void VerifyCachedRva(string cachePath, string cacheKey, long expectedRva)
    {
        if (ReadCachedRva(cachePath, cacheKey) != expectedRva)
        {
            throw new InvalidDataException("공통 UI 주소 캐시 적용 후 검증에 실패했습니다.");
        }
    }

    private static string CreateBackup(PatchContext context)
    {
        var directory = Path.Combine(
            context.ProfileRoot,
            "kr-patch-backups",
            BackupFolderName,
            Path.GetFileName(context.HookDirectory),
            DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(directory);
        var backupPath = Path.Combine(directory, "cs.json");
        File.Copy(context.CachePath, backupPath);
        return backupPath;
    }

    private static CommonUiMarker? ReadMarker(string markerPath)
    {
        if (!File.Exists(markerPath))
        {
            return null;
        }

        return JsonSerializer.Deserialize<CommonUiMarker>(File.ReadAllText(markerPath));
    }

    private static void WriteMarker(PatchContext context, string? backupPath, AddressResolution resolution)
    {
        var gameFile = new FileInfo(context.GameExecutablePath);
        var marker = new CommonUiMarker
        {
            PatchManagerVersion = "0.2.21",
            PatchedAt = DateTimeOffset.Now,
            CacheKey = context.CacheKey,
            ResolvedRva = resolution.TargetRva,
            CallerCount = resolution.CallerCount,
            GameExecutablePath = context.GameExecutablePath,
            GameExecutableSha256 = resolution.ExecutableSha256,
            GameExecutableLength = gameFile.Length,
            GameExecutableLastWriteUtcTicks = gameFile.LastWriteTimeUtc.Ticks,
            BackupPath = backupPath,
        };
        File.WriteAllText(context.MarkerPath, JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed record PatchContext(
        string ProfileRoot,
        string HookDirectory,
        string CachePath,
        string CacheKey,
        string GameExecutablePath,
        string MarkerPath,
        string DisplayVersion);

    private sealed record AddressResolution(int TargetRva, int CallerCount, string ExecutableSha256);

    private sealed class CommonUiMarker
    {
        public string PatchManagerVersion { get; set; } = "";
        public DateTimeOffset PatchedAt { get; set; }
        public string CacheKey { get; set; } = "";
        public int ResolvedRva { get; set; }
        public int CallerCount { get; set; }
        public string GameExecutablePath { get; set; } = "";
        public string GameExecutableSha256 { get; set; } = "";
        public long GameExecutableLength { get; set; }
        public long GameExecutableLastWriteUtcTicks { get; set; }
        public string? BackupPath { get; set; }
    }
}
