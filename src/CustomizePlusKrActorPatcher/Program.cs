using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace CustomizePlusKrActorPatcher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 4 && args[0] == "--test-patch")
            {
                CustomizePlusPatchCore.Patch(args[1], args[2], args[3]);
                Console.WriteLine("Patch and verification succeeded.");
                return 0;
            }

            if (args.Length == 3 && args[0] == "--test-verify")
            {
                CustomizePlusPatchCore.Verify(args[1], args[2]);
                Console.WriteLine("Verification succeeded.");
                return 0;
            }

            if (args.Length == 2 && args[0] == "--test-discover")
            {
                var context = PatchContext.Discover(args[1]);
                Console.WriteLine($"Customize+: {context.PluginDirectory}");
                Console.WriteLine($"KR hook: {context.HookDirectory}");
                Console.WriteLine($"Version: {context.Version}");
                return 0;
            }

            if (args.Length == 2 && args[0] == "--apply")
            {
                Console.WriteLine(PatchOperations.Apply(args[1]));
                return 0;
            }

            if (args.Length == 2 && args[0] == "--restore")
            {
                Console.WriteLine(PatchOperations.Restore(args[1]));
                return 0;
            }

        }
        catch (Exception ex) when (args.Length > 0 &&
            (args[0].StartsWith("--test-", StringComparison.Ordinal) || args[0] is "--apply" or "--restore"))
        {
            Console.Error.WriteLine(ex);
            return 1;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

}

internal sealed class MainForm : Form
{
    private readonly TextBox profilePathBox = new();
    private readonly Label versionValue = new();
    private readonly Label patchValue = new();
    private readonly Label statusLabel = new();
    private readonly TextBox logBox = new();
    private readonly Button applyButton = new();
    private readonly Button restoreButton = new();
    private readonly Button refreshButton = new();
    private readonly Button browseButton = new();
    private bool busy;

    public MainForm()
    {
        Text = "Customize+ KR Actor Patcher";
        ClientSize = new Size(650, 440);
        MinimumSize = new Size(666, 479);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.FromArgb(245, 246, 248);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        BuildUi();
        profilePathBox.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XIVLauncherKR");
        Shown += (_, _) => RefreshState();
    }

    private void BuildUi()
    {
        var header = new Panel { BackColor = Color.FromArgb(45, 48, 54), Dock = DockStyle.Top, Height = 64 };
        header.Controls.Add(new Label
        {
            Text = "Customize+ KR Actor Patcher",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 14F),
            Location = new Point(18, 10),
            AutoSize = true,
        });
        header.Controls.Add(new Label
        {
            Text = "Korean character name and world recognition patch",
            ForeColor = Color.FromArgb(190, 195, 202),
            Location = new Point(20, 38),
            AutoSize = true,
        });
        Controls.Add(header);

        Controls.Add(new Label { Text = "XIVLauncherKR 프로필", Location = new Point(18, 82), Size = new Size(150, 22) });
        profilePathBox.SetBounds(18, 105, 558, 27);
        profilePathBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        profilePathBox.ReadOnly = true;
        Controls.Add(profilePathBox);

        browseButton.Text = "...";
        browseButton.SetBounds(583, 104, 48, 29);
        browseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        browseButton.Click += (_, _) => BrowseProfile();
        Controls.Add(browseButton);

        Controls.Add(new Label { Text = "설치 버전", Location = new Point(18, 148), Size = new Size(90, 22) });
        versionValue.SetBounds(112, 148, 190, 22);
        versionValue.Font = new Font("Segoe UI Semibold", 9F);
        Controls.Add(versionValue);
        Controls.Add(new Label { Text = "패치 상태", Location = new Point(328, 148), Size = new Size(90, 22) });
        patchValue.SetBounds(422, 148, 209, 22);
        patchValue.Font = new Font("Segoe UI Semibold", 9F);
        Controls.Add(patchValue);

        applyButton.Text = "KR 인식 패치 적용";
        applyButton.SetBounds(18, 180, 188, 38);
        applyButton.BackColor = Color.FromArgb(26, 116, 94);
        applyButton.ForeColor = Color.White;
        applyButton.FlatStyle = FlatStyle.Flat;
        applyButton.FlatAppearance.BorderSize = 0;
        applyButton.Click += async (_, _) => await ApplyPatchAsync();
        Controls.Add(applyButton);

        restoreButton.Text = "원본 복구";
        restoreButton.SetBounds(216, 180, 150, 38);
        restoreButton.Click += async (_, _) => await RestoreAsync();
        Controls.Add(restoreButton);

        refreshButton.Text = "새로고침";
        refreshButton.SetBounds(376, 180, 150, 38);
        refreshButton.Click += (_, _) => RefreshState();
        Controls.Add(refreshButton);

        statusLabel.SetBounds(18, 230, 613, 40);
        statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        statusLabel.ForeColor = Color.FromArgb(65, 68, 74);
        Controls.Add(statusLabel);

        var notice = new Label
        {
            Text = "주의: 같은 캐릭터가 여러 프로필에 있으면 하나만 남겨야 합니다. 빈 Bones 템플릿은 외형을 변경하지 않습니다.",
            ForeColor = Color.FromArgb(150, 86, 24),
            Location = new Point(18, 274),
            Size = new Size(613, 38),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        Controls.Add(notice);

        logBox.SetBounds(18, 316, 613, 105);
        logBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        logBox.Multiline = true;
        logBox.ReadOnly = true;
        logBox.ScrollBars = ScrollBars.Vertical;
        logBox.BackColor = Color.White;
        logBox.Font = new Font("Consolas", 9F);
        Controls.Add(logBox);
    }

    private void BrowseProfile()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "XIVLauncherKR 프로필 폴더를 선택하세요.",
            InitialDirectory = Directory.Exists(profilePathBox.Text) ? profilePathBox.Text : string.Empty,
            UseDescriptionForTitle = true,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            profilePathBox.Text = dialog.SelectedPath;
            RefreshState();
        }
    }

    private void RefreshState()
    {
        if (busy)
        {
            return;
        }

        try
        {
            var context = PatchContext.Discover(profilePathBox.Text);
            versionValue.Text = context.Version;
            var patched = CustomizePlusPatchCore.IsPatched(context.PluginDirectory, context.HookDirectory);
            if (!patched)
            {
                CustomizePlusPatchCore.ValidateSupportedOriginal(context.PluginDirectory);
            }

            patchValue.Text = patched ? "적용됨" : "미적용";
            patchValue.ForeColor = patched ? Color.FromArgb(26, 116, 94) : Color.FromArgb(170, 82, 32);
            statusLabel.Text = patched
                ? "KR 캐릭터 이름과 월드 ID 인식 패치가 적용되어 있습니다."
                : "Customize+ 2.2.0.3 공식 설치본에 패치를 적용할 수 있습니다.";
            applyButton.Enabled = !patched;
            restoreButton.Enabled = context.HasBackup;
            AppendLog($"Customize+ {context.Version}: {(patched ? "patched" : "original")}");
        }
        catch (Exception ex)
        {
            versionValue.Text = "-";
            patchValue.Text = "확인 필요";
            patchValue.ForeColor = Color.Firebrick;
            statusLabel.Text = FirstLine(ex.Message);
            applyButton.Enabled = false;
            restoreButton.Enabled = false;
            AppendLog(ex.Message);
        }
    }

    private async Task ApplyPatchAsync()
    {
        await RunOperationAsync("패치 적용 중...", () => PatchOperations.Apply(profilePathBox.Text));
    }

    private async Task RestoreAsync()
    {
        await RunOperationAsync("원본 복구 중...", () => PatchOperations.Restore(profilePathBox.Text));
    }

    private async Task RunOperationAsync(string runningText, Func<string> operation)
    {
        if (busy)
        {
            return;
        }

        SetBusy(true);
        statusLabel.Text = runningText;
        try
        {
            var result = await Task.Run(operation);
            AppendLog(result);
            MessageBox.Show(this, result, "Customize+ KR Actor Patcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppendLog(ex.ToString());
            MessageBox.Show(this, ex.Message, "Customize+ KR Actor Patcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            RefreshState();
        }
    }

    private void SetBusy(bool value)
    {
        busy = value;
        applyButton.Enabled = !value;
        restoreButton.Enabled = !value;
        refreshButton.Enabled = !value;
        browseButton.Enabled = !value;
        UseWaitCursor = value;
    }

    private void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message.Trim()}";
        logBox.AppendText((logBox.TextLength == 0 ? string.Empty : Environment.NewLine) + line + Environment.NewLine);
    }

    private static string FirstLine(string value)
        => value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? value;

}

internal static class PatchOperations
{
    public static string Apply(string profileRoot)
    {
        EnsureDalamudStopped();
        var context = PatchContext.Discover(profileRoot);
        if (CustomizePlusPatchCore.IsPatched(context.PluginDirectory, context.HookDirectory))
        {
            return "이미 패치가 적용되어 있습니다.";
        }

        var staging = Path.Combine(Path.GetTempPath(), "CustomizePlusKrActorPatcher", Guid.NewGuid().ToString("N"));
        try
        {
            CustomizePlusPatchCore.Patch(context.PluginDirectory, context.HookDirectory, staging);
            var backup = context.CreateBackup();
            try
            {
                context.CopyPatchedFileFrom(staging);
                CustomizePlusPatchCore.Verify(context.PluginDirectory, context.HookDirectory);
                context.WriteMarker(backup);
            }
            catch
            {
                context.CopyPatchedFileFrom(backup);
                throw;
            }

            return $"패치 적용 완료: Customize+ {context.Version}\r\n백업: {backup}\r\n\r\n게임 실행 후 중복 캐릭터 할당을 하나만 남기세요.";
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    public static string Restore(string profileRoot)
    {
        EnsureDalamudStopped();
        var context = PatchContext.Discover(profileRoot);
        var backup = context.FindLatestBackup()
            ?? throw new InvalidOperationException("복구할 원본 백업을 찾을 수 없습니다.");
        context.CopyPatchedFileFrom(backup);
        context.DeleteMarker();
        return $"원본 복구 완료: Customize+ {context.Version}\r\n사용한 백업: {backup}";
    }

    internal static void EnsureDalamudStopped()
    {
        var blockedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ffxiv_dx11", "ffxiv", "XIVLauncher", "DalamudCrashHandler",
            "Dalamud.Updater.Gui", "KrDalamudUpdaterGui",
        };
        var running = Process.GetProcesses()
            .Where(process => blockedNames.Contains(process.ProcessName))
            .Select(process => $"{process.ProcessName} ({process.Id})")
            .ToArray();
        if (running.Length > 0)
        {
            throw new InvalidOperationException(
                "게임과 Dalamud 관련 프로그램을 모두 종료한 뒤 다시 실행하세요.\r\n\r\n" +
                string.Join("\r\n", running));
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // Windows can clean up an abandoned staging directory later.
        }
    }
}

internal sealed class PatchContext
{
    public const string PatchedFileName = "Penumbra.GameData.dll";
    private const string RequiredClientStructsVersion = "7.51.0.8319";
    private const int RequiredLanguagePatchVersion = 4;

    private PatchContext(string profileRoot, string pluginDirectory, string hookDirectory, string version)
    {
        ProfileRoot = profileRoot;
        PluginDirectory = pluginDirectory;
        HookDirectory = hookDirectory;
        Version = version;
    }

    public string ProfileRoot { get; }
    public string PluginDirectory { get; }
    public string HookDirectory { get; }
    public string Version { get; }
    public string BackupRoot => Path.Combine(ProfileRoot, "kr-patch-backups", "CustomizePlus", Version);
    public string MarkerPath => Path.Combine(PluginDirectory, "CustomizePlus.KR.Actor.Patch.json");
    public bool HasBackup => FindLatestBackup() != null;

    public static PatchContext Discover(string profileRoot)
    {
        var fullProfileRoot = Path.GetFullPath(profileRoot);
        if (!Directory.Exists(fullProfileRoot))
        {
            throw new DirectoryNotFoundException($"XIVLauncherKR 프로필을 찾을 수 없습니다: {fullProfileRoot}");
        }

        var pluginRoot = Path.Combine(fullProfileRoot, "installedPlugins", "CustomizePlus");
        if (!Directory.Exists(pluginRoot))
        {
            throw new DirectoryNotFoundException("공식 레포지토리에서 Customize+를 먼저 설치하세요.");
        }

        var candidates = Directory.GetDirectories(pluginRoot)
            .Where(directory => File.Exists(Path.Combine(directory, "CustomizePlus.dll")) &&
                File.Exists(Path.Combine(directory, PatchedFileName)))
            .Select(directory => new
            {
                Directory = directory,
                Version = ParseVersion(Path.GetFileName(directory)),
                LastWrite = Directory.GetLastWriteTimeUtc(directory),
            })
            .OrderByDescending(item => item.Version)
            .ThenByDescending(item => item.LastWrite)
            .ToArray();
        if (candidates.Length == 0)
        {
            throw new FileNotFoundException("Customize+ 설치 DLL을 찾을 수 없습니다.");
        }

        var selected = candidates[0];
        var version = Path.GetFileName(selected.Directory);
        if (!version.Equals(CustomizePlusPatchCore.SupportedVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Customize+ {version}은 지원하지 않습니다. 현재 패처는 {CustomizePlusPatchCore.SupportedVersion} 전용입니다.");
        }

        return new PatchContext(fullProfileRoot, selected.Directory, FindHookDirectory(fullProfileRoot), version);
    }

    public string CreateBackup()
    {
        var backup = Path.Combine(BackupRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(backup);
        File.Copy(Path.Combine(PluginDirectory, PatchedFileName), Path.Combine(backup, PatchedFileName), false);
        var manifest = Path.Combine(PluginDirectory, "CustomizePlus.json");
        if (File.Exists(manifest))
        {
            File.Copy(manifest, Path.Combine(backup, "CustomizePlus.json"), false);
        }

        return backup;
    }

    public string? FindLatestBackup()
    {
        if (!Directory.Exists(BackupRoot))
        {
            return null;
        }

        return Directory.GetDirectories(BackupRoot)
            .Where(directory => File.Exists(Path.Combine(directory, PatchedFileName)))
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    public void CopyPatchedFileFrom(string sourceDirectory)
    {
        var source = Path.Combine(sourceDirectory, PatchedFileName);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("패치 파일을 찾을 수 없습니다.", source);
        }

        File.Copy(source, Path.Combine(PluginDirectory, PatchedFileName), true);
    }

    public void WriteMarker(string backupDirectory)
    {
        var patchedDll = Path.Combine(PluginDirectory, PatchedFileName);
        var marker = new
        {
            patchVersion = "0.1.0",
            customizePlusVersion = Version,
            originalCustomizePlusSha256 = CustomizePlusPatchCore.OriginalCustomizePlusSha256,
            originalGameDataSha256 = CustomizePlusPatchCore.OriginalGameDataSha256,
            patchedGameDataSha256 = HashFile(patchedDll),
            patchedAt = DateTimeOffset.Now,
            backupDirectory,
            patches = new[] { "KoreanPlayerName", "KoreanHomeWorld", "KoreanWorldDisplay" },
        };
        File.WriteAllText(MarkerPath, JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void DeleteMarker()
    {
        if (File.Exists(MarkerPath))
        {
            File.Delete(MarkerPath);
        }
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string FindHookDirectory(string profileRoot)
    {
        var hooksRoot = Path.Combine(profileRoot, "addon", "Hooks");
        if (!Directory.Exists(hooksRoot))
        {
            throw new DirectoryNotFoundException("Dalamud addon Hooks 폴더를 찾을 수 없습니다.");
        }

        return Directory.GetDirectories(hooksRoot)
                   .Where(IsCompatibleKrHook)
                   .OrderByDescending(Directory.GetLastWriteTimeUtc)
                   .FirstOrDefault()
               ?? throw new FileNotFoundException(
                   "Customize+ KR 액터 패치에 필요한 호환 훅을 찾지 못했습니다. " +
                   "KR Dalamud 업데이터로 호환 패치를 먼저 적용하세요.");
    }

    private static bool IsCompatibleKrHook(string directory)
    {
        var dalamud = Path.Combine(directory, "Dalamud.dll");
        var clientStructs = Path.Combine(directory, "FFXIVClientStructs.dll");
        var compatibilityMarker = Path.Combine(directory, "Dalamud.KR.Compatibility.Patch.json");
        var languageMarker = Path.Combine(directory, "Dalamud.KR.Language.Patch.json");
        if (!File.Exists(dalamud) || !File.Exists(clientStructs) ||
            !File.Exists(compatibilityMarker) || !File.Exists(languageMarker))
        {
            return false;
        }

        try
        {
            using var compatibility = JsonDocument.Parse(File.ReadAllText(compatibilityMarker));
            if (!compatibility.RootElement.TryGetProperty("ClientStructsFileVersion", out var clientStructsVersion) ||
                clientStructsVersion.GetString() != RequiredClientStructsVersion)
            {
                return false;
            }

            using var language = JsonDocument.Parse(File.ReadAllText(languageMarker));
            if (!language.RootElement.TryGetProperty("Version", out var version) ||
                version.GetInt32() < RequiredLanguagePatchVersion ||
                !language.RootElement.TryGetProperty("CorePatches", out var corePatches))
            {
                return false;
            }

            return corePatches.EnumerateArray().Any(patch => patch.GetString() == "ExcelLanguageKorean");
        }
        catch
        {
            return false;
        }
    }

    private static Version ParseVersion(string value)
        => System.Version.TryParse(value, out var version) ? version : new Version(0, 0);
}
