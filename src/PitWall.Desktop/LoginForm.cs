using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using PitWall.Core.Auth;
using PitWall.Server.Auth;

namespace PitWall.Desktop;

internal enum LoginResult { Cancelled, Connected, NoSubscription }

/// <summary>
/// The F1 TV sign-in pop-up: Formula 1's own sign-in page, framed by PitWall's steps and status.
/// </summary>
/// <remarks>
/// The user types their credentials into formula1.com; PitWall never reads the page. Once signed
/// in, Formula 1 sets a <c>login-session</c> cookie. PitWall reads that cookie from its WebView2
/// profile (it can see HttpOnly cookies, the page's scripts are not involved), keeps only the
/// subscription token inside it, sealed with DPAPI by <see cref="TokenStore"/>, and closes.
/// </remarks>
internal sealed class LoginForm : Form
{
    private const string LoginUrl = "https://account.formula1.com/#/en/login";
    private const string CookieName = "login-session";
    private static readonly string[] CookieHosts = ["https://account.formula1.com", "https://www.formula1.com"];

    private static readonly Color Ground = Color.FromArgb(11, 12, 15);
    private static readonly Color PanelColor = Color.FromArgb(20, 22, 27);
    private static readonly Color Line = Color.FromArgb(45, 50, 60);
    private static readonly Color Ink = Color.FromArgb(236, 238, 242);
    private static readonly Color Muted = Color.FromArgb(150, 156, 168);
    private static readonly Color Red = Color.FromArgb(225, 6, 0);
    private static readonly Color Green = Color.FromArgb(43, 216, 111);
    private static readonly Color Amber = Color.FromArgb(255, 159, 28);

    private readonly TokenStore _tokens;
    private readonly WebView2 _view = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(11, 12, 15) };
    private readonly Panel _header = new DoubleBufferedPanel { Dock = DockStyle.Top, Height = 92, BackColor = PanelColor };
    private readonly Panel _footer = new() { Dock = DockStyle.Bottom, Height = 58, BackColor = PanelColor, Padding = new Padding(18, 0, 14, 0) };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Ink,
        Font = new Font("Segoe UI", 10f),
        Padding = new Padding(20, 0, 0, 0),
    };
    private readonly Button _retry = FooterButton("Retry", PanelColor);
    private readonly Button _cancel = FooterButton("Cancel", PanelColor);
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 1000 };

    private Color _statusColor = Muted;
    private int _step = 1;
    private string? _lastCookie;

    public LoginForm(TokenStore tokens)
    {
        _tokens = tokens;
        Text = "PitWall · Connect F1 TV";
        Icon = AppIcon.Load();
        BackColor = Ground;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(560, 600);
        KeyPreview = true;

        _header.Paint += PaintHeader;
        _footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Line);
            e.Graphics.DrawLine(pen, 0, 0, _footer.Width, 0);
            using var dot = new SolidBrush(_statusColor);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var d = Scale(9);
            e.Graphics.FillEllipse(dot, _footer.Padding.Left, (_footer.Height - d) / 2f, d, d);
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
            BackColor = PanelColor,
            Padding = new Padding(0, 11, 0, 0),
        };
        buttons.Controls.Add(_cancel);
        buttons.Controls.Add(_retry);
        _retry.Visible = false;
        _retry.Click += (_, _) => Navigate();
        _cancel.Click += (_, _) => Close();

        _footer.Controls.Add(_status);
        _footer.Controls.Add(buttons);

        Controls.Add(_view);
        Controls.Add(_footer);
        Controls.Add(_header);

        _poll.Tick += async (_, _) => await CheckCookieAsync();
        SetStatus("Loading Formula 1's sign-in page…", Muted);
    }

    public LoginResult Result { get; private set; } = LoginResult.Cancelled;

    public F1TvToken? Token { get; private set; }

    /// <summary>Centres the pop-up on the display the dashboard is on.</summary>
    public void CenterOn(Screen screen)
    {
        var area = screen.WorkingArea;
        var size = new Size(Math.Min(Scale(1040), area.Width - 80), Math.Min(Scale(860), area.Height - 60));
        Bounds = new Rectangle(area.X + (area.Width - size.Width) / 2, area.Y + (area.Height - size.Height) / 2, size.Width, size.Height);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        await WebViewHost.InitializeAsync(_view);
        var core = _view.CoreWebView2;
        core.NewWindowRequested += (_, args) =>
        {
            // Keep Formula 1's own pop-ups (consent, social sign-in) inside this window.
            args.Handled = true;
            core.Navigate(args.Uri);
        };
        core.NavigationCompleted += OnNavigationCompleted;
        Navigate();
        _poll.Start();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape)
        {
            Close();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _poll.Stop();
        _poll.Dispose();
        base.OnFormClosed(e);
    }

    private void Navigate()
    {
        _retry.Visible = false;
        SetStatus("Loading Formula 1's sign-in page…", Muted);
        _view.CoreWebView2?.Navigate(LoginUrl);
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_step > 1)
        {
            return;
        }
        if (!e.IsSuccess && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
        {
            SetStatus("Couldn't reach formula1.com. Check the connection and retry.", Amber);
            _retry.Visible = true;
            return;
        }
        if (_statusColor == Muted)
        {
            SetStatus("Sign in with your F1 TV account in the page above. Your password stays with Formula 1.", Ink);
        }
    }

    private async Task CheckCookieAsync()
    {
        if (_view.CoreWebView2 is null || _step == 3)
        {
            return;
        }

        string? value = null;
        foreach (var host in CookieHosts)
        {
            var cookies = await _view.CoreWebView2.CookieManager.GetCookiesAsync(host);
            value = cookies.FirstOrDefault(c => c.Name == CookieName)?.Value;
            if (!string.IsNullOrEmpty(value))
            {
                break;
            }
        }

        if (string.IsNullOrEmpty(value) || value == _lastCookie)
        {
            return;
        }
        _lastCookie = value;

        var token = F1TvToken.TryParse(value);
        if (token is null)
        {
            // Signed in, but Formula 1 issued no subscription token: an account without F1 TV.
            Result = LoginResult.NoSubscription;
            SetStatus("Signed in, but this account has no F1 TV subscription. Formula 1 now requires one for live timing.", Amber);
            _cancel.Text = "Close";
            return;
        }

        if (token.IsExpired(DateTimeOffset.UtcNow))
        {
            // A stale cookie from an earlier sign-in: wait for the fresh one.
            SetStatus("Your previous sign-in has expired. Sign in again in the page above.", Amber);
            return;
        }

        _step = 2;
        SetStatus("Signed in. Saving the subscription token…", Ink);
        Token = _tokens.Save(value);

        if (Token is null || !Token.IsActive)
        {
            _step = 1;
            Result = LoginResult.NoSubscription;
            SetStatus($"Signed in, but the F1 TV subscription is '{token.SubscriptionStatus}'. Subscriber data needs an active plan.", Amber);
            _cancel.Text = "Close";
            return;
        }

        _step = 3;
        Result = LoginResult.Connected;
        _poll.Stop();
        _cancel.Text = "Done";
        SetStatus($"Connected: {Token.Product ?? "F1 TV"} · valid until {Token.Expires.LocalDateTime:ddd d MMM, HH:mm}", Green);
        await Task.Delay(1600);
        if (!IsDisposed)
        {
            Close();
        }
    }

    private void SetStatus(string text, Color color)
    {
        _status.Text = text;
        _statusColor = color;
        _footer.Invalidate();
        _header.Invalidate();
    }

    private void PaintHeader(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var pad = Scale(22);
        var icon = Scale(36);
        g.DrawIcon(new Icon(AppIcon.Load(), icon, icon), new Rectangle(pad, (_header.Height - icon) / 2, icon, icon));

        using var title = new Font("Segoe UI Semibold", 15f);
        using var sub = new Font("Segoe UI", 9.5f);
        var textX = pad + icon + Scale(14);
        TextRenderer.DrawText(g, "Connect your F1 TV account", title, new Point(textX, Scale(20)), Ink);
        TextRenderer.DrawText(g, "Unlocks live car positions, telemetry, the championship projection and pit-lane times.",
            sub, new Point(textX, Scale(52)), Muted);

        // Step chips, right-aligned: 1 Sign in · 2 Save token · 3 Live data.
        string[] steps = ["Sign in", "Save token", "Live data"];
        using var stepFont = new Font("Segoe UI Semibold", 9f);
        var x = _header.Width - pad;
        for (var i = steps.Length - 1; i >= 0; i--)
        {
            var number = i + 1;
            var label = $"{number}  {steps[i]}";
            var size = TextRenderer.MeasureText(label, stepFont);
            var chip = new Rectangle(x - size.Width - Scale(18), (_header.Height - Scale(28)) / 2, size.Width + Scale(18), Scale(28));
            var done = number < _step || (_step == 3 && number == 3 && Result == LoginResult.Connected);
            var current = number == _step && !done;
            var fill = done ? Color.FromArgb(40, Green) : current ? Color.FromArgb(50, Red) : Color.FromArgb(28, 31, 38);
            var edge = done ? Green : current ? Red : Line;
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(edge))
            {
                g.FillRectangle(brush, chip);
                g.DrawRectangle(pen, chip);
            }
            TextRenderer.DrawText(g, label, stepFont, chip, done || current ? Ink : Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            x = chip.Left - Scale(8);
        }

        using var line = new Pen(Line);
        g.DrawLine(line, 0, _header.Height - 1, _header.Width, _header.Height - 1);
    }

    private int Scale(int px) => (int)Math.Round(px * DeviceDpi / 96f);

    private static Button FooterButton(string text, Color back)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Padding = new Padding(14, 4, 14, 4),
            Margin = new Padding(8, 0, 0, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = back,
            ForeColor = Ink,
            Font = new Font("Segoe UI Semibold", 9.5f),
        };
        button.FlatAppearance.BorderColor = Line;
        return button;
    }

    private sealed class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel() => DoubleBuffered = true;
    }
}
