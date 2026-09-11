namespace PitWall.Desktop;

/// <summary>Chooses which display gets the live video and which gets the dashboard.</summary>
internal sealed class ScreenSetupForm : Form
{
    private static readonly Color Ground = Color.FromArgb(11, 12, 15);
    private static readonly Color Panel = Color.FromArgb(20, 22, 27);
    private static readonly Color Ink = Color.FromArgb(232, 234, 238);
    private static readonly Color Muted = Color.FromArgb(150, 156, 168);
    private static readonly Color Red = Color.FromArgb(225, 6, 0);
    private static readonly Color Cyan = Color.FromArgb(0, 180, 216);

    private readonly Screen[] _screens = Screen.AllScreens;
    private readonly ComboBox _video = Combo();
    private readonly ComboBox _dashboard = Combo();
    private readonly ComboBox _mode = Combo();
    private readonly CheckBox _fullscreen = new() { Text = "Open the video full screen", ForeColor = Ink, AutoSize = true };
    private readonly DoubleBufferedPanel _preview = new() { Height = 170, Dock = DockStyle.Top, BackColor = Panel };
    private readonly DesktopSettings _settings;

    public ScreenSetupForm(DesktopSettings settings)
    {
        _settings = settings;
        Text = "PitWall · Screens";
        Icon = AppIcon.Load();
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Ground;
        ForeColor = Ink;
        Font = new Font("Segoe UI", 10f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 470);
        Padding = new Padding(20);

        foreach (var (screen, i) in _screens.Select((s, i) => (s, i)))
        {
            var label = $"{i + 1} · {screen.Bounds.Width}×{screen.Bounds.Height}{(screen.Primary ? " · main" : "")}";
            _video.Items.Add(label);
            _dashboard.Items.Add(label);
        }
        _mode.Items.AddRange(["Microsoft Edge (recommended for F1 TV)", "Embedded window", "No video, dashboard only"]);

        var videoIndex = Array.FindIndex(_screens, s => s.DeviceName == settings.VideoDisplay);
        var dashIndex = Array.FindIndex(_screens, s => s.DeviceName == settings.DashboardDisplay);
        if (videoIndex < 0)
        {
            videoIndex = Math.Max(0, Array.FindIndex(_screens, s => s.Primary));
        }
        if (dashIndex < 0 || dashIndex == videoIndex)
        {
            dashIndex = _screens.Length > 1 ? Enumerable.Range(0, _screens.Length).First(i => i != videoIndex) : 0;
        }
        _video.SelectedIndex = videoIndex;
        _dashboard.SelectedIndex = dashIndex;
        _mode.SelectedIndex = (int)settings.Video;
        _fullscreen.Checked = settings.StartVideoFullscreen;

        _video.SelectedIndexChanged += (_, _) => _preview.Invalidate();
        _dashboard.SelectedIndexChanged += (_, _) => _preview.Invalidate();
        _mode.SelectedIndexChanged += (_, _) =>
        {
            _video.Enabled = _mode.SelectedIndex != (int)VideoMode.Off;
            _preview.Invalidate();
        };
        _preview.Paint += PaintPreview;

        var identify = Button("Identify screens", Panel);
        identify.Click += (_, _) => Identify();
        var save = Button("Save and start", Red);
        save.Click += (_, _) => Save();
        AcceptButton = save;

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(0, 16, 0, 0),
            BackColor = Ground,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(grid, "Live video on", _video);
        AddRow(grid, "Dashboard on", _dashboard);
        AddRow(grid, "Video player", _mode);
        grid.Controls.Add(new Label(), 0, 3);
        grid.Controls.Add(_fullscreen, 1, 3);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 52,
            BackColor = Ground,
        };
        buttons.Controls.Add(save);
        buttons.Controls.Add(identify);

        Controls.Add(grid);
        Controls.Add(buttons);
        Controls.Add(_preview);
    }

    private void Save()
    {
        _settings.VideoDisplay = _screens[_video.SelectedIndex].DeviceName;
        _settings.DashboardDisplay = _screens[_dashboard.SelectedIndex].DeviceName;
        _settings.Video = (VideoMode)_mode.SelectedIndex;
        _settings.StartVideoFullscreen = _fullscreen.Checked;
        _settings.Save();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void Identify()
    {
        foreach (var (screen, i) in _screens.Select((s, i) => (s, i)))
        {
            var badge = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                ShowInTaskbar = false,
                TopMost = true,
                BackColor = Red,
                Bounds = new Rectangle(screen.Bounds.X + 40, screen.Bounds.Y + 40, 220, 220),
            };
            badge.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = (i + 1).ToString(),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 96f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
            });
            var timer = new System.Windows.Forms.Timer { Interval = 2500 };
            timer.Tick += (_, _) =>
            {
                timer.Dispose();
                badge.Close();
            };
            badge.Show();
            timer.Start();
        }
    }

    private void PaintPreview(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var all = _screens.Select(s => s.Bounds).Aggregate(Rectangle.Union);
        var area = _preview.ClientRectangle;
        area.Inflate(-24, -20);
        var scale = Math.Min(area.Width / (double)all.Width, area.Height / (double)all.Height);
        var offsetX = area.X + (area.Width - all.Width * scale) / 2;
        var offsetY = area.Y + (area.Height - all.Height * scale) / 2;

        using var font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        for (var i = 0; i < _screens.Length; i++)
        {
            var b = _screens[i].Bounds;
            var rect = new RectangleF(
                (float)(offsetX + (b.X - all.X) * scale) + 3, (float)(offsetY + (b.Y - all.Y) * scale) + 3,
                (float)(b.Width * scale) - 6, (float)(b.Height * scale) - 6);
            var isVideo = i == _video.SelectedIndex && _mode.SelectedIndex != (int)VideoMode.Off;
            var isDash = i == _dashboard.SelectedIndex;
            var fill = isDash ? Cyan : isVideo ? Red : Color.FromArgb(40, 44, 52);
            using var brush = new SolidBrush(Color.FromArgb(isDash || isVideo ? 70 : 255, fill));
            using var pen = new Pen(fill, 2);
            g.FillRectangle(brush, rect);
            g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
            var text = $"{i + 1}\n{(isVideo && isDash ? "VIDEO + DASHBOARD" : isVideo ? "LIVE VIDEO" : isDash ? "DASHBOARD" : "unused")}";
            TextRenderer.DrawText(g, text, font, Rectangle.Round(rect), Ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }
    }

    private static void AddRow(TableLayoutPanel grid, string label, Control control)
    {
        var row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        grid.Controls.Add(new Label { Text = label, ForeColor = Muted, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        grid.Controls.Add(control, 1, row);
    }

    private static ComboBox Combo() => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Dock = DockStyle.Fill,
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(28, 31, 38),
        ForeColor = Ink,
    };

    private static Button Button(string text, Color back)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Padding = new Padding(14, 6, 14, 6),
            FlatStyle = FlatStyle.Flat,
            BackColor = back,
            ForeColor = Color.White,
            Margin = new Padding(8, 8, 0, 0),
        };
        button.FlatAppearance.BorderColor = back == Panel ? Color.FromArgb(60, 64, 74) : back;
        return button;
    }

    private sealed class DoubleBufferedPanel : System.Windows.Forms.Panel
    {
        public DoubleBufferedPanel() => DoubleBuffered = true;
    }
}
