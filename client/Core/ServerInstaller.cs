using System.Text;
using Renci.SshNet;

namespace VlessMonitor.Core;

public class ServerInfo
{
    public string Distro = "";
    public string PkgManager = "";   // apt | dnf | yum | apk | ""
    public bool IsRoot;
    public bool HasSudo;
    public bool SudoNoPass;
    public string Firewall = "";      // ufw | firewalld | iptables | ""
    public bool HasSystemd;
    public bool HasSystemdUser;
    public bool HasPython3;
    public string UserHome = "";

    public string Tier =>
        IsRoot ? "root" : (HasSudo ? "sudo" : "user");
}

public class InstallResult
{
    public bool Success;
    public int Port;
    public string Secret = "";
    public bool PublicReachable;   // verified from the client side
    public bool TunnelOnly;        // bound to localhost (firewall/no-root)
    public string Message = "";
}

/// <summary>
/// Installs / repairs / removes the probe server on a VPS over SSH.
/// Handles three privilege tiers and falls back to a localhost-only
/// (tunnel-reachable) install when a public port can't be opened.
/// </summary>
public class ServerInstaller : IDisposable
{
    private readonly ServerCredentials _cred;
    private readonly Action<string> _log;
    private SshClient? _ssh;
    private SftpClient? _sftp;

    public string? HostKeyFingerprint { get; private set; }

    public ServerInstaller(ServerCredentials cred, Action<string> log)
    {
        _cred = cred;
        _log = log;
    }

    private ConnectionInfo BuildConnection()
    {
        if (_cred.Auth == SshAuth.Key && File.Exists(_cred.KeyPath))
        {
            var key = string.IsNullOrEmpty(_cred.KeyPassphrase)
                ? new PrivateKeyFile(_cred.KeyPath)
                : new PrivateKeyFile(_cred.KeyPath, _cred.KeyPassphrase);
            return new ConnectionInfo(_cred.Host, _cred.Port, _cred.User,
                new PrivateKeyAuthenticationMethod(_cred.User, key));
        }
        return new ConnectionInfo(_cred.Host, _cred.Port, _cred.User,
            new PasswordAuthenticationMethod(_cred.User, _cred.Password));
    }

    public void Connect()
    {
        var info = BuildConnection();
        info.Timeout = TimeSpan.FromSeconds(15);
        _ssh = new SshClient(info);
        _ssh.HostKeyReceived += (_, e) =>
        {
            HostKeyFingerprint = Convert.ToHexString(e.FingerPrint).Replace("-", ":");
            e.CanTrust = true; // TOFU — surfaced to the user for confirmation
        };
        _log($"Подключение к {_cred.User}@{_cred.Host}:{_cred.Port} …");
        _ssh.Connect();
        _log("✓ SSH-соединение установлено");
        if (HostKeyFingerprint != null)
            _log($"Отпечаток ключа сервера: {HostKeyFingerprint}");
    }

    private (int code, string outp) Run(string cmd, bool log = true)
    {
        using var c = _ssh!.CreateCommand(cmd);
        c.CommandTimeout = TimeSpan.FromMinutes(3);
        string outp = c.Execute();
        string err = c.Error;
        string combined = (outp + (err.Length > 0 ? "\n" + err : "")).TrimEnd();
        if (log && combined.Length > 0)
            foreach (var line in combined.Split('\n'))
                _log("  " + line);
        return (c.ExitStatus ?? -1, combined);
    }

    public ServerInfo Detect()
    {
        _log("Определяю окружение сервера…");
        var si = new ServerInfo();

        si.IsRoot = Run("id -u", false).outp.Trim() == "0";
        si.UserHome = Run("echo $HOME", false).outp.Trim();

        var os = Run("cat /etc/os-release 2>/dev/null | grep -E '^(ID|PRETTY_NAME)=' || true", false).outp;
        foreach (var line in os.Split('\n'))
            if (line.StartsWith("PRETTY_NAME=")) si.Distro = line[12..].Trim('"', ' ');

        foreach (var pm in new[] { "apt-get", "dnf", "yum", "apk" })
            if (Run($"command -v {pm} >/dev/null 2>&1 && echo yes || true", false).outp.Trim() == "yes")
            { si.PkgManager = pm; break; }

        si.HasSudo = Run("command -v sudo >/dev/null 2>&1 && echo yes || true", false).outp.Trim() == "yes";
        if (si.HasSudo && !si.IsRoot)
            si.SudoNoPass = Run("sudo -n true 2>/dev/null && echo yes || true", false).outp.Trim() == "yes";

        si.HasSystemd = Run("command -v systemctl >/dev/null 2>&1 && echo yes || true", false).outp.Trim() == "yes";
        si.HasPython3 = Run("command -v python3 >/dev/null 2>&1 && echo yes || true", false).outp.Trim() == "yes";

        foreach (var fw in new[] { "ufw", "firewall-cmd", "iptables" })
            if (Run($"command -v {fw} >/dev/null 2>&1 && echo yes || true", false).outp.Trim() == "yes")
            { si.Firewall = fw; break; }

        _log($"  Дистрибутив: {(si.Distro.Length > 0 ? si.Distro : "неизвестно")}");
        _log($"  Пакетный менеджер: {(si.PkgManager.Length > 0 ? si.PkgManager : "нет")}");
        _log($"  Привилегии: {si.Tier}" + (si.HasSudo && !si.IsRoot ? $" (sudo {(si.SudoNoPass ? "без пароля" : "с паролем")})" : ""));
        _log($"  systemd: {(si.HasSystemd ? "да" : "нет")}, python3: {(si.HasPython3 ? "да" : "нет")}, firewall: {(si.Firewall.Length > 0 ? si.Firewall : "нет")}");
        return si;
    }

    /// <summary>Prefix for privileged commands depending on the tier.</summary>
    private string Sudo(ServerInfo si)
    {
        if (si.IsRoot) return "";
        if (si.SudoNoPass) return "sudo ";
        // password sudo: feed the SSH password (commonly the same)
        return $"echo '{Esc(_cred.Password)}' | sudo -S ";
    }

    private static string Esc(string s) => s.Replace("'", "'\\''");

    private static string ReadEmbeddedProbe()
    {
        var asm = typeof(ServerInstaller).Assembly;
        var name = asm.GetManifestResourceNames()
            .First(n => n.EndsWith("probe_server.py", StringComparison.OrdinalIgnoreCase));
        using var s = asm.GetManifestResourceStream(name)!;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public InstallResult Install(ServerInfo si, int desiredPort, string secret)
    {
        var res = new InstallResult { Port = desiredPort, Secret = secret };
        bool privileged = si.IsRoot || si.HasSudo;
        string dir = privileged ? "/opt/vless-monitor" : $"{si.UserHome}/.local/vless-monitor";
        string sudo = Sudo(si);

        // 1. python3
        if (!si.HasPython3)
        {
            _log("Устанавливаю python3…");
            string inst = si.PkgManager switch
            {
                "apt-get" => $"{sudo}apt-get update -y && {sudo}apt-get install -y python3",
                "dnf" => $"{sudo}dnf install -y python3",
                "yum" => $"{sudo}yum install -y python3",
                "apk" => $"{sudo}apk add --no-cache python3",
                _ => "true",
            };
            if (Run(inst).code != 0)
            { res.Message = "Не удалось установить python3"; return res; }
        }

        // 2. upload probe_server.py
        _log($"Создаю каталог {dir} и загружаю probe_server.py…");
        Run($"{sudo}mkdir -p {dir}");
        if (privileged && !si.IsRoot)
            Run($"{sudo}chown $USER {dir}"); // so SFTP (as user) can write
        UploadFile(ReadEmbeddedProbe(), $"{dir}/probe_server.py", si, sudo, dir);

        // 2b. stop any previous instance of OUR service first, so its port frees
        //     up and a reinstall reuses the same port instead of drifting.
        Run($"{sudo}systemctl stop vless-probe 2>/dev/null || systemctl --user stop vless-probe 2>/dev/null || true", false);
        Run("pkill -f probe_server.py 2>/dev/null || true", false);
        System.Threading.Thread.Sleep(500);

        // 3. choose a free port near the desired one
        int port = PickFreePort(desiredPort);
        res.Port = port;
        _log($"Выбран порт {port}");

        // 4. firewall (privileged only)
        bool firewallOpened = false;
        if (privileged && si.Firewall.Length > 0)
        {
            _log($"Открываю порт {port} в {si.Firewall}…");
            string fw = si.Firewall switch
            {
                "ufw" => $"{sudo}ufw allow {port}/tcp",
                "firewall-cmd" => $"{sudo}firewall-cmd --add-port={port}/tcp --permanent && {sudo}firewall-cmd --reload",
                "iptables" => $"{sudo}iptables -I INPUT -p tcp --dport {port} -j ACCEPT",
                _ => "true",
            };
            firewallOpened = Run(fw).code == 0;
        }

        // 5. decide bind address: public if privileged, else localhost (tunnel-only)
        string bind = privileged ? "0.0.0.0" : "127.0.0.1";
        res.TunnelOnly = bind == "127.0.0.1";

        // 6. service
        if (privileged && si.HasSystemd)
            InstallSystemdSystem(dir, port, secret, bind, sudo);
        else if (si.HasSystemdUser || Run("systemctl --user is-system-running 2>/dev/null; echo done", false).outp.Contains("done"))
            InstallSystemdUser(si, dir, port, secret);
        else
            InstallNohup(dir, port, secret, bind);

        // 7. local health check on the server
        System.Threading.Thread.Sleep(1500);
        var health = Run($"curl -s -o /dev/null -w '%{{http_code}}' -H 'X-Token: {secret}' http://127.0.0.1:{port}/health || echo fail", false);
        bool localUp = health.outp.Trim() == "200";
        _log(localUp ? "✓ probe-сервер отвечает локально" : "⚠ probe-сервер не отвечает локально");

        res.Success = localUp;
        res.PublicReachable = false; // verified later by the client
        res.Message = !localUp ? "Сервис не поднялся — см. лог"
            : res.TunnelOnly ? "Установлено в режиме localhost (доступ через туннель)"
            : firewallOpened ? "Установлено, порт открыт в файрволле ОС"
            : "Установлено (файрвол не настраивался)";
        _log("Готово: " + res.Message);
        return res;
    }

    private void UploadFile(string content, string remotePath, ServerInfo si, string sudo, string dir)
    {
        try
        {
            _sftp ??= new SftpClient(BuildConnection());
            if (!_sftp.IsConnected) _sftp.Connect();
            // write to a temp path we can access, then move with privilege if needed
            string tmp = $"/tmp/probe_{Guid.NewGuid():N}.py";
            _sftp.WriteAllText(tmp, content);
            Run($"{sudo}mv {tmp} {remotePath} && {sudo}chmod 644 {remotePath}");
        }
        catch (Exception ex)
        {
            _log("SFTP не сработал, пишу через ssh heredoc… (" + ex.Message + ")");
            // fallback: base64 over ssh
            var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));
            Run($"echo '{b64}' | base64 -d | {sudo}tee {remotePath} >/dev/null");
        }
    }

    private int PickFreePort(int desired)
    {
        for (int port = desired; port < desired + 20; port++)
        {
            var inUse = Run($"ss -tlnH 'sport = :{port}' 2>/dev/null | head -1", false).outp.Trim();
            if (inUse.Length == 0) return port;
        }
        return desired;
    }

    private void InstallSystemdSystem(string dir, int port, string secret, string bind, string sudo)
    {
        _log("Создаю systemd-сервис (system)…");
        string unit =
            "[Unit]\nDescription=VLESS Monitor Probe Server\nAfter=network.target\n\n" +
            "[Service]\n" +
            $"ExecStart=/usr/bin/python3 {dir}/probe_server.py --port {port} --secret {secret} --bind {bind}\n" +
            "Restart=always\nRestartSec=5\n\n[Install]\nWantedBy=multi-user.target\n";
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(unit));
        Run($"echo '{b64}' | base64 -d | {sudo}tee /etc/systemd/system/vless-probe.service >/dev/null");
        Run($"{sudo}systemctl daemon-reload");
        Run($"{sudo}systemctl enable vless-probe");
        Run($"{sudo}systemctl restart vless-probe"); // restart picks up the new config on reinstall
    }

    private void InstallSystemdUser(ServerInfo si, string dir, int port, string secret)
    {
        _log("Создаю systemd-сервис (--user)…");
        Run("loginctl enable-linger $USER 2>/dev/null || true");
        Run("mkdir -p ~/.config/systemd/user");
        string unit =
            "[Unit]\nDescription=VLESS Monitor Probe Server\n\n[Service]\n" +
            $"ExecStart=/usr/bin/python3 {dir}/probe_server.py --port {port} --secret {secret} --bind 127.0.0.1\n" +
            "Restart=always\n\n[Install]\nWantedBy=default.target\n";
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(unit));
        Run($"echo '{b64}' | base64 -d > ~/.config/systemd/user/vless-probe.service");
        Run("systemctl --user daemon-reload && systemctl --user enable --now vless-probe");
    }

    private void InstallNohup(string dir, int port, string secret, string bind)
    {
        _log("systemd недоступен — запускаю через nohup + cron @reboot…");
        string start = $"/usr/bin/python3 {dir}/probe_server.py --port {port} --secret {secret} --bind {bind}";
        Run($"pkill -f probe_server.py 2>/dev/null || true");
        Run($"nohup {start} >/tmp/vless-probe.log 2>&1 &");
        Run($"(crontab -l 2>/dev/null | grep -v probe_server.py; echo '@reboot {start} >/tmp/vless-probe.log 2>&1') | crontab -");
    }

    // ── Health / repair / uninstall (used by the on-demand server check) ──
    public string HealthCheck(ServerInfo si)
    {
        string sudo = Sudo(si);
        var active = Run($"{sudo}systemctl is-active vless-probe 2>/dev/null || systemctl --user is-active vless-probe 2>/dev/null || echo unknown", false).outp.Trim();
        return active;
    }

    public bool Repair(ServerInfo si)
    {
        string sudo = Sudo(si);
        _log("Пробую перезапустить probe-сервер…");
        var r = Run($"{sudo}systemctl restart vless-probe 2>/dev/null || systemctl --user restart vless-probe 2>/dev/null || echo fail");
        System.Threading.Thread.Sleep(1500);
        return HealthCheck(si) == "active";
    }

    public void Uninstall(ServerInfo si)
    {
        string sudo = Sudo(si);
        _log("Удаляю probe-сервер…");
        Run($"{sudo}systemctl disable --now vless-probe 2>/dev/null || systemctl --user disable --now vless-probe 2>/dev/null || true");
        Run($"{sudo}rm -f /etc/systemd/system/vless-probe.service ~/.config/systemd/user/vless-probe.service");
        Run($"{sudo}systemctl daemon-reload 2>/dev/null || true");
        Run($"crontab -l 2>/dev/null | grep -v probe_server.py | crontab - 2>/dev/null || true");
        Run($"pkill -f probe_server.py 2>/dev/null || true");
    }

    /// <summary>Client-side reachability probe — this is what catches a cloud firewall the OS can't see.</summary>
    public static async Task<bool> VerifyExternalAsync(string host, int port, string secret)
    {
        try
        {
            using var c = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            c.DefaultRequestHeaders.Add("X-Token", secret);
            var r = await c.GetAsync($"http://{host}:{port}/health");
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public static string GenerateSecret() =>
        "vm-" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLower();

    public void Dispose()
    {
        try { _sftp?.Disconnect(); _sftp?.Dispose(); } catch { }
        try { _ssh?.Disconnect(); _ssh?.Dispose(); } catch { }
    }
}
