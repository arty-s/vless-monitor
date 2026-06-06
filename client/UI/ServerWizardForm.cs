using VlessMonitor.Core;

namespace VlessMonitor.UI;

/// <summary>
/// Install / reinstall / health-check / remove the optional probe server on a VPS.
/// SSH password or key; credentials saved encrypted (DPAPI).
/// </summary>
public class ServerWizardForm : Form
{
    private readonly Config _cfg;

    private readonly TextBox _host, _user, _password, _keyPath;
    private readonly NumericUpDown _port;
    private readonly RadioButton _authPass, _authKey;
    private readonly CheckBox _remember;
    private readonly TextBox _log;
    private readonly FluentButton _install, _check, _remove;

    public ServerWizardForm(Config cfg)
    {
        _cfg = cfg;
        Theme.Refresh();
        Text = "Серверный модуль (probe)";
        Size = new Size(720, 640);
        MinimumSize = new Size(640, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg; ForeColor = Theme.Fg;
        Font = new Font(Theme.UiFont, 9.5f);
        Icon = IconFactory.Create(OverallStatus.Unknown);

        var p = Theme.Current;
        int y = 16;
        Label L(string t, int yy) => new() { Text = t, ForeColor = p.TextSecondary, AutoSize = true, Location = new Point(18, yy + 3), Font = new Font(Theme.UiFont, 9f) };
        TextBox T(int yy, int w, bool pass = false) => new() { Location = new Point(150, yy), Width = w, BackColor = p.ControlFill, ForeColor = p.TextPrimary, BorderStyle = BorderStyle.FixedSingle, UseSystemPasswordChar = pass };

        Controls.Add(L("Адрес сервера:", y));
        _host = T(y, 220); Controls.Add(_host);
        Controls.Add(new Label { Text = "Порт:", ForeColor = p.TextSecondary, AutoSize = true, Location = new Point(390, y + 3) });
        _port = new NumericUpDown { Location = new Point(440, y), Width = 70, Minimum = 1, Maximum = 65535, Value = 22, BackColor = p.ControlFill, ForeColor = p.TextPrimary, BorderStyle = BorderStyle.FixedSingle };
        Controls.Add(_port);
        y += 36;

        Controls.Add(L("Пользователь:", y));
        _user = T(y, 220); _user.Text = "root"; Controls.Add(_user);
        y += 36;

        _authPass = new RadioButton { Text = "Пароль", ForeColor = p.TextPrimary, AutoSize = true, Location = new Point(150, y), Checked = true };
        _authKey = new RadioButton { Text = "SSH-ключ", ForeColor = p.TextPrimary, AutoSize = true, Location = new Point(250, y) };
        _authPass.CheckedChanged += (_, _) => UpdateAuthVis();
        Controls.Add(_authPass); Controls.Add(_authKey);
        y += 32;

        Controls.Add(L("Пароль:", y));
        _password = T(y, 220, pass: true); Controls.Add(_password);
        y += 36;

        Controls.Add(L("Файл ключа:", y));
        _keyPath = T(y, 220); Controls.Add(_keyPath);
        var browse = new FluentButton { Text = "Обзор…", Width = 80, Height = 26, Location = new Point(380, y) };
        browse.Click += (_, _) => { using var d = new OpenFileDialog(); if (d.ShowDialog() == DialogResult.OK) _keyPath.Text = d.FileName; };
        Controls.Add(browse);
        y += 36;

        _remember = new CheckBox { Text = "Запомнить доступ (шифруется DPAPI)", ForeColor = p.TextPrimary, AutoSize = true, Location = new Point(150, y), Checked = true };
        Controls.Add(_remember);
        y += 34;

        _install = new FluentButton { Text = "Установить / Переустановить", Width = 220, Height = 32, Location = new Point(18, y), Accent = true };
        _install.Click += (_, _) => RunOp(OpInstall);
        _check = new FluentButton { Text = "Проверить состояние", Width = 170, Height = 32, Location = new Point(248, y) };
        _check.Click += (_, _) => RunOp(OpCheck);
        _remove = new FluentButton { Text = "Удалить", Width = 110, Height = 32, Location = new Point(428, y) };
        _remove.Click += (_, _) => RunOp(OpRemove);
        Controls.Add(_install); Controls.Add(_check); Controls.Add(_remove);
        y += 44;

        _log = new TextBox
        {
            Location = new Point(18, y), Width = ClientSize.Width - 36, Height = ClientSize.Height - y - 16,
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            BackColor = p.CardBg, ForeColor = p.TextPrimary, BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9f),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
        };
        Controls.Add(_log);

        Prefill();
        UpdateAuthVis();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.UseDarkTitleBar(Handle, Theme.Current.IsDark);
        Native.UseRoundedCorners(Handle);
    }

    private void Prefill()
    {
        var c = ServerCredentials.Load();
        if (c != null)
        {
            _host.Text = c.Host; _port.Value = c.Port; _user.Text = c.User;
            _authPass.Checked = c.Auth == SshAuth.Password;
            _authKey.Checked = c.Auth == SshAuth.Key;
            _password.Text = c.Password; _keyPath.Text = c.KeyPath;
        }
        else if (!string.IsNullOrEmpty(_cfg.VpsHost))
        {
            _host.Text = _cfg.VpsHost;
        }
    }

    private void UpdateAuthVis()
    {
        _password.Enabled = _authPass.Checked;
        _keyPath.Enabled = _authKey.Checked;
    }

    private void Log(string s)
    {
        if (_log.InvokeRequired) { _log.BeginInvoke(() => Log(s)); return; }
        _log.AppendText(s + Environment.NewLine);
    }

    private ServerCredentials BuildCred() => new()
    {
        Host = _host.Text.Trim(),
        Port = (int)_port.Value,
        User = _user.Text.Trim(),
        Auth = _authKey.Checked ? SshAuth.Key : SshAuth.Password,
        Password = _password.Text,
        KeyPath = _keyPath.Text.Trim(),
    };

    private void RunOp(Action<ServerCredentials> op)
    {
        if (_host.Text.Trim().Length == 0) { Log("Укажите адрес сервера."); return; }
        SetBusy(true);
        var cred = BuildCred();
        System.Threading.Tasks.Task.Run(() =>
        {
            try { op(cred); }
            catch (Exception ex) { Log("✗ Ошибка: " + ex.Message); Logger.Error("Server wizard op", ex); }
            finally { if (!IsDisposed) BeginInvoke(() => SetBusy(false)); }
        });
    }

    private void SetBusy(bool busy)
    {
        _install.Enabled = _check.Enabled = _remove.Enabled = !busy;
    }

    private void OpInstall(ServerCredentials cred)
    {
        using var inst = new ServerInstaller(cred, Log);
        inst.Connect();

        // Host-key TOFU
        var saved = ServerCredentials.Load();
        if (saved != null && saved.HostKeyFingerprint.Length > 0 &&
            inst.HostKeyFingerprint != null && saved.HostKeyFingerprint != inst.HostKeyFingerprint)
        {
            var ok = MessageBox.Show(
                "Отпечаток ключа сервера ИЗМЕНИЛСЯ!\n\nСохранён: " + saved.HostKeyFingerprint +
                "\nСейчас:  " + inst.HostKeyFingerprint +
                "\n\nЭто может означать атаку (MITM). Продолжить?",
                "Внимание", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (ok != DialogResult.Yes) { Log("Отменено пользователем."); return; }
        }

        var si = inst.Detect();
        var secret = ServerInstaller.GenerateSecret();
        var res = inst.Install(si, _cfg.ProbeServerPort, secret);

        if (!res.Success) { Log("✗ Установка не удалась."); return; }

        // External reachability — catches the cloud firewall
        if (!res.TunnelOnly)
        {
            Log($"Проверяю доступность {cred.Host}:{res.Port} снаружи…");
            bool reachable = ServerInstaller.VerifyExternalAsync(cred.Host, res.Port, res.Secret)
                .GetAwaiter().GetResult();
            res.PublicReachable = reachable;
            Log(reachable
                ? "✓ Порт доступен снаружи — полная диагностика включена."
                : "⚠ Порт НЕ доступен снаружи. Скорее всего блокирует файрвол хостинга " +
                  "(security group). Мониторинг через туннель всё равно работает; чтобы включить " +
                  "прямую диагностику — откройте порт в панели провайдера.");
        }

        // Persist results to client config
        _cfg.ProbeServerPort = res.Port;
        _cfg.ProbeSecret = res.Secret;
        _cfg.Save();
        Log($"Сохранено в настройках: порт {res.Port}, секрет обновлён.");

        // Save credentials (encrypted) if requested
        if (_remember.Checked)
        {
            cred.HostKeyFingerprint = inst.HostKeyFingerprint ?? "";
            cred.Save();
            Log("Доступ к серверу сохранён (DPAPI).");
        }
        else ServerCredentials.Delete();

        Log("✓ Готово. Изменения применятся при следующем цикле проверок.");
    }

    private void OpCheck(ServerCredentials cred)
    {
        using var inst = new ServerInstaller(cred, Log);
        inst.Connect();
        var si = inst.Detect();
        var state = inst.HealthCheck(si);
        Log($"Состояние probe-сервера: {state}");
        if (state != "active")
        {
            Log("Сервис не активен — пробую перезапустить…");
            bool ok = inst.Repair(si);
            if (ok) Log("✓ probe-сервер снова активен.");
            else
            {
                if (BeginInvoke(() =>
                {
                    var r = MessageBox.Show(
                        "Похоже, серверный probe-модуль сломан или удалён. Переустановить?",
                        "probe-сервер недоступен", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (r == DialogResult.Yes) RunOp(OpInstall);
                }) == null) { }
            }
        }
    }

    private void OpRemove(ServerCredentials cred)
    {
        using var inst = new ServerInstaller(cred, Log);
        inst.Connect();
        var si = inst.Detect();
        inst.Uninstall(si);
        Log("✓ probe-сервер удалён с сервера.");
    }
}
