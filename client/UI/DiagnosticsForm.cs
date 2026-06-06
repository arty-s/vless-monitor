using VlessMonitor.Core;

namespace VlessMonitor.UI;

/// <summary>Small dialog to build a shareable diagnostic bundle.</summary>
public class DiagnosticsForm : Form
{
    private readonly Config _cfg;
    private readonly MonitorState? _state;
    private readonly CheckBox _includeIp;
    private readonly Label _status;
    private readonly FluentButton _create;

    public DiagnosticsForm(Config cfg, MonitorState? state)
    {
        _cfg = cfg;
        _state = state;

        Theme.Refresh();
        Text = "Сбор диагностики";
        Size = new Size(520, 320);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false; MinimizeBox = false;
        BackColor = Theme.Bg; ForeColor = Theme.Fg;
        Font = new Font(Theme.UiFont, 9.5f);
        Icon = IconFactory.Create(OverallStatus.Unknown);

        var info = new Label
        {
            Dock = DockStyle.Top, Height = 96,
            Padding = new Padding(20, 18, 20, 0),
            Font = new Font(Theme.UiFont, 10f),
            Text = "Будет создан zip-архив на рабочем столе: логи, обезличенный конфиг, "
                 + "сведения о системе и последнее состояние проверок.\n\n"
                 + "Секреты (UUID, ключи, токены) вырезаются автоматически — архив безопасно "
                 + "переслать для разбора проблемы.",
        };

        _includeIp = new CheckBox
        {
            Dock = DockStyle.Top, Height = 44,
            Padding = new Padding(20, 0, 20, 0),
            Text = "Включить реальный адрес сервера (только доверенному получателю)",
            ForeColor = Theme.Yellow,
            Checked = false,
        };

        _status = new Label
        {
            Dock = DockStyle.Top, Height = 28,
            Padding = new Padding(20, 0, 20, 0),
            ForeColor = Theme.FgDim,
            Text = "По умолчанию адрес сервера маскируется (185.121.x.x).",
        };

        var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Theme.Bg };
        _create = new FluentButton { Text = "Создать архив", Width = 150, Accent = true };
        _create.Click += OnCreate;
        var cancel = new FluentButton { Text = "Закрыть", Width = 110 };
        cancel.Click += (_, _) => Close();
        btnPanel.Controls.Add(_create);
        btnPanel.Controls.Add(cancel);
        btnPanel.Resize += (_, _) =>
        {
            _create.Location = new Point(btnPanel.Width - _create.Width - 20, 14);
            cancel.Location = new Point(_create.Left - cancel.Width - 8, 14);
        };

        Controls.Add(_status);
        Controls.Add(_includeIp);
        Controls.Add(info);
        Controls.Add(btnPanel);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.UseDarkTitleBar(Handle, Theme.Current.IsDark);
        Native.UseRoundedCorners(Handle);
    }

    private void OnCreate(object? sender, EventArgs e)
    {
        _create.Enabled = false;
        _status.ForeColor = Theme.FgDim;
        _status.Text = "Создаю архив…";
        try
        {
            var path = Diagnostics.CreateBundle(_cfg, _state, _includeIp.Checked);
            _status.ForeColor = Theme.Green;
            _status.Text = "✓ Готово: " + Path.GetFileName(path);
            Diagnostics.OpenInExplorer(path);
        }
        catch (Exception ex)
        {
            _status.ForeColor = Theme.Red;
            _status.Text = "Ошибка: " + ex.Message;
            Logger.Error("Сбой при создании bundle", ex);
        }
        finally { _create.Enabled = true; }
    }
}
