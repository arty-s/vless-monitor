using System.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VlessMonitor.Core;

/// <summary>
/// Parses a vless:// URI and generates an xray-core client config.
/// </summary>
public class VlessUri
{
    public string Uuid = "";
    public string Host = "";
    public int Port = 443;
    public string Name = "";
    public string Network = "tcp";
    public string Encryption = "none";
    public string Security = "none";
    public string Flow = "";
    public string PublicKey = "";
    public string Fingerprint = "chrome";
    public string Sni = "";
    public string ShortId = "";
    public string SpiderX = "/";

    public static VlessUri Parse(string uri)
    {
        if (!uri.StartsWith("vless://"))
            throw new ArgumentException("Не VLESS-ссылка (должна начинаться с vless://)");

        var u = new Uri(uri);
        var q = HttpUtility.ParseQueryString(u.Query);

        return new VlessUri
        {
            Uuid = u.UserInfo,
            Host = u.Host,
            Port = u.Port > 0 ? u.Port : 443,
            Name = Uri.UnescapeDataString(u.Fragment.TrimStart('#')),
            Network = q["type"] ?? "tcp",
            Encryption = q["encryption"] ?? "none",
            Security = q["security"] ?? "none",
            Flow = q["flow"] ?? "",
            PublicKey = q["pbk"] ?? "",
            Fingerprint = q["fp"] ?? "chrome",
            Sni = q["sni"] ?? "",
            ShortId = q["sid"] ?? "",
            SpiderX = Uri.UnescapeDataString(q["spx"] ?? "/"),
        };
    }

    /// <summary>Build a minimal xray-core config JSON for this VLESS client.</summary>
    public string BuildXrayConfig(int socksPort)
    {
        var streamSettings = new JsonObject { ["network"] = Network };

        if (Security == "reality")
        {
            streamSettings["security"] = "reality";
            streamSettings["realitySettings"] = new JsonObject
            {
                ["serverName"] = Sni,
                ["fingerprint"] = Fingerprint,
                ["shortId"] = ShortId,
                ["publicKey"] = PublicKey,
                ["spiderX"] = SpiderX,
                ["show"] = false,
            };
        }
        else if (Security == "tls")
        {
            streamSettings["security"] = "tls";
            streamSettings["tlsSettings"] = new JsonObject
            {
                ["serverName"] = Sni,
                ["fingerprint"] = Fingerprint,
            };
        }

        var user = new JsonObject { ["id"] = Uuid, ["encryption"] = Encryption };
        if (!string.IsNullOrEmpty(Flow)) user["flow"] = Flow;

        var config = new JsonObject
        {
            ["log"] = new JsonObject { ["loglevel"] = "warning" },
            ["inbounds"] = new JsonArray
            {
                new JsonObject
                {
                    ["listen"] = "127.0.0.1",
                    ["port"] = socksPort,
                    ["protocol"] = "socks",
                    ["settings"] = new JsonObject { ["auth"] = "noauth", ["udp"] = true },
                    ["tag"] = "socks-in",
                }
            },
            ["outbounds"] = new JsonArray
            {
                new JsonObject
                {
                    ["tag"] = "vless-out",
                    ["protocol"] = "vless",
                    ["settings"] = new JsonObject
                    {
                        ["vnext"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["address"] = Host,
                                ["port"] = Port,
                                ["users"] = new JsonArray { user },
                            }
                        }
                    },
                    ["streamSettings"] = streamSettings,
                },
                new JsonObject { ["tag"] = "direct", ["protocol"] = "freedom" },
            },
        };

        return config.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
