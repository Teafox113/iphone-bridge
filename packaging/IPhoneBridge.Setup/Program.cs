using System.Drawing;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace IPhoneBridge.Setup;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new SetupForm());
    }
}

internal sealed class SetupForm : Form
{
    private const string InstallerVersion = "0.1.6";
    private const string RuntimeDownloadUrl = "https://dotnet.microsoft.com/download/dotnet/10.0";
    private const string AppResourceName = "IPhoneBridge.Setup.Payload.IPhoneBridge.Windows.exe";
    private const string NoticesResourceName = "IPhoneBridge.Setup.Payload.third-party-notices.txt";
    private const string IconResourceName = "IPhoneBridge.Setup.Payload.phone-bridge.ico";
    private const string UninstallerResourceName = "IPhoneBridge.Setup.Payload.uninstall.ps1";
    private const string RuntimeRequirementResourceName = "IPhoneBridge.Setup.Payload.runtime-requirement.txt";
    private readonly Button _installButton = new();
    private readonly ProgressBar _progress = new();
    private readonly Label _runtimeStatus = new();
    private readonly bool _requiresInstalledRuntime;
    private readonly string _installDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        "iPhone Bridge");

    public SetupForm()
    {
        _requiresInstalledRuntime = ReadRuntimeRequirement();
        Text = "iPhone Bridge 安裝程式";
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath)!;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(470, _requiresInstalledRuntime ? 370 : 300);
        BackColor = Color.FromArgb(241, 244, 249);
        Font = new Font("Segoe UI", 9F);

        var title = new Label
        {
            Text = "iPhone Bridge",
            AutoSize = true,
            Location = new Point(28, 24),
            Font = new Font("Segoe UI", 22F, FontStyle.Bold),
            ForeColor = Color.FromArgb(28, 41, 65),
        };
        var description = new Label
        {
            Text = "將 iPhone 或 Android 手機的照片、影片與檔案接收到 Windows。",
            AutoSize = true,
            Location = new Point(31, 68),
            ForeColor = Color.FromArgb(92, 106, 128),
        };
        var accountNote = new Label
        {
            Text = _requiresInstalledRuntime
                ? "程式安裝至目前帳號；安裝 .NET 執行環境時，Windows 可能要求管理員權限。"
                : "安裝至目前 Windows 帳號，不需要管理員權限。",
            AutoSize = true,
            Location = new Point(31, 94),
            ForeColor = Color.FromArgb(92, 106, 128),
        };
        if (_requiresInstalledRuntime)
        {
            _runtimeStatus.Location = new Point(31, 120);
            _runtimeStatus.Size = new Size(408, 38);
            _runtimeStatus.Font = new Font("Segoe UI", 8.5F);
            _runtimeStatus.AutoEllipsis = true;
            Controls.Add(_runtimeStatus);

            var downloadButton = new Button
            {
                Text = "開啟 .NET 10 官方下載頁",
                Location = new Point(31, 157),
                Size = new Size(190, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(53, 108, 246),
                Cursor = Cursors.Hand,
            };
            downloadButton.FlatAppearance.BorderColor = Color.FromArgb(210, 220, 235);
            downloadButton.Click += (_, _) => Process.Start(new ProcessStartInfo(RuntimeDownloadUrl) { UseShellExecute = true });
            Controls.Add(downloadButton);

            var recheckButton = new Button
            {
                Text = "重新檢查",
                Location = new Point(229, 157),
                Size = new Size(105, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(53, 108, 246),
                Cursor = Cursors.Hand,
            };
            recheckButton.FlatAppearance.BorderColor = Color.FromArgb(210, 220, 235);
            recheckButton.Click += (_, _) => RefreshRuntimeStatus();
            Controls.Add(recheckButton);
        }
        var pathLabel = new Label
        {
            Text = "安裝位置",
            AutoSize = true,
            Location = new Point(31, _requiresInstalledRuntime ? 198 : 142),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(28, 41, 65),
        };
        var pathBox = new TextBox
        {
            Text = _installDirectory,
            ReadOnly = true,
            Location = new Point(31, _requiresInstalledRuntime ? 222 : 166),
            Size = new Size(408, 26),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
        };
        _installButton.Text = File.Exists(Path.Combine(_installDirectory, "IPhoneBridge.Windows.exe"))
            ? "更新／重新安裝"
            : "安裝 iPhone Bridge";
        _installButton.Location = new Point(31, _requiresInstalledRuntime ? 267 : 211);
        _installButton.Size = new Size(180, 42);
        _installButton.FlatStyle = FlatStyle.Flat;
        _installButton.FlatAppearance.BorderSize = 0;
        _installButton.BackColor = Color.FromArgb(53, 108, 246);
        _installButton.ForeColor = Color.White;
        _installButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _installButton.Click += InstallButtonClicked;

        _progress.Location = new Point(31, _requiresInstalledRuntime ? 319 : 263);
        _progress.Size = new Size(408, 8);
        _progress.Style = ProgressBarStyle.Marquee;
        _progress.MarqueeAnimationSpeed = 24;
        _progress.Visible = false;

        var creator = new Label
        {
            Text = "BY floofyfox",
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(365, _requiresInstalledRuntime ? 281 : 225),
            ForeColor = Color.FromArgb(126, 137, 157),
        };

        Controls.AddRange([title, description, accountNote, pathLabel, pathBox, _installButton, _progress, creator]);
        if (_requiresInstalledRuntime) RefreshRuntimeStatus();
    }

    private async void InstallButtonClicked(object? sender, EventArgs e)
    {
        if (_requiresInstalledRuntime && !RefreshRuntimeStatus())
        {
            MessageBox.Show(this,
                "輕量版需要安裝 .NET 10 Desktop Runtime 與 ASP.NET Core Runtime（x64）。請開啟官方下載頁安裝後，再按「重新檢查」。",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (Process.GetProcessesByName("IPhoneBridge.Windows").Length > 0)
        {
            MessageBox.Show(this, "請先關閉 iPhone Bridge，再執行安裝。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _installButton.Enabled = false;
        _progress.Visible = true;
        var applicationPath = Path.Combine(_installDirectory, "IPhoneBridge.Windows.exe");
        var iconPath = Path.Combine(_installDirectory, $"phone-bridge-{InstallerVersion}.ico");
        var temporaryApplicationPath = applicationPath + ".new";
        try
        {
            Directory.CreateDirectory(_installDirectory);
            await using (var source = OpenPayload(AppResourceName))
            await using (var destination = new FileStream(temporaryApplicationPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true))
            {
                await source.CopyToAsync(destination);
            }
            File.Move(temporaryApplicationPath, applicationPath, overwrite: true);

            using (var source = OpenPayload(UninstallerResourceName))
            using (var reader = new StreamReader(source))
            {
                File.WriteAllText(Path.Combine(_installDirectory, "uninstall.ps1"), reader.ReadToEnd(), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            await using (var source = OpenPayload(NoticesResourceName))
            await using (var destination = new FileStream(Path.Combine(_installDirectory, "THIRD-PARTY-NOTICES.txt"), FileMode.Create, FileAccess.Write, FileShare.Read, 81920, useAsync: true))
            {
                await source.CopyToAsync(destination);
            }

            await using (var source = OpenPayload(IconResourceName))
            await using (var destination = new FileStream(iconPath, FileMode.Create, FileAccess.Write, FileShare.Read, 81920, useAsync: true))
            {
                await source.CopyToAsync(destination);
            }

            CreateStartMenuShortcut(applicationPath, iconPath);
            RegisterUninstaller(applicationPath, iconPath);
            RemoveSupersededIcons(iconPath);
            _progress.Visible = false;

            var launch = MessageBox.Show(this, "安裝完成。現在要啟動 iPhone Bridge 嗎？", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (launch == DialogResult.Yes)
                Process.Start(new ProcessStartInfo(applicationPath) { UseShellExecute = true });
            Close();
        }
        catch (Exception ex)
        {
            try { if (File.Exists(temporaryApplicationPath)) File.Delete(temporaryApplicationPath); } catch { }
            _progress.Visible = false;
            _installButton.Enabled = true;
            MessageBox.Show(this, "安裝失敗：" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static Stream OpenPayload(string name)
    {
        return Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("安裝檔缺少必要元件：" + name);
    }

    private static bool ReadRuntimeRequirement()
    {
        using var stream = OpenPayload(RuntimeRequirementResourceName);
        using var reader = new StreamReader(stream);
        return string.Equals(reader.ReadToEnd().Trim(), "framework-dependent", StringComparison.OrdinalIgnoreCase);
    }

    private bool RefreshRuntimeStatus()
    {
        var missing = GetMissingFrameworks();
        var ready = missing.Count == 0;
        _runtimeStatus.Text = ready
            ? ".NET 10 執行環境已就緒，可以安裝輕量版。"
            : "尚缺少：" + string.Join("、", missing) + "。安裝後按「重新檢查」。";
        _runtimeStatus.ForeColor = ready ? Color.FromArgb(47, 137, 100) : Color.FromArgb(174, 105, 34);
        _installButton.Enabled = ready;
        return ready;
    }

    private static List<string> GetMissingFrameworks()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in new[] { "DOTNET_ROOT_X64", "DOTNET_ROOT" })
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value)) roots.Add(value);
        }
        using var runtimeKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64");
        var installLocation = runtimeKey?.GetValue("InstallLocation") as string;
        if (!string.IsNullOrWhiteSpace(installLocation)) roots.Add(installLocation);
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"));

        var required = new Dictionary<string, string>
        {
            ["Microsoft.NETCore.App"] = ".NET Runtime 10 x64",
            ["Microsoft.WindowsDesktop.App"] = ".NET Desktop Runtime 10 x64",
            ["Microsoft.AspNetCore.App"] = "ASP.NET Core Runtime 10 x64",
        };
        return required
            .Where(framework => !roots.Any(root => HasFrameworkVersion(root, framework.Key, 10)))
            .Select(framework => framework.Value)
            .ToList();
    }

    private static bool HasFrameworkVersion(string root, string frameworkName, int requiredMajor)
    {
        var frameworkPath = Path.Combine(root, "shared", frameworkName);
        if (!Directory.Exists(frameworkPath)) return false;
        foreach (var directory in Directory.EnumerateDirectories(frameworkPath))
        {
            var versionName = Path.GetFileName(directory).Split('-', 2)[0];
            if (Version.TryParse(versionName, out var version) && version.Major == requiredMajor) return true;
        }
        return false;
    }

    private void CreateStartMenuShortcut(string applicationPath, string iconPath)
    {
        var programsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        var shortcutDirectory = Path.Combine(programsDirectory, "iPhone Bridge");
        Directory.CreateDirectory(shortcutDirectory);
        var shortcutPath = Path.Combine(shortcutDirectory, "iPhone Bridge.lnk");
        var temporaryShortcutPath = Path.Combine(shortcutDirectory, "iPhone Bridge.update.lnk");
        if (File.Exists(temporaryShortcutPath)) File.Delete(temporaryShortcutPath);

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("無法建立開始功能表捷徑。");
        object shellObject = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("無法建立 Windows 捷徑服務。");
        try
        {
            dynamic shell = shellObject;
            dynamic shortcut = shell.CreateShortcut(temporaryShortcutPath);
            try
            {
                shortcut.TargetPath = applicationPath;
                shortcut.WorkingDirectory = _installDirectory;
                shortcut.Description = "將 iPhone 或 Android 檔案接收到 Windows 電腦";
                shortcut.IconLocation = iconPath;
                shortcut.Save();
                File.Move(temporaryShortcutPath, shortcutPath, overwrite: true);
            }
            finally
            {
                if (Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            }
        }
        finally
        {
            if (Marshal.IsComObject(shellObject)) Marshal.FinalReleaseComObject(shellObject);
        }
    }

    private static void RemoveSupersededIcons(string currentIconPath)
    {
        foreach (var iconPath in Directory.EnumerateFiles(Path.GetDirectoryName(currentIconPath)!, "phone-bridge-*.ico"))
        {
            if (!string.Equals(iconPath, currentIconPath, StringComparison.OrdinalIgnoreCase))
                File.Delete(iconPath);
        }
    }

    private void RegisterUninstaller(string applicationPath, string iconPath)
    {
        var uninstallerPath = Path.Combine(_installDirectory, "uninstall.ps1");
        var powershellPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var uninstallCommand = $"\"{powershellPath}\" -NoProfile -ExecutionPolicy Bypass -File \"{uninstallerPath}\"";
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\IPhoneBridge", writable: true)
            ?? throw new InvalidOperationException("無法建立解除安裝資訊。");
        key.SetValue("DisplayName", "iPhone Bridge");
        key.SetValue("DisplayVersion", InstallerVersion);
        key.SetValue("Publisher", "floofyfox");
        key.SetValue("InstallLocation", _installDirectory);
        key.SetValue("DisplayIcon", iconPath);
        key.SetValue("UninstallString", uninstallCommand);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }
}
