"""
Parse a VLESS URI into components and generate an xray-core outbound config.

vless://UUID@host:port?type=tcp&security=reality&pbk=KEY&fp=chrome&sni=SNI&sid=ID&spx=PATH#name
"""
import json
import urllib.parse
from dataclasses import dataclass, field


@dataclass
class VlessConfig:
    uuid: str
    host: str
    port: int
    name: str = ""
    network: str = "tcp"
    encryption: str = "none"
    security: str = "none"
    flow: str = ""
    # Reality settings
    public_key: str = ""
    fingerprint: str = "chrome"
    sni: str = ""
    short_id: str = ""
    spider_x: str = "/"


def parse_vless_uri(uri: str) -> VlessConfig:
    if not uri.startswith("vless://"):
        raise ValueError("Not a vless:// URI")

    parsed = urllib.parse.urlparse(uri)
    params = dict(urllib.parse.parse_qsl(parsed.query))

    return VlessConfig(
        uuid=parsed.username or "",
        host=parsed.hostname or "",
        port=parsed.port or 443,
        name=urllib.parse.unquote(parsed.fragment or ""),
        network=params.get("type", "tcp"),
        encryption=params.get("encryption", "none"),
        security=params.get("security", "none"),
        flow=params.get("flow", ""),
        public_key=params.get("pbk", ""),
        fingerprint=params.get("fp", "chrome"),
        sni=params.get("sni", ""),
        short_id=params.get("sid", ""),
        spider_x=urllib.parse.unquote(params.get("spx", "/")),
    )


def build_xray_client_config(vless: VlessConfig, socks_port: int = 10808) -> dict:
    """Generate a minimal xray-core config for a VLESS client."""

    stream_settings: dict = {"network": vless.network}

    if vless.security == "reality":
        stream_settings["security"] = "reality"
        stream_settings["realitySettings"] = {
            "serverName": vless.sni,
            "fingerprint": vless.fingerprint,
            "shortId": vless.short_id,
            "publicKey": vless.public_key,
            "spiderX": vless.spider_x,
            "show": False,
        }
    elif vless.security == "tls":
        stream_settings["security"] = "tls"
        stream_settings["tlsSettings"] = {
            "serverName": vless.sni,
            "fingerprint": vless.fingerprint,
        }

    user = {"id": vless.uuid, "encryption": vless.encryption}
    if vless.flow:
        user["flow"] = vless.flow

    return {
        "log": {"loglevel": "warning"},
        "inbounds": [
            {
                "listen": "127.0.0.1",
                "port": socks_port,
                "protocol": "socks",
                "settings": {"auth": "noauth", "udp": True},
                "tag": "socks-in",
            }
        ],
        "outbounds": [
            {
                "tag": "vless-out",
                "protocol": "vless",
                "settings": {
                    "vnext": [
                        {
                            "address": vless.host,
                            "port": vless.port,
                            "users": [user],
                        }
                    ]
                },
                "streamSettings": stream_settings,
            },
            {"tag": "direct", "protocol": "freedom"},
        ],
    }
