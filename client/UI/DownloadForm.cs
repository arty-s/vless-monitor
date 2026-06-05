using VlessMonitor.Core;

namespace VlessMonitor.UI;

/// <summary>First-run dialog to download xray-core. Returns OK on success.</summary>
public class DownloadForm : Form
{
    private readonly Label _status;
    private readonly ProgressBar _bar;
    private readonly Button _btn;
    public bool Success { get; private set; }

    public DownloadForm()
    {
        Text = "VLESS Monitor — первый запуск";
        Size = new Size(440, 220);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false; MinimizeBox = false;
        BackColor = Theme.Bg; ForeColor = Theme.Fg;
        Font = new Font(Theme.FontFamily, 9.5f);
        Icon = IconFactory.Create(OverallStatus.Unknown);

        var info = new Label
        {
            Text = "Для работы нужен xray-core.\nСкачать сейчас с GitHub (~15 МБ)?",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(Theme.FontFamily, 11f),
            Dock = DockStyle.Top, Height = 70,
        };

        _status = new Label
        {
            Text = "", ForeColor = Theme.Blue,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top, Height = 24,
            Font = new Font(Theme.FontFamily, 9f),
        };

        _bar = new ProgressBar
        {
            Style = ProgressBarStyle.Marquee,
            Dock = DockStyle.Top, Height = 10, Visible = false,
            MarqueeAnimationSpeed = 30,
        };

        _btn = new Button
        {
            Text = "Скачать xray-core",
            Size = new Size(200, 38),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Bg3, ForeColor = Theme.Green,
            Cursor = Cursors.Hand,
            Anchor = AnchorStyles.None,
            Font = new Font(Theme.FontFamily, 11f),
        };
        _btn.FlatAppearance.BorderColor = Theme.Green;
        _btn.Click += OnDownload;

        var btnHost = new Panel { Dock = DockStyle.Fill };
        btnHost.Controls.Add(_btn);
        btnHost.Resize += (_, _) =>
            _btn.Location = new Point((btnHost.Width - _btn.Width) / 2,
                                      (btnHost.Height - _btn.Height) / 2);

        Controls.Add(btnHost);
        Controls.Add(_bar);
        Controls.Add(_status);
        Controls.Add(info);
    }

    private async void OnDownload(object? sender, EventArgs e)
    {
        _btn.Enabled = false;
        _bar.Visible = true;

        void Report(string msg)
        {
            if (InvokeRequired) BeginInvoke(() => _status.Text = msg);
            else _status.Text = msg;
        }

        Success = await XrayManager.DownloadXrayAsync(Report);

        _bar.Visible = false;
        if (Success)
        {
            _status.Text = "✓ Готово!";
            _status.ForeColor = Theme.Green;
            await Task.Delay(700);
            DialogResult = DialogResult.OK;
            Close();
        }
        else
        {
            _status.ForeColor = Theme.Red;
            _btn.Enabled = true;
        }
    }
}
