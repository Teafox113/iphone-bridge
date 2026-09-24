using System.Drawing.Drawing2D;

namespace IPhoneBridge.Windows;

internal sealed class MatrixRainView : Control
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 95 };
    private readonly Random _random = new();
    private readonly List<RainColumn> _columns = new();
    private string _theme = "clean";
    private bool _sending;
    private int _configuredWidth;
    private int _configuredHeight;

    public MatrixRainView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        _timer.Tick += (_, _) =>
        {
            AdvanceRain();
            Invalidate();
        };
        _timer.Start();
    }

    public void SetTheme(string theme)
    {
        if (_theme != theme)
        {
            _theme = theme;
            ConfigureColumns();
        }
        Invalidate();
    }

    public void SetSending(bool sending) => _sending = sending;

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ConfigureColumns();
    }

    private void ConfigureColumns()
    {
        if (Width <= 0 || Height <= 0) return;
        _configuredWidth = Width;
        _configuredHeight = Height;
        _columns.Clear();
        var spacing = _theme switch { "studio" => 10, "warm" => 16, "gba" => 12, _ => 18 };
        for (var x = 3; x < Width; x += spacing)
        {
            var speed = _theme switch { "studio" => _random.Next(3, 7), "warm" => _random.Next(1, 3), "gba" => _random.Next(2, 4), _ => _random.Next(2, 4) };
            _columns.Add(new RainColumn
            {
                X = x,
                HeadY = _random.Next(-Math.Max(1, Height / 3), Math.Max(2, Height)),
                Speed = speed,
                Length = _theme switch { "studio" => _random.Next(5, 9), "warm" => _random.Next(2, 5), "gba" => _random.Next(4, 7), _ => _random.Next(2, 5) },
                Seed = _random.Next(0, 64),
            });
        }
    }

    private void AdvanceRain()
    {
        if (_configuredWidth != Width || _configuredHeight != Height) ConfigureColumns();
        foreach (var column in _columns)
        {
            column.HeadY += column.Speed * (_sending ? 1.6F : 1F);
            if (column.HeadY - column.Length * 12 > Height)
            {
                column.HeadY = -_random.Next(20, Math.Max(40, Height + 80));
                column.Speed = _theme switch { "studio" => _random.Next(3, 7), "warm" => _random.Next(1, 3), "gba" => _random.Next(2, 4), _ => _random.Next(2, 4) };
                column.Length = _theme switch { "studio" => _random.Next(5, 9), "warm" => _random.Next(2, 5), "gba" => _random.Next(4, 7), _ => _random.Next(2, 5) };
                column.Seed = _random.Next(0, 64);
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var palette = _theme switch
        {
            "studio" => new RainPalette(Color.FromArgb(7, 16, 14), Color.FromArgb(67, 211, 165), Color.FromArgb(205, 255, 232), Color.FromArgb(45, 112, 91)),
            "warm" => new RainPalette(Color.FromArgb(248, 246, 241), Color.FromArgb(122, 139, 91), Color.FromArgb(164, 102, 59), Color.FromArgb(216, 206, 183)),
            "gba" => new RainPalette(Color.FromArgb(32, 44, 37), Color.FromArgb(119, 151, 89), Color.FromArgb(204, 229, 166), Color.FromArgb(84, 105, 72)),
            _ => new RainPalette(Color.FromArgb(242, 246, 252), Color.FromArgb(76, 119, 191), Color.FromArgb(33, 76, 155), Color.FromArgb(207, 220, 242)),
        };

        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.None;
        using (var background = new SolidBrush(palette.Background))
            graphics.FillRectangle(background, ClientRectangle);

        var glyphs = _theme switch { "studio" => "01ACEF+-<>/", "warm" => "01:;_+=.", _ => "01" };
        using var codeFont = new Font("Consolas", _theme == "studio" ? 9F : 8F, FontStyle.Regular);
        foreach (var column in _columns)
        for (var index = 0; index < column.Length; index++)
        {
            var glyphHeight = _theme == "gba" ? 11 : 10;
            var y = column.HeadY - index * glyphHeight;
            if (y < -glyphHeight || y > Height) continue;
            var alpha = index == 0 ? 255 : Math.Max(_theme == "warm" ? 55 : 38, 192 - index * (_theme == "warm" ? 32 : 24));
            var color = index == 0 ? palette.Leading : Color.FromArgb(alpha, palette.Rain);
            using var brush = new SolidBrush(color);
            var glyphIndex = Math.Abs(column.Seed + index + (int)(column.HeadY / 8)) % glyphs.Length;
            var glyph = glyphs[glyphIndex].ToString();
            if (_theme == "gba") DrawPixelDigit(graphics, glyph[0], column.X, (int)y, brush, 2);
            else graphics.DrawString(glyph, codeFont, brush, column.X, y);
        }

        if (_theme == "clean")
        {
            using var scan = new Pen(Color.FromArgb(80, palette.Edge));
            var scanY = (_configuredHeight <= 0 ? 0 : Environment.TickCount / 95 % _configuredHeight);
            graphics.DrawLine(scan, 1, scanY, Width - 2, scanY);
        }

        using var border = new Pen(palette.Edge, _theme == "gba" ? 2 : 1);
        graphics.DrawRectangle(border, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
    }

    private static void DrawPixelDigit(Graphics graphics, char digit, int x, int y, Brush brush, int pixel)
    {
        string[] pattern = digit == '0'
            ? new[] { "111", "101", "101", "101", "111" }
            : new[] { "010", "110", "010", "010", "111" };
        for (var row = 0; row < pattern.Length; row++)
        for (var column = 0; column < pattern[row].Length; column++)
            if (pattern[row][column] == '1') graphics.FillRectangle(brush, x + column * pixel, y + row * pixel, pixel, pixel);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }

    private sealed class RainColumn
    {
        public int X { get; set; }
        public float HeadY { get; set; }
        public int Speed { get; set; }
        public int Length { get; set; }
        public int Seed { get; set; }
    }

    private sealed record RainPalette(Color Background, Color Rain, Color Leading, Color Edge);
}
