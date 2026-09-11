using Microsoft.Web.WebView2.WinForms;
using PitWall.Core.Auth;
using PitWall.Server.Auth;

namespace PitWall.Desktop;

/// <summary>
/// Signs in to the user's Formula 1 account on formula1.com itself.
/// </summary>
/// <remarks>
/// The user types their credentials into Formula 1's own page; PitWall never reads the form.
/// Once signed in, Formula 1 sets a <c>login-session</c> cookie. PitWall reads that cookie
/// from its WebView2 profile, keeps only the subscription token inside it (sealed with DPAPI
/// by <see cref="TokenStore"/>), and uses it to unlock subscriber timing data.
/// </remarks>
internal sealed class LoginForm : Form
{
    private const string LoginUrl = "https://account.formula1.com/#/en/login";
    private const string CookieName = "login-session";

    private readonly WebView2 _view = new() { Dock = DockStyle.Fill };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Top,
        Height = 44,
        Padding = new Padding(16, 0, 16, 0),
        TextAlign = ContentAlignment.MiddleLeft,
        BackColor = Color.FromArgb(20, 22, 27),
        ForeColor = Color.FromArgb(230, 232, 236),
        Font = new Font("Segoe UI", 10.5f),
        Text = "Sign in with your F1 TV account. PitWall only keeps the subscription token Formula 1 issues.",
    };
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 1000 };
    private readonly TokenStore _tokens;
    private string? _lastCookie;

    public LoginForm(TokenStore tokens)
    {
        _tokens = tokens;
        Text = "PitWall · Connect F1 TV";
        Icon = AppIcon.Load();
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1040, 820);
        MinimumSize = new Size(520, 520);
        Controls.Add(_view);
        Controls.Add(_status);
        _poll.Tick += async (_, _) => await CheckCookieAsync();
    }

    public F1TvToken? Result { get; private set; }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        await WebViewHost.InitializeAsync(_view);
        _view.CoreWebView2.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            _view.CoreWebView2.Navigate(args.Uri);
        };
        _view.CoreWebView2.Navigate(LoginUrl);
        _poll.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _poll.Stop();
        _poll.Dispose();
        base.OnFormClosed(e);
    }

    private async Task CheckCookieAsync()
    {
        if (_view.CoreWebView2 is null || Result is not null)
        {
            return;
        }

        var cookies = await _view.CoreWebView2.CookieManager.GetCookiesAsync("https://account.formula1.com");
        var value = cookies.FirstOrDefault(c => c.Name == CookieName)?.Value;
        if (string.IsNullOrEmpty(value) || value == _lastCookie)
        {
            return;
        }
        _lastCookie = value;

        var token = _tokens.Save(value);
        if (token is null)
        {
            // Signed in, but the account has no subscription token (no F1 TV plan).
            _status.Text = "Signed in, but this account has no F1 TV subscription token. Timing still works without it.";
            return;
        }

        Result = token;
        _poll.Stop();
        _status.BackColor = token.IsActive ? Color.FromArgb(0, 120, 70) : Color.FromArgb(150, 90, 0);
        _status.Text = token.IsActive
            ? $"Connected: {token.Product ?? "F1 TV"} · valid until {token.Expires.LocalDateTime:ddd d MMM HH:mm}"
            : $"Signed in, but the subscription is '{token.SubscriptionStatus}'. Subscriber timing data needs an active plan.";
        await Task.Delay(1800);
        DialogResult = DialogResult.OK;
        Close();
    }
}
