using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CustomizePlusKrActorPatcher;
using GlamourerKrActorPatcher;
using KrDalamudPatchManager.Modules;

namespace KrDalamudPatchManager;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--status")
        {
            foreach (var module in PatchManagerEdition.VisibleModules())
            {
                var status = module.GetStatus(args[1]);
                Console.WriteLine($"{module.Id}\t{status.Version ?? "not-installed"}\t{status.Message}");
            }

            return 0;
        }

        if (args.Length == 1 && args[0] == "--check-update")
        {
            var release = PatchManagerUpdater.GetLatestReleaseAsync().GetAwaiter().GetResult();
            Console.WriteLine($"latest={release.Version}\nzip={release.ZipUrl}\nsha256={release.HashUrl}");
            return 0;
        }

        if (args.Length == 3 && args[0] == "--inspect-pending")
        {
            var module = PatchManagerEdition.VisibleModules().FirstOrDefault(candidate => candidate.Id == args[2] && candidate.CanInspectUpdates)
                ?? throw new ArgumentException("새 버전 검사 지원 모듈을 찾지 못했습니다.");
            Console.WriteLine(PendingPatchInspector.Inspect(module, args[1]));
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new PatchManagerForm());
        return 0;
    }
}

internal sealed class PatchManagerForm : Form
{
    private readonly TextBox profileRootBox = new();
    private readonly ListView modulesView = new();
    private readonly TextBox logBox = new();
    private readonly Button applyButton = new();
    private readonly Button restoreButton = new();
    private readonly Button refreshButton = new();
    private readonly Button browseButton = new();
    private readonly Button updateButton = new();
    private readonly Button inspectUpdateButton = new();
    private readonly List<PatchModule> modules = PatchManagerEdition.VisibleModules();
    private bool busy;

    public PatchManagerForm()
    {
        Text = "KR Dalamud Patch Manager";
        ClientSize = new Size(900, 570);
        MinimumSize = new Size(916, 609);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.FromArgb(245, 246, 248);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        BuildUi();
        profileRootBox.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XIVLauncherKR");
        Shown += (_, _) => RefreshModules();
    }

    private void BuildUi()
    {
        var header = new Panel { BackColor = Color.FromArgb(45, 48, 54), Dock = DockStyle.Top, Height = 72 };
        header.Controls.Add(new Label
        {
            Text = "KR Dalamud Patch Manager",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 16F),
            Location = new Point(18, 10),
            AutoSize = true,
        });
        header.Controls.Add(new Label
        {
            Text = "선택한 KR 호환성 · 데이터 패치를 한 곳에서 적용하고 원본으로 복원합니다.",
            ForeColor = Color.FromArgb(195, 200, 208),
            Location = new Point(20, 42),
            AutoSize = true,
        });
        Controls.Add(header);

        Controls.Add(new Label { Text = "XIVLauncherKR 경로", Location = new Point(18, 90), Size = new Size(130, 24) });
        profileRootBox.SetBounds(18, 115, 754, 27);
        profileRootBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        Controls.Add(profileRootBox);

        browseButton.Text = "찾기";
        browseButton.SetBounds(780, 114, 102, 29);
        browseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        browseButton.Click += (_, _) => Browse();
        Controls.Add(browseButton);

        modulesView.SetBounds(18, 158, 864, 226);
        modulesView.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        modulesView.CheckBoxes = true;
        modulesView.FullRowSelect = true;
        modulesView.GridLines = true;
        modulesView.HideSelection = false;
        modulesView.View = View.Details;
        modulesView.Columns.Add("모듈", 180);
        modulesView.Columns.Add("분류", 95);
        modulesView.Columns.Add("설치 버전", 100);
        modulesView.Columns.Add("상태", 210);
        modulesView.Columns.Add("검증 성공 조건", 275);
        Controls.Add(modulesView);

        applyButton.Text = "선택 항목 적용";
        applyButton.SetBounds(18, 398, 150, 34);
        applyButton.Click += async (_, _) => await ApplySelectedAsync();
        Controls.Add(applyButton);

        restoreButton.Text = "선택 항목 복원";
        restoreButton.SetBounds(176, 398, 150, 34);
        restoreButton.Click += async (_, _) => await RestoreSelectedAsync();
        Controls.Add(restoreButton);

        refreshButton.Text = "상태 새로고침";
        refreshButton.SetBounds(334, 398, 130, 34);
        refreshButton.Click += (_, _) => RefreshModules();
        Controls.Add(refreshButton);

        updateButton.Text = "업데이트 확인";
        updateButton.SetBounds(472, 398, 130, 34);
        updateButton.Click += async (_, _) => await CheckForUpdateAsync();
        Controls.Add(updateButton);

        if (!PatchManagerEdition.IsLite)
        {
            inspectUpdateButton.Text = "새 버전 검사";
            inspectUpdateButton.SetBounds(610, 398, 130, 34);
            inspectUpdateButton.Click += async (_, _) => await InspectSelectedUpdatesAsync();
            Controls.Add(inspectUpdateButton);
        }

        Controls.Add(new Label
        {
            Text = "적용 전 게임·XIVLauncher·Dalamud를 모두 종료해야 합니다. 원본은 %APPDATA%\\XIVLauncherKR\\kr-patch-backups에 보관됩니다.",
            Location = new Point(18, 441),
            Size = new Size(850, 24),
            ForeColor = Color.FromArgb(82, 88, 96),
        });

        logBox.SetBounds(18, 470, 864, 82);
        logBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        logBox.BackColor = Color.White;
        logBox.Font = new Font("Consolas", 9F);
        logBox.Multiline = true;
        logBox.ReadOnly = true;
        logBox.ScrollBars = ScrollBars.Vertical;
        Controls.Add(logBox);
    }

    private void Browse()
    {
        using var dialog = new FolderBrowserDialog { Description = "XIVLauncherKR 폴더를 선택하세요." };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            profileRootBox.Text = dialog.SelectedPath;
            RefreshModules();
        }
    }

    private void RefreshModules()
    {
        modulesView.BeginUpdate();
        modulesView.Items.Clear();
        foreach (var module in modules)
        {
            var status = module.GetStatus(profileRootBox.Text);
            var item = new ListViewItem(module.Name) { Tag = module, Checked = status.CanApply };
            item.SubItems.Add(module.Group);
            item.SubItems.Add(status.Version ?? "미설치");
            item.SubItems.Add(status.Message);
            item.SubItems.Add(module.SuccessCase);
            item.ForeColor = status.IsError ? Color.Firebrick : status.IsPatched ? Color.FromArgb(20, 110, 62) : Color.FromArgb(45, 48, 54);
            modulesView.Items.Add(item);
        }
        modulesView.EndUpdate();
    }

    private async Task ApplySelectedAsync()
    {
        var selected = SelectedModules();
        if (selected.Length == 0)
        {
            MessageBox.Show(this, "적용할 모듈을 선택하세요.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        await RunAsync("패치 적용", () =>
        {
            PatchModule.EnsureGameStopped();
            return selected.Select(module => module.Apply(profileRootBox.Text)).ToArray();
        });
    }

    private async Task RestoreSelectedAsync()
    {
        var selected = SelectedModules();
        if (selected.Length == 0)
        {
            MessageBox.Show(this, "복원할 모듈을 선택하세요.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        await RunAsync("원본 복원", () =>
        {
            PatchModule.EnsureGameStopped();
            return selected.Select(module => module.Restore(profileRootBox.Text)).ToArray();
        });
    }

    private PatchModule[] SelectedModules()
        => modulesView.CheckedItems.Cast<ListViewItem>().Select(item => (PatchModule)item.Tag!).ToArray();

    private async Task RunAsync(string title, Func<string[]> operation)
    {
        if (busy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var results = await Task.Run(operation);
            var message = string.Join(Environment.NewLine, results);
            AppendLog(message);
            MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppendLog(ex.ToString());
            MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            RefreshModules();
        }
    }

    private async Task CheckForUpdateAsync()
    {
        if (busy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var release = await PatchManagerUpdater.GetLatestReleaseAsync();
            var current = PatchManagerUpdater.CurrentVersion;
            if (release.Version <= current)
            {
                MessageBox.Show(this, $"이미 최신 버전입니다.\n\n현재 버전: {current}", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var answer = MessageBox.Show(
                this,
                $"새 버전이 있습니다.\n\n현재: {current}\n최신: {release.Version}\n\nGitHub에서 내려받고 SHA-256을 확인한 뒤 프로그램을 다시 시작할까요?",
                Text,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
            {
                return;
            }

            await PatchManagerUpdater.DownloadAndRestartAsync(release);
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            AppendLog(ex.ToString());
            MessageBox.Show(this, ex.Message, "업데이트 확인", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task InspectSelectedUpdatesAsync()
    {
        var selected = SelectedModules().Where(module => module.CanInspectUpdates).ToArray();
        if (selected.Length == 0)
        {
            MessageBox.Show(this, "새 버전 검사를 지원하는 모듈을 선택하세요.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        await RunAsync("새 버전 격리 검사", () => selected
            .Select(module => PendingPatchInspector.Inspect(module, profileRootBox.Text))
            .ToArray());
    }

    private void SetBusy(bool value)
    {
        busy = value;
        applyButton.Enabled = !value;
        restoreButton.Enabled = !value;
        refreshButton.Enabled = !value;
        browseButton.Enabled = !value;
        updateButton.Enabled = !value;
        inspectUpdateButton.Enabled = !value;
        UseWaitCursor = value;
    }

    private void AppendLog(string message)
        => logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
}

internal static class PatchManagerUpdater
{
    private const string Repository = "MiqoKR/kr-dalamud-patches";
    private const string AssetPrefix = "KR.Dalamud.PatchManager-";
    private const string AssetSuffix = ".zip";
    private const string LegacyAssetSuffix = "-win-x64.zip";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(2) };

    public static Version CurrentVersion
    {
        get
        {
            var executablePath = Environment.ProcessPath;
            var fileVersion = string.IsNullOrWhiteSpace(executablePath) ? null : FileVersionInfo.GetVersionInfo(executablePath).FileVersion;
            return Version.TryParse(fileVersion, out var version) ? version : new Version(0, 0, 0);
        }
    }

    public static async Task<ManagerRelease> GetLatestReleaseAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases?per_page=100");
        request.Headers.UserAgent.ParseAdd("KR-Dalamud-PatchManager");
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        var selected = document.RootElement.EnumerateArray()
            .Where(release => !release.GetProperty("draft").GetBoolean() && !release.GetProperty("prerelease").GetBoolean())
            .Select(release => new { Release = release.Clone(), Tag = release.GetProperty("tag_name").GetString() })
            .FirstOrDefault(item => item.Tag != null && item.Tag.StartsWith("patch-manager-v", StringComparison.Ordinal) &&
                Version.TryParse(item.Tag["patch-manager-v".Length..], out _));
        if (selected?.Tag == null || !Version.TryParse(selected.Tag["patch-manager-v".Length..], out var version))
        {
            throw new InvalidOperationException("배포 가능한 Patch Manager 릴리스를 찾지 못했습니다.");
        }

        var expectedZip = $"{AssetPrefix}{version}{AssetSuffix}";
        var legacyZip = $"{AssetPrefix}{version}{LegacyAssetSuffix}";
        var assets = selected.Release.GetProperty("assets").EnumerateArray().Select(asset => new
        {
            Name = asset.GetProperty("name").GetString(),
            Url = asset.GetProperty("browser_download_url").GetString(),
        }).ToArray();
        var zip = assets.FirstOrDefault(asset => asset.Name == expectedZip) ??
            assets.FirstOrDefault(asset => asset.Name == legacyZip) ??
            throw new InvalidOperationException($"릴리스에서 {expectedZip}을(를) 찾지 못했습니다.");
        var zipUrl = zip.Url ?? throw new InvalidOperationException("릴리스 ZIP 다운로드 주소가 없습니다.");
        var hashUrl = assets.FirstOrDefault(asset => asset.Name == zip.Name + ".sha256")?.Url
            ?? throw new InvalidOperationException("릴리스 SHA-256 파일을 찾지 못했습니다.");
        return new ManagerRelease(version, zipUrl, hashUrl);
    }

    public static async Task DownloadAndRestartAsync(ManagerRelease release)
    {
        var currentExecutable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExecutable) || !File.Exists(currentExecutable) ||
            !currentExecutable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("설치된 KR.Dalamud.PatchManager.exe에서만 자동 업데이트할 수 있습니다.");
        }

        var updateRoot = Path.Combine(Path.GetTempPath(), "KR-Dalamud-PatchManager", "updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(updateRoot);
        var archive = Path.Combine(updateRoot, "update.zip");
        var extracted = Path.Combine(updateRoot, "extracted");
        var expectedHash = (await Client.GetStringAsync(release.HashUrl)).Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (expectedHash == null || expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException("릴리스 SHA-256 값 형식이 올바르지 않습니다.");
        }

        await using (var source = await Client.GetStreamAsync(release.ZipUrl))
        await using (var target = File.Create(archive))
        {
            await source.CopyToAsync(target);
        }

        var actualHash = await HashFileAsync(archive);
        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("다운로드한 업데이트 파일의 SHA-256이 릴리스 값과 다릅니다.");
        }

        ZipFile.ExtractToDirectory(archive, extracted);
        var replacement = Path.Combine(extracted, "KR.Dalamud.PatchManager.exe");
        if (!File.Exists(replacement))
        {
            throw new InvalidOperationException("업데이트 압축 파일에서 실행 파일을 찾지 못했습니다.");
        }

        var updateScript = Path.Combine(updateRoot, "apply-update.ps1");
        var script = $"$ErrorActionPreference = 'Stop'\r\n" +
            $"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue\r\n" +
            "Start-Sleep -Milliseconds 500\r\n" +
            "$copied = $false\r\n" +
            "for ($attempt = 0; $attempt -lt 30; $attempt++) {\r\n" +
            "    try {\r\n" +
            $"        Copy-Item -LiteralPath '{EscapePowerShell(replacement)}' -Destination '{EscapePowerShell(currentExecutable)}' -Force -ErrorAction Stop\r\n" +
            "        $copied = $true\r\n" +
            "        break\r\n" +
            "    } catch {\r\n" +
            "        Start-Sleep -Milliseconds 250\r\n" +
            "    }\r\n" +
            "}\r\n" +
            "if (-not $copied) {\r\n" +
            $"    Start-Process -FilePath '{EscapePowerShell(currentExecutable)}'\r\n" +
            "    exit 1\r\n" +
            "}\r\n" +
            $"Start-Process -FilePath '{EscapePowerShell(currentExecutable)}'\r\n";
        File.WriteAllText(updateScript, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{updateScript}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }

    private static async Task<string> HashFileAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }

    private static string EscapePowerShell(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}

internal sealed record ManagerRelease(Version Version, string ZipUrl, string HashUrl);

internal static class PendingPatchInspector
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(5) };

    public static string Inspect(PatchModule module, string profileRoot)
    {
        var reportContext = new InspectionReportContext(module.Id, null, null, null, null, null, null);
        try
        {
            var root = Path.GetFullPath(profileRoot);
            var installedDirectory = FindInstalledDirectory(root, module);
            var installedVersion = Version.Parse(Path.GetFileName(installedDirectory));
            var hookDirectory = PatchModule.FindHookDirectory(root);
            var manifestUrl = module.OfficialManifestUrl ?? throw new InvalidOperationException("공식 매니페스트 URL이 설정되지 않았습니다.");
            var entry = LoadOfficialEntry(manifestUrl, module.PluginFolder);
            var latestVersionText = GetString(entry, "AssemblyVersion") ?? throw new InvalidOperationException("공식 매니페스트에 AssemblyVersion이 없습니다.");
            var latestVersion = Version.Parse(latestVersionText);
            var downloadUrl = GetString(entry, "DownloadLinkUpdate") ?? GetString(entry, "DownloadLinkInstall")
                ?? throw new InvalidOperationException("공식 매니페스트에 다운로드 URL이 없습니다.");
            reportContext = reportContext with { InstalledVersion = installedVersion.ToString(), LatestVersion = latestVersion.ToString(), ManifestUrl = manifestUrl, DownloadUrl = downloadUrl };

            if (latestVersion <= installedVersion)
            {
                var report = WriteReport(root, reportContext with { Outcome = "already-current", Message = "설치된 버전이 공식 최신 버전과 같거나 더 높습니다." });
                return $"{module.Name}: 새 버전 없음 · 리포트: {report}";
            }

            var temporaryRoot = Path.Combine(Path.GetTempPath(), "KR-Dalamud-PatchManager", "pending", module.Id, Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temporaryRoot);
                var archive = Path.Combine(temporaryRoot, "official-release.zip");
                Download(downloadUrl, archive);
                var archiveHash = HashFile(archive);
                var extracted = Path.Combine(temporaryRoot, "extracted");
                ZipFile.ExtractToDirectory(archive, extracted);
                var candidateDirectory = FindCandidateDirectory(extracted, module);
                var originalHashes = module.Files.ToDictionary(file => file, file => HashFile(Path.Combine(candidateDirectory, file)));
                var patchResult = module.ValidateStagedCandidate(candidateDirectory, hookDirectory);
                reportContext = reportContext with
                {
                    ArchiveSha256 = archiveHash,
                    OriginalFileHashes = originalHashes,
                    PatchedFileHashes = patchResult.PatchedFileHashes,
                    Outcome = patchResult.Success ? "existing-patch-compatible" : "patch-update-required",
                    Message = patchResult.Success ? patchResult.Message : patchResult.Error ?? patchResult.Message,
                };
                var report = WriteReport(root, reportContext);
                return patchResult.Success
                    ? $"{module.Name} {latestVersion}: 기존 패치 로직 검증 성공 · 설치본은 변경하지 않음 · 리포트: {report}"
                    : $"{module.Name} {latestVersion}: 지원 대기 · 새 패치 작업 필요 · 설치본은 변경하지 않음 · 리포트: {report}";
            }
            finally
            {
                TryDeleteDirectory(temporaryRoot);
            }
        }
        catch (Exception ex)
        {
            var root = Path.GetFullPath(profileRoot);
            var report = TryWriteReport(root, reportContext with { Outcome = "inspection-failed", Message = ex.ToString() });
            return $"{module.Name}: 새 버전 검사 실패{(report == null ? string.Empty : $" · 리포트: {report}")}";
        }
    }

    private static JsonElement LoadOfficialEntry(string manifestUrl, string internalName)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, manifestUrl);
        request.Headers.UserAgent.ParseAdd("KR-Dalamud-PatchManager");
        using var response = Client.Send(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(response.Content.ReadAsStream());
        var entries = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray()
            : document.RootElement.GetProperty("plugins").EnumerateArray();
        foreach (var entry in entries)
        {
            if (string.Equals(GetString(entry, "InternalName"), internalName, StringComparison.Ordinal))
            {
                return entry.Clone();
            }
        }

        throw new InvalidOperationException($"공식 매니페스트에서 {internalName} 항목을 찾지 못했습니다.");
    }

    private static string FindInstalledDirectory(string profileRoot, PatchModule module)
    {
        var pluginRoot = Path.Combine(profileRoot, "installedPlugins", module.PluginFolder);
        return Directory.GetDirectories(pluginRoot)
                   .Where(directory => module.Files.All(file => File.Exists(Path.Combine(directory, file))))
                   .OrderByDescending(Path.GetFileName)
                   .FirstOrDefault()
               ?? throw new DirectoryNotFoundException($"{module.Name} 설치 폴더를 찾지 못했습니다.");
    }

    private static string FindCandidateDirectory(string extractedRoot, PatchModule module)
    {
        var candidates = new[] { extractedRoot }.Concat(Directory.GetDirectories(extractedRoot, "*", SearchOption.AllDirectories));
        return candidates.FirstOrDefault(directory => module.Files.All(file => File.Exists(Path.Combine(directory, file))))
               ?? throw new FileNotFoundException($"공식 ZIP에서 {module.Name} DLL을 찾지 못했습니다.");
    }

    private static string WriteReport(string profileRoot, InspectionReportContext context)
    {
        var version = context.LatestVersion ?? "unknown";
        var directory = Path.Combine(profileRoot, "kr-patch-reports", context.ModuleId, version);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(context, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    private static string? TryWriteReport(string profileRoot, InspectionReportContext context)
    {
        try
        {
            return WriteReport(profileRoot, context);
        }
        catch
        {
            return null;
        }
    }

    private static void Download(string url, string destination)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("KR-Dalamud-PatchManager");
        using var response = Client.Send(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        using var source = response.Content.ReadAsStream();
        using var target = File.Create(destination);
        source.CopyTo(target);
    }

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
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
            // The inspector leaves an inaccessible temporary folder for operating-system cleanup.
        }
    }

    private sealed record InspectionReportContext(
        string ModuleId,
        string? InstalledVersion,
        string? LatestVersion,
        string? ManifestUrl,
        string? DownloadUrl,
        string? ArchiveSha256,
        IReadOnlyDictionary<string, string>? OriginalFileHashes,
        IReadOnlyDictionary<string, string>? PatchedFileHashes = null,
        string? Outcome = null,
        string? Message = null);
}

internal sealed class PatchModule
{
    private readonly Func<string, string, bool> verify;
    private readonly Func<string, string, bool>? needsUpgrade;
    private readonly Action<string, string, string>? patchToStaging;
    private readonly Action<string, string>? patchInPlace;
    private readonly Action<string, string>? upgradeInPlace;

    private PatchModule(
        string id,
        string name,
        string group,
        string pluginFolder,
        IReadOnlyCollection<string> supportedVersions,
        string[] files,
        string successCase,
        Func<string, string, bool> verify,
        Action<string, string, string>? patchToStaging = null,
        Action<string, string>? patchInPlace = null,
        Func<string, string, bool>? needsUpgrade = null,
        Action<string, string>? upgradeInPlace = null,
        string? legacyMarker = null,
        string? officialManifestUrl = null,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? originalHashesByVersion = null)
    {
        Id = id;
        Name = name;
        Group = group;
        PluginFolder = pluginFolder;
        SupportedVersions = supportedVersions;
        Files = files;
        SuccessCase = successCase;
        this.verify = verify;
        this.needsUpgrade = needsUpgrade;
        this.patchToStaging = patchToStaging;
        this.patchInPlace = patchInPlace;
        this.upgradeInPlace = upgradeInPlace;
        LegacyMarker = legacyMarker;
        OfficialManifestUrl = officialManifestUrl;
        OriginalHashesByVersion = originalHashesByVersion ?? new Dictionary<string, IReadOnlyDictionary<string, string>>();
    }

    public string Id { get; }
    public string Name { get; }
    public string Group { get; }
    public string PluginFolder { get; }
    public IReadOnlyCollection<string> SupportedVersions { get; }
    public string[] Files { get; }
    public string SuccessCase { get; }
    public string? LegacyMarker { get; }
    public string? OfficialManifestUrl { get; }
    public bool CanInspectUpdates => OfficialManifestUrl != null;
    private IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> OriginalHashesByVersion { get; }
    private string ManagerMarker => $"KR.Dalamud.PatchManager.{Id}.json";

    public static List<PatchModule> CreateAll() => new()
    {
        new PatchModule(
            "customizeplus", "Customize+ KR 캐릭터 인식", "호환성", "CustomizePlus", new[] { "2.2.0.3" }, new[] { "Penumbra.GameData.dll" }, "한국어 단일 이름 · KR 월드 ID 인식",
            (plugin, hook) => TryVerify(() => CustomizePlusPatchCore.Verify(plugin, hook)),
            (source, hook, output) => CustomizePlusPatchCore.Patch(source, hook, output),
            needsUpgrade: CustomizePlusPatchCore.NeedsActorRuntimeUpgrade,
            upgradeInPlace: CustomizePlusPatchCore.UpgradeActorRuntime,
            legacyMarker: "CustomizePlus.KR.Actor.Patch.json"),
        new PatchModule(
            "glamourer", "Glamourer KR 호환성", "호환성", "Glamourer", new[] { "1.6.1.7", "1.7.0.1", "1.7.0.2" }, new[] { "Glamourer.dll", "Penumbra.GameData.dll" }, "한국어 캐릭터 조건 · CreateNewModel 호환",
            GlamourerPatchCore.IsPatched,
            (source, hook, output) => GlamourerPatchCore.Patch(source, hook, output),
            needsUpgrade: GlamourerPatchCore.NeedsWorldDisplayUpgrade,
            upgradeInPlace: GlamourerPatchCore.UpgradeWorldDisplay,
            legacyMarker: "Glamourer.KR.Actor.Patch.json",
            originalHashesByVersion: new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["1.7.0.1"] = new Dictionary<string, string>
                {
                    ["Glamourer.dll"] = "BE9484C105846C577AF2C98DEA09CEB670AAFFA16C3CB57BE6F51E4CBDB9BB60",
                    ["Penumbra.GameData.dll"] = "A9BB73AA345245066ED23ED6C8CF95758E4A658FED09432499FEEDF20A258062",
                },
                ["1.7.0.2"] = new Dictionary<string, string>
                {
                    ["Glamourer.dll"] = "F9A8BC33FE275FE394B76B582664C0B8C6D3BE1436DFF25CAD65547966F5F430",
                    ["Penumbra.GameData.dll"] = "C176B4D6B9727B2AA98121B0157273CD6C51BA27E2C554E6D402B3A0B29DC551",
                },
            }),
        new PatchModule(
            "penumbra", "Penumbra KR 개인 할당", "호환성", "Penumbra", new[] { "1.7.0.5" }, new[] { "Penumbra.GameData.dll" }, "한국어 캐릭터명 · KR 월드 ID 인식",
            PenumbraPatchCore.IsPatched,
            (source, hook, output) => PenumbraPatchCore.Patch(source, hook, output),
            originalHashesByVersion: new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["1.7.0.5"] = new Dictionary<string, string>
                {
                    ["Penumbra.GameData.dll"] = "D904B9A4E7C0159E9519EF3D1612FF16A30EB16DDC4206A717E1EA24037CD9BF",
                },
            }),
        new PatchModule(
            "umbra", "Umbra KR UI 호환성", "호환성", "Umbra", new[] { "3.1.17.0" }, new[] { "Una.Drawing.dll" }, "AtkResNode.IsVisible 클리핑 fallback",
            UmbraPatchCore.IsPatched,
            (source, hook, output) => UmbraPatchCore.Patch(source, hook, output),
            originalHashesByVersion: new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["3.1.17.0"] = new Dictionary<string, string>
                {
                    ["Una.Drawing.dll"] = "E57DDE0995D72BA8CC2F254F30C236408F95568C5822DEEF43B77767F0A0B94D",
                },
            }),
        new PatchModule(
            "simpleheels", "Simple Heels KR 안정성", "호환성", "SimpleHeels", new[] { "0.11.1.8" }, new[] { "SimpleHeels.dll" }, "탑승 기울기 fallback · 수영 높이 보정 훅 비활성",
            SimpleHeelsPatchCore.IsPatched,
            patchInPlace: SimpleHeelsPatchCore.Patch,
            officialManifestUrl: "https://raw.githubusercontent.com/Ottermandias/SeaOfStars/main/repo.json",
            originalHashesByVersion: new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["0.11.1.8"] = new Dictionary<string, string>
                {
                    ["SimpleHeels.dll"] = "07859D6B9E76542F044BB144BDD4BDD3BAD95C6FF56F7C1F8DCC27280BB0C0BE",
                },
            }),
        new PatchModule(
            "bossmodreborn", "BossModReborn KR 데이터", "KR 데이터", "BossModReborn", new[] { "7.5.1.26", "7.5.1.29", "7.5.1.32", "7.5.1.35", "7.5.5.0", "7.5.5.2", "7.5.5.5", "7.5.5.9" }, new[] { "BossModReborn.dll" }, "KR Lumina 시트 · legacy map-effect 제거",
            BossModPatchCore.IsPatched,
            patchInPlace: BossModPatchCore.Patch,
            officialManifestUrl: "https://raw.githubusercontent.com/FFXIV-CombatReborn/CombatRebornRepo/main/pluginmaster.json",
            originalHashesByVersion: new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["7.5.1.29"] = new Dictionary<string, string> { ["BossModReborn.dll"] = "CBE3723EB0721949258D05986E7055DBC2EC892CA17D08278AFDEFCBE8746C8F" },
                ["7.5.1.32"] = new Dictionary<string, string> { ["BossModReborn.dll"] = "444938A28E3D699F142F265CD55A87F2CB2C16E4635984A7478AC5CF8D257522" },
                ["7.5.1.35"] = new Dictionary<string, string> { ["BossModReborn.dll"] = "718049F61ED426CC0C07D58A87801C3A8CA1B23411B4A31063517362FBFA0599" },
                ["7.5.5.0"] = new Dictionary<string, string> { ["BossModReborn.dll"] = "3B0D2B282927E51454B932DDB99DF136C25BB9578DD3A204DFBD77CFB99A6B52" },
                ["7.5.5.2"] = new Dictionary<string, string> { ["BossModReborn.dll"] = "FB05BB5210CDC4CCC29F70FA39A24862621C4EC15C04DD2ED7A5FEE3B198B27B" },
                ["7.5.5.5"] = new Dictionary<string, string> { ["BossModReborn.dll"] = "60EF0EEA8FDBD3482CB363C22123458F6B90FF5B64A761982835E96D2E73948E" },
                ["7.5.5.9"] = new Dictionary<string, string> { ["BossModReborn.dll"] = "0E2F3AB0A6B7F568709E8DC4992BB30EEA2A477B66F63954DED4F049A77507BD" },
            }),
        new PatchModule(
            "gatherbuddyreborn", "GatherBuddyReborn KR 데이터", "KR 데이터", "GatherBuddyReborn", new[] { "7.5.1.0", "7.5.1.1", "7.5.5.0" }, new[] { "GatherBuddy.GameData.dll", "GatherBuddyReborn.dll" }, "언어 fallback · 낚시 Regex fallback",
            GatherBuddyPatchCore.IsPatched,
            (source, hook, output) => GatherBuddyPatchCore.Patch(source, output, hook),
            officialManifestUrl: "https://raw.githubusercontent.com/FFXIV-CombatReborn/CombatRebornRepo/main/pluginmaster.json",
            originalHashesByVersion: new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["7.5.1.1"] = new Dictionary<string, string>
                {
                    ["GatherBuddy.GameData.dll"] = "D8C8AC73285F2D2A9D2B3B17ACACFA07BF08E634B2703F0D8E081E5E83C4E3C2",
                    ["GatherBuddyReborn.dll"] = "F6609A3E47FD0D37BFF31E64F27773C18EC07E59F0367CC969D603984434481B",
                },
                ["7.5.5.0"] = new Dictionary<string, string>
                {
                    ["GatherBuddy.GameData.dll"] = "EDFA515789D00892210A5BB4F1D4BD9A31488B59604FA8406C3A1039378A70B5",
                    ["GatherBuddyReborn.dll"] = "3B4334885D7F0419414620C7AC47A87E9A874AB45FC04851A09271D3F267D7F0",
                },
            }),
    };

    public ModuleStatus GetStatus(string profileRoot)
    {
        try
        {
            var context = Discover(profileRoot);
            if (!SupportedVersions.Contains(context.Version, StringComparer.Ordinal))
            {
                return new ModuleStatus(context.Version, $"미지원 버전입니다. 현재 지원: {string.Join(", ", SupportedVersions)}", false, true, false, false);
            }

            if (verify(context.PluginDirectory, context.HookDirectory))
            {
                var canRestore = FindMarker(context) != null;
                var message = canRestore
                    ? "적용됨 · 원본 복원 가능"
                    : "적용됨 · 기존 원본 백업이 없어 현재 상태 보호";
                return new ModuleStatus(context.Version, message, true, false, false, canRestore);
            }

            if (needsUpgrade?.Invoke(context.PluginDirectory, context.HookDirectory) == true && upgradeInPlace != null)
            {
                return new ModuleStatus(context.Version, "표시용 KR 월드명 사전 보완을 적용할 수 있습니다.", false, false, true, FindMarker(context) != null);
            }

            RequireKnownOriginalHash(context);
            return new ModuleStatus(context.Version, "적용 가능 · 원본 백업 후 처리", false, false, true, false);
        }
        catch (Exception ex)
        {
            return new ModuleStatus(null, ex.Message, false, true, false, false);
        }
    }

    public string Apply(string profileRoot)
    {
        var context = Discover(profileRoot);
        RequireSupported(context);
        if (verify(context.PluginDirectory, context.HookDirectory))
        {
            return $"{Name}: 이미 적용되어 있습니다.";
        }

        if (needsUpgrade?.Invoke(context.PluginDirectory, context.HookDirectory) == true && upgradeInPlace != null)
        {
            if (FindMarker(context) == null)
            {
                throw new InvalidOperationException($"{Name}: 기존 패치의 원본 백업을 찾지 못했습니다. 먼저 복원 후 다시 적용해 주세요.");
            }

            upgradeInPlace(context.PluginDirectory, context.HookDirectory);
            if (!verify(context.PluginDirectory, context.HookDirectory))
            {
                throw new InvalidOperationException("표시용 KR 월드명 사전 보완 검증에 실패했습니다.");
            }

            return $"{Name}: 표시용 KR 월드명 사전 보완을 적용했습니다. 기존 원본 백업은 유지됩니다.";
        }

        RequireKnownOriginalHash(context);

        var backup = CreateBackup(context);
        try
        {
            if (patchInPlace != null)
            {
                patchInPlace(context.PluginDirectory, context.HookDirectory);
            }
            else if (patchToStaging != null)
            {
                var staging = Path.Combine(Path.GetTempPath(), "KR-Dalamud-PatchManager", Id, Guid.NewGuid().ToString("N"));
                try
                {
                    patchToStaging(context.PluginDirectory, context.HookDirectory, staging);
                    CopyFiles(staging, context.PluginDirectory, Files);
                }
                finally
                {
                    TryDeleteDirectory(staging);
                }
            }
            else
            {
                throw new InvalidOperationException("패치 모듈 구현을 찾지 못했습니다.");
            }

            if (!verify(context.PluginDirectory, context.HookDirectory))
            {
                throw new InvalidOperationException("적용 후 검증에 실패했습니다. 백업에서 복원하세요.");
            }

            WriteMarker(context, backup);
            return $"{Name}: 적용 완료 (백업: {backup})";
        }
        catch
        {
            CopyFiles(backup, context.PluginDirectory, Files);
            throw;
        }
    }

    public string Restore(string profileRoot)
    {
        var context = Discover(profileRoot);
        var markerPath = FindMarker(context);
        if (markerPath == null)
        {
            throw new InvalidOperationException($"{Name}: 이 매니저가 관리하는 백업 마커를 찾지 못했습니다.");
        }

        using var marker = JsonDocument.Parse(File.ReadAllText(markerPath));
        if (!marker.RootElement.TryGetProperty("backupDirectory", out var backupProperty) || string.IsNullOrWhiteSpace(backupProperty.GetString()))
        {
            throw new InvalidOperationException($"{Name}: 백업 경로가 없는 마커입니다.");
        }

        var backup = Path.GetFullPath(backupProperty.GetString()!);
        var permittedRoot = Path.GetFullPath(Path.Combine(context.ProfileRoot, "kr-patch-backups", PluginFolder));
        if (!backup.StartsWith(permittedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{Name}: 허용되지 않은 백업 경로입니다.");
        }

        CopyFiles(backup, context.PluginDirectory, Files);
        File.Delete(markerPath);
        var managerMarker = Path.Combine(context.PluginDirectory, ManagerMarker);
        if (!managerMarker.Equals(markerPath, StringComparison.OrdinalIgnoreCase) && File.Exists(managerMarker))
        {
            File.Delete(managerMarker);
        }

        return $"{Name}: 원본 복원 완료 (백업: {backup})";
    }

    internal CandidatePatchResult ValidateStagedCandidate(string sourceDirectory, string hookDirectory)
    {
        var stagingRoot = Path.Combine(Path.GetTempPath(), "KR-Dalamud-PatchManager", "candidate", Id, Guid.NewGuid().ToString("N"));
        try
        {
            string patchedDirectory;
            if (patchInPlace != null)
            {
                patchedDirectory = Path.Combine(stagingRoot, "plugin");
                CopyDirectory(sourceDirectory, patchedDirectory);
                patchInPlace(patchedDirectory, hookDirectory);
            }
            else if (patchToStaging != null)
            {
                patchedDirectory = Path.Combine(stagingRoot, "patched");
                patchToStaging(sourceDirectory, hookDirectory, patchedDirectory);
            }
            else
            {
                throw new InvalidOperationException("패치 모듈 구현을 찾지 못했습니다.");
            }

            if (!verify(patchedDirectory, hookDirectory))
            {
                throw new InvalidOperationException("격리된 후보 파일의 패치 후 검증에 실패했습니다.");
            }

            return new CandidatePatchResult(true, "기존 패치 로직으로 검증 성공", Files.ToDictionary(
                file => file,
                file => HashFile(Path.Combine(patchedDirectory, file))), null);
        }
        catch (Exception ex)
        {
            return new CandidatePatchResult(false, "새 버전에 기존 패치 로직을 적용할 수 없습니다.", null, ex.ToString());
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }

    public static void EnsureGameStopped()
    {
        var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ffxiv_dx11", "ffxiv", "XIVLauncher", "DalamudCrashHandler", "Dalamud.Updater.Gui", "KrDalamudUpdaterGui",
        };
        var running = Process.GetProcesses().Where(process => blocked.Contains(process.ProcessName))
            .Select(process => $"{process.ProcessName} ({process.Id})").ToArray();
        if (running.Length > 0)
        {
            throw new InvalidOperationException("게임과 Dalamud/XIVLauncher를 모두 종료한 뒤 다시 시도하세요.\r\n\r\n" + string.Join("\r\n", running));
        }
    }

    private ModuleContext Discover(string profileRoot)
    {
        var root = Path.GetFullPath(profileRoot);
        var pluginRoot = Path.Combine(root, "installedPlugins", PluginFolder);
        if (!Directory.Exists(pluginRoot))
        {
            throw new DirectoryNotFoundException($"{Name} 설치 폴더를 찾지 못했습니다.");
        }

        var candidate = Directory.GetDirectories(pluginRoot)
            .Where(directory => Files.All(file => File.Exists(Path.Combine(directory, file))))
            .OrderByDescending(Path.GetFileName)
            .FirstOrDefault() ?? throw new FileNotFoundException($"{Name} DLL을 찾지 못했습니다.");
        var hook = FindHookDirectory(root);
        return new ModuleContext(root, candidate, hook, Path.GetFileName(candidate));
    }

    private void RequireSupported(ModuleContext context)
    {
        if (!SupportedVersions.Contains(context.Version, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"{Name} {context.Version}은(는) 지원하지 않습니다. 현재 지원 버전: {string.Join(", ", SupportedVersions)}");
        }
    }

    private void RequireKnownOriginalHash(ModuleContext context)
    {
        if (!OriginalHashesByVersion.TryGetValue(context.Version, out var expectedHashes))
        {
            return;
        }

        foreach (var (file, expectedHash) in expectedHashes)
        {
            var actualHash = HashFile(Path.Combine(context.PluginDirectory, file));
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"{Name} {context.Version}의 {file} 원본 SHA-256이 검증값과 다릅니다.");
            }
        }
    }

    private string? FindMarker(ModuleContext context)
        => new[] { Path.Combine(context.PluginDirectory, ManagerMarker), LegacyMarker == null ? null : Path.Combine(context.PluginDirectory, LegacyMarker) }
            .FirstOrDefault(path => path != null && File.Exists(path));

    private string CreateBackup(ModuleContext context)
    {
        var backup = Path.Combine(context.ProfileRoot, "kr-patch-backups", PluginFolder, context.Version, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(backup);
        CopyFiles(context.PluginDirectory, backup, Files);
        return backup;
    }

    private void WriteMarker(ModuleContext context, string backup)
    {
        var marker = new
        {
            patchManagerVersion = "0.1.0",
            module = Id,
            pluginVersion = context.Version,
            patchedAt = DateTimeOffset.Now,
            backupDirectory = backup,
            files = Files.ToDictionary(file => file, file => HashFile(Path.Combine(context.PluginDirectory, file))),
        };
        File.WriteAllText(Path.Combine(context.PluginDirectory, ManagerMarker), JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static string FindHookDirectory(string profileRoot)
    {
        var hooksRoot = Path.Combine(profileRoot, "addon", "Hooks");
        if (!Directory.Exists(hooksRoot))
        {
            throw new DirectoryNotFoundException("Dalamud Hooks 폴더를 찾지 못했습니다.");
        }

        return Directory.GetDirectories(hooksRoot)
                   .Where(directory => File.Exists(Path.Combine(directory, "Dalamud.dll")) && File.Exists(Path.Combine(directory, "Lumina.Excel.dll")))
                   .OrderByDescending(Directory.GetLastWriteTimeUtc)
                   .FirstOrDefault()
               ?? throw new FileNotFoundException("호환되는 Dalamud Hooks 설치본을 찾지 못했습니다.");
    }

    private static bool TryVerify(Action verify)
    {
        try
        {
            verify();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void CopyFiles(string source, string destination, IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            var sourcePath = Path.Combine(source, file);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("필수 백업 또는 패치 파일을 찾지 못했습니다.", sourcePath);
            }
            File.Copy(sourcePath, Path.Combine(destination, file), true);
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        foreach (var sourcePath in Directory.EnumerateFileSystemEntries(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            if (Directory.Exists(sourcePath))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, true);
        }
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
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
            // The temporary staging folder is safe to leave for operating-system cleanup.
        }
    }

    internal sealed record ModuleStatus(string? Version, string Message, bool IsPatched, bool IsError, bool CanApply, bool CanRestore);
    internal sealed record CandidatePatchResult(bool Success, string Message, IReadOnlyDictionary<string, string>? PatchedFileHashes, string? Error);
    private sealed record ModuleContext(string ProfileRoot, string PluginDirectory, string HookDirectory, string Version);
}
