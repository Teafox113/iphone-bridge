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
    private const string InstallerVersion = "0.1.0";
    private const string AppResourceName = "IPhoneBridge.Setup.Payload.IPhoneBridge.Windows.exe";
    private const string NoticesResourceName = "IPhoneBridge.Setup.Payload.third-party-notices.txt";
    private const string UninstallerResourceName = "IPhoneBridge.Setup.Payload.uninstall.ps1";
    private readonly Button _installButton = new();
    private readonly ProgressBar _progress = new();
    private readonly string _installDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        "iPhone Bridge");

    public SetupForm()
    {
        Text = "iPhone Bridge 安裝程式";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(470, 300);
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
            Text = "安裝至目前 Windows 帳號，不需要管理員權限。",
            AutoSize = true,
            Location = new Point(31, 94),
            ForeColor = Color.FromArgb(92, 106, 128),
        };
        var pathLabel = new Label
        {
            Text = "安裝位置",
            AutoSize = true,
            Location = new Point(31, 142),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(28, 41, 65),
        };
        var pathBox = new TextBox
        {
            Text = _installDirectory,
            ReadOnly = true,
            Location = new Point(31, 166),
            Size = new Size(408, 26),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
        };
        _installButton.Text = "安裝 iPhone Bridge";
        _installButton.Location = new Point(31, 211);
        _installButton.Size = new Size(180, 42);
        _installButton.FlatStyle = FlatStyle.Flat;
        _installButton.FlatAppearance.BorderSize = 0;
        _installButton.BackColor = Color.FromArgb(53, 108, 246);
        _installButton.ForeColor = Color.White;
        _installButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _installButton.Click += InstallButtonClicked;

        _progress.Location = new Point(31, 263);
        _progress.Size = new Size(408, 8);
        _progress.Style = ProgressBarStyle.Marquee;
        _progress.MarqueeAnimationSpeed = 24;
        _progress.Visible = false;

        var creator = new Label
        {
            Text = "BY floofyfox",
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(365, 225),
            ForeColor = Color.FromArgb(126, 137, 157),
        };

        Controls.AddRange([title, description, accountNote, pathLabel, pathBox, _installButton, _progress, creator]);
    }

    private async void InstallButtonClicked(object? sender, EventArgs e)
    {
        if (Process.GetProcessesByName("IPhoneBridge.Windows").Length > 0)
        {
            MessageBox.Show(this, "請先關閉 iPhone Bridge，再執行安裝。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _installButton.Enabled = false;
        _progress.Visible = true;
        var applicationPath = Path.Combine(_installDirectory, "IPhoneBridge.Windows.exe");
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

            CreateStartMenuShortcut(applicationPath);
            RegisterUninstaller(applicationPath);
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

    private void CreateStartMenuShortcut(string applicationPath)
    {
        var programsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        var shortcutDirectory = Path.Combine(programsDirectory, "iPhone Bridge");
        Directory.CreateDirectory(shortcutDirectory);
        var shortcutPath = Path.Combine(shortcutDirectory, "iPhone Bridge.lnk");

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("無法建立開始功能表捷徑。");
        object shellObject = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("無法建立 Windows 捷徑服務。");
        try
        {
            dynamic shell = shellObject;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            try
            {
                shortcut.TargetPath = applicationPath;
                shortcut.WorkingDirectory = _installDirectory;
                shortcut.Description = "將 iPhone 或 Android 檔案接收到 Windows 電腦";
                shortcut.IconLocation = applicationPath + ",0";
                shortcut.Save();
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

    private void RegisterUninstaller(string applicationPath)
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
        key.SetValue("DisplayIcon", applicationPath);
        key.SetValue("UninstallString", uninstallCommand);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }
}
