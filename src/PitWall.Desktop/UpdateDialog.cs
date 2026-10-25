using PitWall.Server.Services;

namespace PitWall.Desktop;

/// <summary>
/// Shows what the new release contains and installs it on the user's say-so.
/// </summary>
/// <remarks>
/// Nothing is downloaded until <em>Download</em> is pressed and nothing is replaced until
/// <em>Install and restart</em> is, so an update never interrupts a race weekend by surprise.
/// </remarks>
internal sealed class UpdateDialog : Form
{
    private static readonly Color Ground = Color.FromArgb(11, 12, 15);
    private static readonly Color PanelColor = Color.FromArgb(20, 22, 27);
    private static readonly Color Line = Color.FromArgb(45, 50, 60);
    private static readonly Color Ink = Color.FromArgb(236, 238, 242);
    private static readonly Color Muted = Color.FromArgb(150, 156, 168);
    private static readonly Color Red = Color.FromArgb(225, 6, 0);
    private static readonly Color Green = Color.FromArgb(43, 216, 111);

    private readonly UpdateService _updates;
    private readonly Label _heading = new()
    {
        Dock = DockStyle.Top,
        Height = 40,
        ForeColor = Ink,
        Font = new Font("Segoe UI Semibold", 14f),
        TextAlign = ContentAlignment.MiddleLeft,
    };
    private readonly Label _detail = new()
    {
        Dock = DockStyle.Top,
        Height = 26,
        ForeColor = Muted,
        Font = new Font("Segoe UI", 9.5f),
        TextAlign = ContentAlignment.MiddleLeft,
    };
    private readonly TextBox _notes = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        BorderStyle = BorderStyle.None,
        BackColor = PanelColor,
        ForeColor = Ink,
        Font = new Font("Segoe UI", 9.5f),
    };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Bottom, Height = 6, Visible = false, Style = ProgressBarStyle.Continuous };
    private readonly Button _action = Button("Check again", Red);
    private readonly Button _close = Button("Close", PanelColor);
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 400 };

    public UpdateDialog(UpdateService updates)
    {
        _updates = updates;
        Text = "PitWall · Update";
        Icon = AppIcon.Load();
        BackColor = Ground;
        ForeColor = Ink;
        Font = new Font("Segoe UI", 10f);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(560, 420);
        Padding = new Padding(22, 18, 22, 16);
        KeyPreview = true;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 54,
            BackColor = Ground,
            Padding = new Padding(0, 12, 0, 0),
        };
        buttons.Controls.Add(_close);
        buttons.Controls.Add(_action);
        _close.Click += (_, _) => Close();
        _action.Click += async (_, _) => await ActAsync();

        var notesPanel = new Panel { Dock = DockStyle.Fill, BackColor = PanelColor, Padding = new Padding(12) };
        notesPanel.Controls.Add(_notes);

        Controls.Add(notesPanel);
        Controls.Add(buttons);
        Controls.Add(_progress);
        Controls.Add(_detail);
        Controls.Add(_heading);

        _poll.Tick += (_, _) => Render();
    }

    /// <summary>Set when the user chose to install: the caller exits so the swap can happen.</summary>
    public bool Installing { get; private set; }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Render();
        _poll.Start();
        if (_updates.Status.State is UpdateState.Unknown or UpdateState.Failed)
        {
            await _updates.CheckAsync(force: true, CancellationToken.None);
            Render();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _poll.Stop();
        _poll.Dispose();
        base.OnFormClosed(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape)
        {
            Close();
        }
    }

    private async Task ActAsync()
    {
        switch (_updates.Status.State)
        {
            case UpdateState.Available:
                _action.Enabled = false;
                await _updates.DownloadAsync(CancellationToken.None);
                _action.Enabled = true;
                Render();
                break;

            case UpdateState.Ready:
                if (_updates.StartInstall())
                {
                    Installing = true;
                    DialogResult = DialogResult.OK;
                    Close();
                }
                else
                {
                    Render();
                }
                break;

            default:
                _action.Enabled = false;
                await _updates.CheckAsync(force: true, CancellationToken.None);
                _action.Enabled = true;
                Render();
                break;
        }
    }

    private void Render()
    {
        var s = _updates.Status;
        _progress.Visible = s.State == UpdateState.Downloading;
        _progress.Value = Math.Clamp(s.Progress, 0, 100);

        switch (s.State)
        {
            case UpdateState.Checking:
                _heading.Text = "Checking for updates…";
                _detail.Text = $"You have {s.Current}.";
                _action.Text = "Check again";
                break;

            case UpdateState.Available:
                _heading.Text = $"PitWall {s.Latest} is available";
                _detail.Text = $"You have {s.Current}" + (s.Published is { } p ? $" · released {p.LocalDateTime:d MMM yyyy}" : "");
                _action.Text = "Download";
                break;

            case UpdateState.Downloading:
                _heading.Text = $"Downloading {s.Latest}…";
                _detail.Text = $"{s.Progress}%";
                _action.Text = "Download";
                break;

            case UpdateState.Ready:
                _heading.Text = $"PitWall {s.Latest} is ready";
                _detail.Text = "Checksum verified. Installing closes PitWall and opens the new version.";
                _action.Text = "Install and restart";
                break;

            case UpdateState.UpToDate:
                _heading.Text = "PitWall is up to date";
                _detail.Text = $"You have {s.Current}, the newest release.";
                _action.Text = "Check again";
                break;

            case UpdateState.NotSupported:
                _heading.Text = "Updates are not available here";
                _detail.Text = s.Error ?? "This build cannot update itself.";
                _action.Text = "Check again";
                break;

            default:
                _heading.Text = "Could not check for updates";
                _detail.Text = s.Error ?? "Something went wrong.";
                _action.Text = "Try again";
                break;
        }

        _detail.ForeColor = s.State == UpdateState.Ready ? Green : Muted;
        _notes.Text = s.Notes ?? (s.State == UpdateState.UpToDate
            ? "Nothing to install."
            : "Release notes appear here once a release has been found.");
        _action.Enabled = s.State != UpdateState.Downloading && s.State != UpdateState.Checking;
    }

    private static Button Button(string text, Color back)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Padding = new Padding(16, 5, 16, 5),
            Margin = new Padding(8, 0, 0, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = back,
            ForeColor = back == PanelColor ? Ink : Color.White,
            Font = new Font("Segoe UI Semibold", 9.5f),
        };
        button.FlatAppearance.BorderColor = back == PanelColor ? Line : back;
        return button;
    }
}
