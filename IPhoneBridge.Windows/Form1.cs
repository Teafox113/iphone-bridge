using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using QRCoder;

namespace IPhoneBridge.Windows;

public sealed class Form1 : Form
{
    private readonly LocalTransferServer _server = new();
    private readonly FlowLayoutPanel _root = new();
    private readonly TextBox _folderBox = new();
    private readonly TextBox _addressBox = new();
    private readonly TextBox _codeBox = new();
    private readonly PictureBox _qrPreview = new();
    private readonly Label _qrPlaceholder = new();
    private readonly Label _statusLabel = new();
    private readonly MatrixRainView _transferView = new();
    private readonly Label _transferHeading = new();
    private readonly Label _transferDetail = new();
    private readonly Label _networkHint = new();
    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();
    private readonly Button _chooseFolderButton = new();
    private readonly Button _copyAddressButton = new();
    private readonly ListBox _activityList = new();
    private readonly Dictionary<string, Button> _themeButtons = new();
    private readonly ToolTip _themeToolTip = new();
    private readonly Panel _futureGameSlot = new();
    private string _pairingCode = string.Empty;
    private string _selectedTheme = "clean";

    private static readonly IReadOnlyDictionary<string, ThemePalette> Palettes = new Dictionary<string, ThemePalette>
    {
        ["clean"] = new(Color.FromArgb(241, 244, 249), Color.White, Color.FromArgb(28, 41, 65), Color.FromArgb(126, 137, 157), Color.FromArgb(233, 237, 243), Color.FromArgb(248, 250, 253), Color.FromArgb(53, 108, 246)),
        ["studio"] = new(Color.FromArgb(16, 21, 28), Color.FromArgb(20, 27, 35), Color.FromArgb(241, 244, 246), Color.FromArgb(157, 169, 183), Color.FromArgb(40, 49, 59), Color.FromArgb(23, 31, 40), Color.FromArgb(89, 217, 191)),
        ["warm"] = new(Color.FromArgb(248, 246, 241), Color.FromArgb(255, 254, 250), Color.FromArgb(38, 55, 46), Color.FromArgb(120, 129, 119), Color.FromArgb(230, 228, 220), Color.FromArgb(244, 241, 233), Color.FromArgb(84, 123, 85)),
        ["gba"] = new(Color.FromArgb(168, 185, 149), Color.FromArgb(185, 200, 162), Color.FromArgb(32, 44, 37), Color.FromArgb(80, 97, 73), Color.FromArgb(113, 130, 105), Color.FromArgb(174, 192, 154), Color.FromArgb(77, 93, 71)),
    };

    public Form1()
    {
        Text = "iPhone Bridge";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(520, 780);
        Size = new Size(580, 960);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Palettes[_selectedTheme].Background;

        BuildLayout();
        _server.FileReceived += file => BeginInvoke(() => AddActivity(file));
        _server.ActiveUploadsChanged += count =>
        {
            if (!IsDisposed && IsHandleCreated) BeginInvoke(() => SetTransferCaption(_server.IsRunning, count > 0));
        };
        _folderBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "iPhone Bridge");
        SetRunning(false);
        ApplyTheme(_selectedTheme);
    }

    private void BuildLayout()
    {
        _root.Dock = DockStyle.Fill;
        _root.FlowDirection = FlowDirection.TopDown;
        _root.WrapContents = false;
        _root.AutoScroll = true;
        _root.Padding = new Padding(18, 16, 18, 18);
        _root.Tag = "page";
        Controls.Add(_root);

        _root.Controls.Add(BuildHeader());
        _root.Controls.Add(BuildThemeSection());
        _root.Controls.Add(BuildFolderSection());
        _root.Controls.Add(BuildConnectionSection());
        var firewallHint = new Label
        {
            Text = "第一次連線若出現 Windows 防火牆提示，請允許私人網路存取。",
            Height = 34,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(3, 0, 3, 0),
            Tag = "muted",
            Margin = new Padding(0, 0, 0, 6),
        };
        _root.Controls.Add(firewallHint);
        _root.Controls.Add(BuildActivitySection());
        _root.Controls.Add(BuildCreatorFooter());

        _root.SizeChanged += (_, _) => ResizeSections();
        ResizeSections();
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 1,
            Height = 66,
            Margin = new Padding(0, 0, 0, 8),
            Padding = Padding.Empty,
            Tag = "page",
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var titleArea = new Panel { Dock = DockStyle.Fill, Tag = "page" };
        titleArea.Controls.Add(new Label { Text = "iPhone Bridge", AutoSize = true, Location = new Point(0, 0), Font = new Font("Segoe UI", 23F, FontStyle.Bold), Tag = "brand" });
        titleArea.Controls.Add(new Label { Text = "把 iPhone 的照片、影片與檔案接收到這台電腦。", AutoSize = true, Location = new Point(2, 41), Font = new Font("Segoe UI", 9F), Tag = "muted" });
        header.Controls.Add(titleArea, 0, 0);
        return header;
    }

    private Control BuildThemeSection()
    {
        var section = new Panel { Height = 54, Margin = new Padding(0, 0, 0, 8), Padding = Padding.Empty, Tag = "page" };
        var title = new Label { Text = "介面", Size = new Size(44, 26), TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 10.5F, FontStyle.Bold), Tag = "title" };
        _statusLabel.Text = "尚未啟動";
        _statusLabel.TextAlign = ContentAlignment.MiddleCenter;
        _statusLabel.Size = new Size(104, 26);
        _statusLabel.Margin = Padding.Empty;
        _statusLabel.Tag = "status";
        var themePicker = new FlowLayoutPanel
        {
            Size = new Size(200, 40),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
            Tag = "page",
        };
        AddThemeButton(themePicker, "clean", "01", "清爽原生");
        AddThemeButton(themePicker, "studio", "02", "深色工作台");
        AddThemeButton(themePicker, "warm", "03", "暖白效率");
        AddThemeButton(themePicker, "gba", "04", "GBA 像素風格");
        _futureGameSlot.Size = new Size(34, 34);
        _futureGameSlot.Margin = new Padding(3);
        _futureGameSlot.Tag = "future-slot";
        _futureGameSlot.AccessibleName = "預留的小遊戲位置";
        _futureGameSlot.Paint += (_, e) =>
        {
            using var pen = new Pen(_currentPalette.Border) { DashStyle = DashStyle.Dash };
            e.Graphics.DrawRectangle(pen, 1, 1, _futureGameSlot.Width - 3, _futureGameSlot.Height - 3);
        };
        themePicker.Controls.Add(_futureGameSlot);
        section.Controls.Add(title);
        section.Controls.Add(themePicker);
        section.Controls.Add(_statusLabel);
        section.SizeChanged += (_, _) => ArrangeThemeSection(section, title, themePicker);
        ArrangeThemeSection(section, title, themePicker);
        return section;
    }

    private void ArrangeThemeSection(Panel section, Label title, FlowLayoutPanel themePicker)
    {
        var centerY = section.ClientSize.Height / 2;
        title.Location = new Point(0, centerY - title.Height / 2);
        _statusLabel.Location = new Point(Math.Max(0, section.ClientSize.Width - _statusLabel.Width), centerY - _statusLabel.Height / 2);
        themePicker.Location = new Point(title.Width + 4, centerY - themePicker.Height / 2);
        themePicker.Width = Math.Max(0, _statusLabel.Left - themePicker.Left - 8);
    }

    private void AddThemeButton(FlowLayoutPanel picker, string key, string number, string themeName)
    {
        var button = new Button
        {
            Text = number,
            Size = new Size(34, 34),
            Margin = new Padding(3),
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = false,
            UseCompatibleTextRendering = false,
            Padding = Padding.Empty,
            Tag = "theme:" + key,
            AccessibleName = themeName,
            AccessibleDescription = "切換至" + themeName + "介面",
            UseVisualStyleBackColor = false,
        };
        _themeToolTip.SetToolTip(button, themeName);
        button.SizeChanged += (_, _) => SetRoundedRegion(button, _selectedTheme == "gba" ? 0 : 6);
        button.Click += (_, _) => ApplyTheme(key);
        _themeButtons.Add(key, button);
        picker.Controls.Add(button);
    }

    private Control BuildFolderSection()
    {
        var card = MakeCard("01　儲存位置", string.Empty, 132, out var body);
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = Padding.Empty, Tag = "card" };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        _folderBox.ReadOnly = true;
        _folderBox.Dock = DockStyle.Fill;
        _folderBox.Margin = new Padding(0, 1, 0, 3);
        _folderBox.Tag = "field";
        _chooseFolderButton.Text = "選擇資料夾";
        _chooseFolderButton.Dock = DockStyle.None;
        _chooseFolderButton.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        _chooseFolderButton.Size = new Size(116, 32);
        _chooseFolderButton.Margin = Padding.Empty;
        _chooseFolderButton.Click += (_, _) => ChooseFolder();
        _chooseFolderButton.Tag = "secondary";
        row.Controls.Add(_folderBox, 0, 0);
        row.Controls.Add(_chooseFolderButton, 0, 1);
        body.Controls.Add(row);
        return card;
    }

    private Control BuildConnectionSection()
    {
        var card = MakeCard("02　連接 iPhone", "手機和電腦連上同一個 Wi-Fi，再掃描 QR Code。", 466, out var body);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = Padding.Empty, Tag = "card" };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 144));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 51));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var animationRow = new Panel { Dock = DockStyle.Fill, Tag = "card" };
        _transferView.Size = new Size(58, 58);
        _transferView.Location = new Point(1, 1);
        _transferView.SetTheme(_selectedTheme);
        _transferHeading.Text = "接收待命";
        _transferHeading.AutoSize = true;
        _transferHeading.Location = new Point(72, 9);
        _transferHeading.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        _transferHeading.Tag = "title";
        _transferDetail.Text = "啟動接收以等待資料";
        _transferDetail.AutoSize = true;
        _transferDetail.Location = new Point(73, 32);
        _transferDetail.Font = new Font("Segoe UI", 8.5F);
        _transferDetail.Tag = "muted";
        animationRow.Controls.Add(_transferView);
        animationRow.Controls.Add(_transferHeading);
        animationRow.Controls.Add(_transferDetail);
        layout.Controls.Add(animationRow, 0, 0);

        var qrHost = new Panel { Dock = DockStyle.Fill, Tag = "qrhost", Margin = new Padding(0, 0, 0, 4) };
        _qrPlaceholder.Text = "啟動接收後，QR Code 會出現在這裡";
        _qrPlaceholder.Dock = DockStyle.Fill;
        _qrPlaceholder.TextAlign = ContentAlignment.MiddleCenter;
        _qrPlaceholder.Tag = "muted";
        _qrPreview.Size = new Size(144, 144);
        _qrPreview.SizeMode = PictureBoxSizeMode.CenterImage;
        _qrPreview.BackColor = Color.White;
        _qrPreview.BorderStyle = BorderStyle.FixedSingle;
        _qrPreview.AccessibleName = "iPhone 連線網址 QR Code";
        _qrPreview.Visible = false;
        qrHost.Controls.Add(_qrPlaceholder);
        qrHost.Controls.Add(_qrPreview);
        qrHost.SizeChanged += (_, _) => _qrPreview.Location = new Point(Math.Max(0, (qrHost.ClientSize.Width - _qrPreview.Width) / 2), Math.Max(0, (qrHost.ClientSize.Height - _qrPreview.Height) / 2));
        layout.Controls.Add(qrHost, 0, 1);

        var codeLabel = new Label { Text = "本次連線碼（QR 掃描會自動填入）", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Tag = "muted" };
        layout.Controls.Add(codeLabel, 0, 2);
        _codeBox.ReadOnly = true;
        _codeBox.Dock = DockStyle.None;
        _codeBox.Anchor = AnchorStyles.None;
        _codeBox.Size = new Size(210, 36);
        _codeBox.TextAlign = HorizontalAlignment.Center;
        _codeBox.Tag = "field code";
        layout.Controls.Add(_codeBox, 0, 3);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Anchor = AnchorStyles.None,
            AutoSize = false,
            Padding = Padding.Empty,
            Tag = "card",
        };
        _startButton.Text = "啟動接收";
        _startButton.Size = new Size(138, 40);
        _startButton.Margin = new Padding(4, 3, 8, 2);
        _startButton.Click += async (_, _) => await StartServerAsync();
        _startButton.Tag = "primary";
        _stopButton.Text = "停止接收";
        _stopButton.Size = new Size(120, 40);
        _stopButton.Margin = new Padding(4, 3, 4, 2);
        _stopButton.Click += async (_, _) => await StopServerAsync();
        _stopButton.Tag = "secondary";
        actions.Controls.Add(_startButton);
        actions.Controls.Add(_stopButton);
        layout.Controls.Add(actions, 0, 4);

        var addressRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Tag = "card", Padding = Padding.Empty };
        addressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        addressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        _addressBox.ReadOnly = true;
        _addressBox.Multiline = true;
        _addressBox.ScrollBars = ScrollBars.Vertical;
        _addressBox.Dock = DockStyle.Fill;
        _addressBox.Margin = new Padding(0, 2, 7, 2);
        _addressBox.Tag = "field address";
        _copyAddressButton.Text = "複製網址";
        _copyAddressButton.Dock = DockStyle.Fill;
        _copyAddressButton.Margin = new Padding(2, 2, 0, 2);
        _copyAddressButton.Click += (_, _) => CopyAddress();
        _copyAddressButton.Tag = "secondary";
        addressRow.Controls.Add(_addressBox, 0, 0);
        addressRow.Controls.Add(_copyAddressButton, 1, 0);
        layout.Controls.Add(addressRow, 0, 5);

        _networkHint.Text = "連線碼只在接收服務啟動期間有效。請使用信任的私人網路。";
        _networkHint.Dock = DockStyle.Fill;
        _networkHint.TextAlign = ContentAlignment.MiddleCenter;
        _networkHint.Tag = "muted";
        layout.Controls.Add(_networkHint, 0, 6);
        body.Controls.Add(layout);
        return card;
    }

    private Control BuildActivitySection()
    {
        var card = MakeCard("最近收到", "新檔案會立即出現在儲存資料夾。", 156, out var body);
        _activityList.Dock = DockStyle.Fill;
        _activityList.BorderStyle = BorderStyle.None;
        _activityList.IntegralHeight = false;
        _activityList.Tag = "field activity";
        body.Controls.Add(_activityList);
        return card;
    }

    private Control BuildCreatorFooter()
    {
        return new Label
        {
            Text = "BY floofyfox",
            Height = 28,
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = Padding.Empty,
            Tag = "muted",
            Margin = new Padding(0, 0, 0, 4),
        };
    }

    private static TableLayoutPanel MakeCard(string title, string description, int height, out Panel body)
    {
        return MakeCard(title, description, height, out body, out _);
    }

    private static TableLayoutPanel MakeCard(string title, string description, int height, out Panel body, out Panel header)
    {
        var card = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 2,
            Height = height,
            Padding = new Padding(14, 10, 14, 10),
            Margin = new Padding(0, 0, 0, 8),
            Tag = "surface-card",
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var hasDescription = !string.IsNullOrWhiteSpace(description);
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, hasDescription ? 44 : 26));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.SizeChanged += (_, _) => SetRoundedRegion(card, ((Form1?)card.FindForm())?._selectedTheme == "gba" ? 0 : 16);
        card.Paint += (_, e) =>
        {
            var border = ((Form1?)card.FindForm())?._currentPalette.Border ?? Color.LightGray;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (((Form1?)card.FindForm())?._selectedTheme == "gba")
                ControlPaint.DrawBorder(e.Graphics, card.ClientRectangle, border, ButtonBorderStyle.Solid);
            else
            {
                using var path = RoundedRectangle(new Rectangle(0, 0, card.Width - 1, card.Height - 1), 16);
                using var pen = new Pen(border);
                e.Graphics.DrawPath(pen, path);
            }
        };
        header = new Panel { Dock = DockStyle.Fill, Tag = "card" };
        header.Controls.Add(new Label { Text = title, AutoSize = true, Location = new Point(0, 0), Font = new Font("Segoe UI", 10.5F, FontStyle.Bold), Tag = "title" });
        if (hasDescription)
            header.Controls.Add(new Label { Text = description, AutoSize = true, Location = new Point(1, 22), Font = new Font("Segoe UI", 8.5F), Tag = "muted" });
        body = new Panel { Dock = DockStyle.Fill, Tag = "card" };
        card.Controls.Add(header, 0, 0);
        card.Controls.Add(body, 0, 1);
        return card;
    }

    private ThemePalette _currentPalette => Palettes[_selectedTheme];

    private static void SetRoundedRegion(Control control, int radius)
    {
        if (control.Width <= 0 || control.Height <= 0) return;
        var previous = control.Region;
        if (radius <= 0)
        {
            control.Region = null;
            previous?.Dispose();
            return;
        }
        using var path = RoundedRectangle(new Rectangle(0, 0, control.Width, control.Height), radius);
        control.Region = new Region(path);
        previous?.Dispose();
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void ResizeSections()
    {
        var available = Math.Max(360, _root.ClientSize.Width - _root.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 2);
        var width = Math.Min(460, available);
        var sideSpace = Math.Max(0, (available - width) / 2);
        foreach (Control child in _root.Controls)
        {
            child.Width = width;
            child.Margin = new Padding(sideSpace, child.Margin.Top, sideSpace, child.Margin.Bottom);
        }
    }

    private void ApplyTheme(string theme)
    {
        if (!Palettes.ContainsKey(theme)) theme = "clean";
        _selectedTheme = theme;
        var palette = _currentPalette;
        ApplyThemeToControl(_root, palette);
        _transferView.SetTheme(theme);
        foreach (var pair in _themeButtons)
        {
            var active = pair.Key == theme;
            var button = pair.Value;
            var swatch = Palettes[pair.Key];
            button.BackColor = swatch.Surface;
            button.ForeColor = swatch.Text;
            button.FlatAppearance.BorderColor = active ? palette.Accent : swatch.Border;
            button.FlatAppearance.BorderSize = active ? 3 : 1;
        }
        _futureGameSlot.Invalidate();
        if (_server.IsRunning) _server.SetTheme(theme);
        SetRunning(_server.IsRunning);
        Invalidate(true);
    }

    private void ApplyThemeToControl(Control control, ThemePalette palette)
    {
        var role = control.Tag?.ToString() ?? string.Empty;
        if (role == "page") control.BackColor = palette.Background;
        else if (role == "card" || role == "surface-card" || role == "qrhost") control.BackColor = palette.Surface;
        else if (role == "future-slot") control.BackColor = palette.Background;
        else if (role.StartsWith("field", StringComparison.Ordinal)) control.BackColor = palette.Field;
        else if (role == "muted") { control.BackColor = Color.Transparent; control.ForeColor = palette.Muted; }
        else if (role == "title" || role == "brand") { control.BackColor = Color.Transparent; control.ForeColor = palette.Text; }
        else if (role == "status") { control.BackColor = palette.Field; control.ForeColor = palette.Muted; }
        else if (role == "primary")
        {
            var button = (Button)control;
            button.BackColor = palette.Accent;
            button.ForeColor = _selectedTheme == "studio" ? Color.FromArgb(16, 33, 29) : Color.White;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = palette.Accent;
            button.FlatAppearance.BorderSize = _selectedTheme == "gba" ? 2 : 0;
        }
        else if (role == "secondary")
        {
            var button = (Button)control;
            button.BackColor = palette.Surface;
            button.ForeColor = palette.Text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = palette.Border;
            button.FlatAppearance.BorderSize = _selectedTheme == "gba" ? 2 : 1;
        }

        if (control is Button shapedButton && (role == "primary" || role == "secondary" || role.StartsWith("theme:", StringComparison.Ordinal)))
            SetRoundedRegion(shapedButton, _selectedTheme == "gba" ? 0 : role.StartsWith("theme:", StringComparison.Ordinal) ? 6 : 12);
        if (role == "status") SetRoundedRegion(control, _selectedTheme == "gba" ? 0 : 13);
        if (role == "surface-card") SetRoundedRegion(control, _selectedTheme == "gba" ? 0 : 16);

        if (_selectedTheme == "gba" && control is not PictureBox)
            control.Font = new Font("Consolas", role == "brand" ? 20F : 9F, role is "brand" or "title" ? FontStyle.Bold : FontStyle.Regular);
        else if (control is not PictureBox)
        {
            var size = role == "brand" ? 23F : role == "title" ? 10.5F : 9F;
            var style = role is "brand" or "title" ? FontStyle.Bold : FontStyle.Regular;
            control.Font = new Font("Segoe UI", size, style);
        }

        foreach (Control child in control.Controls) ApplyThemeToControl(child, palette);
    }

    private void ChooseFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "選擇接收 iPhone 檔案的位置", UseDescriptionForTitle = true };
        if (Directory.Exists(_folderBox.Text)) dialog.SelectedPath = _folderBox.Text;
        if (dialog.ShowDialog(this) == DialogResult.OK) _folderBox.Text = dialog.SelectedPath;
    }

    private async Task StartServerAsync()
    {
        try
        {
            Directory.CreateDirectory(_folderBox.Text);
            var addresses = GetPrivateIpv4Addresses();
            if (addresses.Count == 0)
            {
                MessageBox.Show(this, "找不到可用的私人 IPv4 網路。請先讓電腦連上 Wi-Fi 或區域網路。", "無法啟動接收", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _pairingCode = Random.Shared.Next(0, 100_000_000).ToString("D8");
            var urls = await _server.StartAsync(_folderBox.Text, _pairingCode, addresses, _selectedTheme);
            _addressBox.Text = string.Join(Environment.NewLine, urls);
            _codeBox.Text = _pairingCode;
            RenderQrCode(urls[0] + "?code=" + Uri.EscapeDataString(_pairingCode));
            SetRunning(true);
            AddActivity(new ReceivedFile("接收服務已啟動", null, DateTime.Now, "掃描 QR Code 會自動連線。"));
        }
        catch (Exception ex)
        {
            await _server.StopAsync();
            SetRunning(false);
            MessageBox.Show(this, "接收服務無法啟動。請確認網路介面與防火牆設定。\r\n\r\n" + ex.Message, "啟動失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task StopServerAsync()
    {
        await _server.StopAsync();
        SetRunning(false);
        AddActivity(new ReceivedFile("接收服務已停止", null, DateTime.Now, "新的上傳已關閉。"));
    }

    private void SetRunning(bool running)
    {
        _startButton.Enabled = !running;
        _stopButton.Enabled = running;
        _chooseFolderButton.Enabled = !running;
        _folderBox.Enabled = !running;
        _copyAddressButton.Enabled = running;
        _statusLabel.Text = running ? "接收中" : "尚未啟動";
        _statusLabel.BackColor = running ? Color.FromArgb(225, 245, 237) : _currentPalette.Field;
        _statusLabel.ForeColor = running ? Color.FromArgb(39, 132, 98) : _currentPalette.Muted;
        SetTransferCaption(running, false);
        if (!running)
        {
            _addressBox.Clear();
            _codeBox.Clear();
            _pairingCode = string.Empty;
            ClearQrCode();
        }
    }

    private void SetTransferCaption(bool accepting, bool sending)
    {
        _transferView.SetTheme(_selectedTheme);
        _transferView.SetSending(sending);
        _transferHeading.Text = sending ? "傳送中" : accepting ? "等待傳送" : "接收待命";
        _transferDetail.Text = sending ? "檔案正傳送至這台電腦" : accepting ? "iPhone 已連線，可以選取檔案" : "啟動接收以等待資料";
    }

    private void RenderQrCode(string value)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(value, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new QRCode(data);
        var previous = _qrPreview.Image;
        _qrPreview.Image = qrCode.GetGraphic(4, Color.Black, Color.White, drawQuietZones: true);
        _qrPreview.Visible = true;
        _qrPlaceholder.Visible = false;
        previous?.Dispose();
    }

    private void ClearQrCode()
    {
        var previous = _qrPreview.Image;
        _qrPreview.Image = null;
        _qrPreview.Visible = false;
        _qrPlaceholder.Visible = true;
        previous?.Dispose();
    }

    private void CopyAddress()
    {
        var firstUrl = _addressBox.Lines.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
        if (!string.IsNullOrEmpty(firstUrl))
        {
            Clipboard.SetText(firstUrl);
            _networkHint.Text = "網址已複製。手機與電腦請保持在同一個私人 Wi-Fi。";
        }
    }

    private void AddActivity(ReceivedFile file)
    {
        var line = file.SizeDescription is null
            ? $"{file.Time:HH:mm:ss}  ·  {file.Name}  {file.Details}"
            : $"{file.Time:HH:mm:ss}  ·  {file.Name}  ({file.SizeDescription})";
        _activityList.Items.Insert(0, line);
        while (_activityList.Items.Count > 100) _activityList.Items.RemoveAt(_activityList.Items.Count - 1);
    }

    private static List<IPAddress> GetPrivateIpv4Addresses()
    {
        var found = new List<(int Priority, IPAddress Address)>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            var priority = adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 1;
            foreach (var entry in adapter.GetIPProperties().UnicastAddresses)
            {
                var address = entry.Address;
                if (address.AddressFamily != AddressFamily.InterNetwork || !IsPrivateIpv4(address)) continue;
                found.Add((priority, address));
            }
        }
        return found.OrderBy(item => item.Priority).Select(item => item.Address).Distinct().ToList();
    }

    private static bool IsPrivateIpv4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            || (bytes[0] == 192 && bytes[1] == 168);
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        await _server.StopAsync();
        ClearQrCode();
        base.OnFormClosing(e);
    }

    private sealed record ThemePalette(Color Background, Color Surface, Color Text, Color Muted, Color Border, Color Field, Color Accent);
}
