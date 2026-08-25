using LinqqXrayVPN.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LinqqXrayVPN.Services
{
    /// <summary>
    /// Builds an xray-core JSON configuration string for the given server and app settings.
    /// Uses JsonObject/JsonArray so Native AOT does not need reflection-based serialization.
    /// </summary>
    public static class XrayConfigBuilder
    {
        private const string DefaultLogLevel = "info";

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true
        };

        public static string Build(ServerEntry server, AppSettings settings, string? tunOutboundInterfaceName = null)
        {
            var config = new JsonObject
            {
                ["log"] = BuildLog(settings),
                ["dns"] = BuildDns(settings),
                ["inbounds"] = BuildInbounds(settings),
                ["outbounds"] = BuildOutbounds(server, settings, tunOutboundInterfaceName),
                ["routing"] = BuildRouting(settings)
            };

            return config.ToJsonString(JsonOpts);
        }

        private static JsonObject BuildLog(AppSettings settings)
        {
            var log = new JsonObject
            {
                ["loglevel"] = DefaultLogLevel
            };

            if (LogMaskAddress.IsEnabled(settings.LogMaskAddress))
            {
                log["maskAddress"] = settings.LogMaskAddress;
            }

            return log;
        }

        private static JsonArray BuildInbounds(AppSettings settings)
        {
            var list = new JsonArray();

            if (settings.IsTunMode)
            {
                AddNode(list, BuildTunInbound(settings));
            }

            AddNode(list, new JsonObject
            {
                ["tag"] = "mixed-in",
                ["protocol"] = "socks",
                ["listen"] = "127.0.0.1",
                ["port"] = settings.LocalMixedPort,
                ["settings"] = new JsonObject
                {
                    ["auth"] = "noauth",
                    ["udp"] = true
                }
            });

            return list;
        }

        private static JsonObject BuildTunInbound(AppSettings settings)
        {
            return new JsonObject
            {
                ["tag"] = "tun-in",
                ["protocol"] = "tun",
                ["settings"] = new JsonObject
                {
                    ["name"] = "xray-tun",
                    ["MTU"] = 1280,
                    // IPv4 + IPv6
                    ["gateway"] = CreateStringArray("172.18.0.1/30", "fdfe:dcba:9876::1/64"),
                    ["strictRoute"] = true,
                    ["autoSystemRoutingTable"] = CreateStringArray("0.0.0.0/0", "::/0"),
                    ["autoOutboundsInterface"] = settings.TunOutboundInterface ?? "auto"
                },
                ["sniffing"] = new JsonObject
                {
                    ["enabled"] = true,
                    ["destOverride"] = CreateStringArray("http", "tls", "quic")
                }
            };
        }

        private static JsonArray BuildOutbounds(ServerEntry server, AppSettings settings, string? tunOutboundInterfaceName)
        {
            var proxy = BuildProxyOutbound(server, "proxy");

            var direct = new JsonObject
            {
                ["tag"] = "direct",
                ["protocol"] = "freedom",
                ["settings"] = new JsonObject()
            };

            var list = new JsonArray();
            AddNode(list, proxy);
            AddNode(list, direct);

            // block outbound is needed by:
            //   1. TUN mode's UDP:443 quench rule
            //   2. Any enabled custom rule targeting "block" (smart mode only)
            bool customRulesUseBlock =
                settings.RoutingMode == "smart"
                && settings.CustomRules is { } rules
                && rules.Any(r => r.IsEnabled
                                  && !string.IsNullOrWhiteSpace(r.Match)
                                  && r.OutboundTag == "block");

            if (settings.IsTunMode || customRulesUseBlock)
            {
                AddNode(list, new JsonObject
                {
                    ["tag"] = "block",
                    ["protocol"] = "blackhole",
                    ["settings"] = new JsonObject()
                });
            }

            if (settings.IsTunMode && !string.IsNullOrWhiteSpace(tunOutboundInterfaceName))
            {
                foreach (var outbound in list.OfType<JsonObject>())
                    ApplyOutboundInterface(outbound, tunOutboundInterfaceName);
            }

            return list;
        }

        private static void ApplyOutboundInterface(JsonObject outbound, string interfaceName)
        {
            var streamSettings = outbound["streamSettings"] as JsonObject;
            if (streamSettings is null)
            {
                streamSettings = new JsonObject();
                outbound["streamSettings"] = streamSettings;
            }

            var sockopt = streamSettings["sockopt"] as JsonObject;
            if (sockopt is null)
            {
                sockopt = new JsonObject();
                streamSettings["sockopt"] = sockopt;
            }

            sockopt["interface"] = interfaceName;
        }

        public static string BuildSpeedtestConfig(
            IReadOnlyList<(ServerEntry server, int port)> entries,
            string? outboundInterface)
        {
            var inbounds = new JsonArray();
            var outbounds = new JsonArray();
            var rules = new JsonArray();

            for (int i = 0; i < entries.Count; i++)
            {
                var (server, port) = entries[i];
                var inTag = $"in-{i}";
                var outTag = $"out-{i}";

                AddNode(inbounds, new JsonObject
                {
                    ["tag"] = inTag,
                    ["protocol"] = "socks",
                    ["listen"] = "127.0.0.1",
                    ["port"] = port,
                    ["settings"] = new JsonObject
                    {
                        ["auth"] = "noauth",
                        ["udp"] = false
                    }
                });

                var outbound = BuildProxyOutbound(server, outTag);
                if (outboundInterface is not null)
                    ApplyOutboundInterface(outbound, outboundInterface);
                AddNode(outbounds, outbound);

                AddNode(rules, new JsonObject
                {
                    ["type"] = "field",
                    ["inboundTag"] = CreateStringArray(inTag),
                    ["outboundTag"] = outTag
                });
            }

            AddNode(outbounds, new JsonObject
            {
                ["tag"] = "direct",
                ["protocol"] = "freedom",
                ["settings"] = new JsonObject()
            });

            var config = new JsonObject
            {
                ["log"] = new JsonObject { ["loglevel"] = "warning" },
                ["inbounds"] = inbounds,
                ["outbounds"] = outbounds,
                ["routing"] = new JsonObject
                {
                    ["domainStrategy"] = "AsIs",
                    ["rules"] = rules
                }
            };

            return config.ToJsonString(JsonOpts);
        }

        private static JsonObject BuildProxyOutbound(ServerEntry server, string tag)
        {
            var outbound = server.Protocol.ToLowerInvariant() switch
            {
                "vmess" => BuildVmessOutbound(server),
                "vless" => BuildVlessOutbound(server),
                "hysteria2" => BuildHysteria2Outbound(server),
                "trojan" => BuildTrojanOutbound(server),
                _ => BuildSsOutbound(server)
            };
            outbound["tag"] = tag;
            return outbound;
        }

        private static JsonObject BuildSsOutbound(ServerEntry server)
        {
            var servers = new JsonArray();
            AddNode(servers, new JsonObject
            {
                ["address"] = server.Host,
                ["port"] = server.Port,
                ["method"] = server.Encryption,
                ["password"] = server.Password
            });

            var outbound = new JsonObject
            {
                ["tag"] = "proxy",
                ["protocol"] = "shadowsocks",
                ["settings"] = new JsonObject
                {
                    ["servers"] = servers
                },
                ["streamSettings"] = new JsonObject
                {
                    ["network"] = "tcp"
                }
            };

            ApplyFinalmask((JsonObject)outbound["streamSettings"]!, server);
            return outbound;
        }

        private static JsonObject BuildVmessOutbound(ServerEntry server)
        {
            var users = new JsonArray();
            AddNode(users, new JsonObject
            {
                ["id"] = server.Uuid,
                ["alterId"] = server.AlterId,
                ["security"] = "auto"
            });

            var vnext = new JsonArray();
            AddNode(vnext, new JsonObject
            {
                ["address"] = server.Host,
                ["port"] = server.Port,
                ["users"] = users
            });

            return new JsonObject
            {
                ["tag"] = "proxy",
                ["protocol"] = "vmess",
                ["settings"] = new JsonObject
                {
                    ["vnext"] = vnext
                },
                ["streamSettings"] = BuildStreamSettings(server)
            };
        }

        private static JsonObject BuildVlessOutbound(ServerEntry server)
        {
            var user = new JsonObject
            {
                ["id"] = server.Uuid,
                ["encryption"] = string.IsNullOrEmpty(server.VlessEncryption) ? "none" : server.VlessEncryption
            };

            if (!string.IsNullOrWhiteSpace(server.Flow))
            {
                user["flow"] = server.Flow;
            }

            var users = new JsonArray();
            AddNode(users, user);

            var vnext = new JsonArray();
            AddNode(vnext, new JsonObject
            {
                ["address"] = server.Host,
                ["port"] = server.Port,
                ["users"] = users
            });

            return new JsonObject
            {
                ["tag"] = "proxy",
                ["protocol"] = "vless",
                ["settings"] = new JsonObject
                {
                    ["vnext"] = vnext
                },
                ["streamSettings"] = BuildStreamSettings(server)
            };
        }

        private static JsonObject BuildHysteria2Outbound(ServerEntry server)
        {
            var sni = string.IsNullOrWhiteSpace(server.Sni) ? server.Host : server.Sni;

            var streamSettings = new JsonObject
            {
                ["network"] = "hysteria",
                ["security"] = "tls",
                ["tlsSettings"] = new JsonObject
                {
                    ["serverName"] = sni,
                    ["allowInsecure"] = server.AllowInsecure
                },
                ["hysteriaSettings"] = new JsonObject
                {
                    ["version"] = 2,
                    ["auth"] = server.Password
                }
            };
            ApplyFinalmask(streamSettings, server);

            return new JsonObject
            {
                ["tag"] = "proxy",
                ["protocol"] = "hysteria",
                ["settings"] = new JsonObject
                {
                    ["version"] = 2,
                    ["address"] = server.Host,
                    ["port"] = server.Port
                },
                ["streamSettings"] = streamSettings
            };
        }

        private static JsonObject BuildTrojanOutbound(ServerEntry server)
        {
            return new JsonObject
            {
                ["tag"] = "proxy",
                ["protocol"] = "trojan",
                ["settings"] = new JsonObject
                {
                    ["address"] = server.Host,
                    ["port"] = server.Port,
                    ["password"] = server.Password
                },
                ["streamSettings"] = BuildStreamSettings(server)
            };
        }

        private static JsonObject BuildStreamSettings(ServerEntry server)
        {
            var network = string.IsNullOrWhiteSpace(server.Network)
                ? "tcp"
                : server.Network.ToLowerInvariant();
            var security = string.IsNullOrWhiteSpace(server.Security)
                ? "none"
                : server.Security.ToLowerInvariant();

            var stream = new JsonObject
            {
                ["network"] = network,
                ["security"] = security
            };

            if (security == "tls")
            {
                // CDN/xhttp nodes need SNI to match the Host header when no explicit SNI is given.
                var hostHeader = (network == "ws" || network == "xhttp") ? server.WsHost : null;
                var sni = !string.IsNullOrWhiteSpace(server.Sni) ? server.Sni
                    : !string.IsNullOrWhiteSpace(hostHeader) ? hostHeader
                    : server.Host;
                var fingerprint = string.IsNullOrWhiteSpace(server.Fingerprint) ? "chrome" : server.Fingerprint;
                var tlsSettings = new JsonObject
                {
                    ["serverName"] = sni,
                    ["fingerprint"] = fingerprint,
                    ["allowInsecure"] = server.AllowInsecure
                };

                // XHTTP rides HTTP/2; without h2 in ALPN many nginx/CDN fronts never hand the
                // request to xray, which shows up as a timeout / "No response".
                if (network == "xhttp")
                    tlsSettings["alpn"] = CreateStringArray("h2");

                if (string.Equals(server.Protocol, "vless", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(server.EchConfigList))
                {
                    tlsSettings["echConfigList"] = server.EchConfigList;

                    var echForceQuery = EchSettings.NormalizeForceQuery(server.EchForceQuery);
                    if (!string.IsNullOrEmpty(echForceQuery))
                    {
                        tlsSettings["echForceQuery"] = echForceQuery;
                    }
                }

                stream["tlsSettings"] = tlsSettings;
            }
            else if (security == "reality")
            {
                var sni = string.IsNullOrWhiteSpace(server.Sni) ? server.Host : server.Sni;
                var fingerprint = string.IsNullOrWhiteSpace(server.Fingerprint) ? "chrome" : server.Fingerprint;
                var spiderX = string.IsNullOrWhiteSpace(server.SpiderX) ? "/" : server.SpiderX;

                stream["realitySettings"] = new JsonObject
                {
                    ["serverName"] = sni,
                    ["fingerprint"] = fingerprint,
                    ["publicKey"] = server.PublicKey,
                    ["shortId"] = server.ShortId,
                    ["spiderX"] = spiderX
                };
            }

            if (network == "ws")
            {
                JsonObject headers;
                if (string.IsNullOrWhiteSpace(server.WsHost))
                {
                    headers = [];
                }
                else
                {
                    headers = new JsonObject
                    {
                        ["Host"] = server.WsHost
                    };
                }

                stream["wsSettings"] = new JsonObject
                {
                    ["path"] = server.Path,
                    ["headers"] = headers
                };
            }
            else if (network == "grpc")
            {
                stream["grpcSettings"] = new JsonObject
                {
                    ["serviceName"] = server.Path
                };
            }
            else if (network == "xhttp")
            {
                var settings = new JsonObject
                {
                    ["path"] = string.IsNullOrWhiteSpace(server.Path) ? "/" : server.Path
                };

                // If host is omitted, xray may send the resolved IP as the HTTP Host header
                // and the front rejects it. Default to the share-link hostname.
                var xhttpHost = !string.IsNullOrWhiteSpace(server.WsHost) ? server.WsHost : server.Host;
                if (!string.IsNullOrWhiteSpace(xhttpHost))
                    settings["host"] = xhttpHost;

                var mode = XhttpSettings.NormalizeMode(server.XhttpMode);

                if (FinalmaskJson.Parse(server.XhttpExtra) is JsonObject extra)
                {
                    NormalizeXhttpDownloadSettings(extra);
                    settings["extra"] = extra;

                    if (mode == XhttpSettings.StreamOne && extra["downloadSettings"] is JsonObject)
                        mode = string.Empty;
                }

                // xray maps empty/auto → packet-up (POST /path/session/seq). Steal-oneself
                // TLS fronts (nginx/CDN) reject that with 405; Reality already auto-picks
                // stream-one for the same reason. Do the same for TLS when the link omits mode.
                if (string.IsNullOrEmpty(mode) && security == "tls")
                    mode = XhttpSettings.StreamOne;

                if (!string.IsNullOrEmpty(mode))
                    settings["mode"] = mode;

                stream["xhttpSettings"] = settings;
            }

            ApplyFinalmask(stream, server);
            return stream;
        }

        private static void ApplyFinalmask(JsonObject streamSettings, ServerEntry server)
        {
            var finalmask = FinalmaskJson.Parse(server.Finalmask);
            if (finalmask is JsonObject)
            {
                streamSettings["finalmask"] = finalmask;
            }
        }

        // v2board panels emit compact downloadSettings ({server, servername, path, port});
        // xray wants a StreamConfig (address + network/security/tlsSettings/xhttpSettings).
        private static void NormalizeXhttpDownloadSettings(JsonObject extra)
        {
            if (extra["downloadSettings"] is not JsonObject download)
                return;

            var isCompact = download["server"] is not null
                || download["servername"] is not null
                || download["path"] is not null;
            if (!isCompact)
                return;

            if (download["address"] is null && download["server"] is JsonNode address)
            {
                download.Remove("server");
                download["address"] = address;
            }

            if (download["network"] is null)
                download["network"] = "xhttp";

            if (download["xhttpSettings"] is null && download["path"] is JsonNode path)
            {
                download.Remove("path");
                download["xhttpSettings"] = new JsonObject { ["path"] = path };
            }

            if (download["tlsSettings"] is null && download["servername"] is JsonNode serverName)
            {
                download.Remove("servername");
                if (download["security"] is null)
                    download["security"] = "tls";
                download["tlsSettings"] = new JsonObject { ["serverName"] = serverName };
            }
        }

        private static JsonObject BuildRouting(AppSettings settings)
        {
            var rules = new JsonArray();

            if (settings.IsTunMode)
            {
                AddNode(rules, new JsonObject
                {
                    ["type"] = "field",
                    ["outboundTag"] = "direct",
                    ["process"] = CreateStringArray("self/", "xray/")
                });

                AddNode(rules, new JsonObject
                {
                    ["type"] = "field",
                    ["outboundTag"] = "block",
                    ["network"] = "udp",
                    ["port"] = "443"
                });
            }

            if (settings.RoutingMode == "global")
            {
                AddNode(rules, new JsonObject
                {
                    ["type"] = "field",
                    ["outboundTag"] = "proxy",
                    ["network"] = "tcp,udp"
                });

                return new JsonObject
                {
                    ["domainStrategy"] = "AsIs",
                    ["rules"] = rules
                };
            }

            // User-defined custom rules run first (smart mode only, first-match-wins).
            if (settings.CustomRules is { } customRules)
            {
                foreach (var rule in customRules)
                {
                    if (!rule.IsEnabled || string.IsNullOrWhiteSpace(rule.Match))
                        continue;

                    var node = new JsonObject
                    {
                        ["type"] = "field",
                        ["outboundTag"] = rule.OutboundTag,
                    };
                    if (rule.Type == "ip")
                        node["ip"] = CreateStringArray(rule.Match);
                    else
                        node["domain"] = CreateStringArray(rule.Match);

                    AddNode(rules, node);
                }
            }

            AddNode(rules, new JsonObject
            {
                ["type"] = "field",
                ["outboundTag"] = "proxy",
                ["domain"] = CreateStringArray(
					"geosite:google"
				)
            });
            AddNode(rules, new JsonObject
            {
                ["type"] = "field",
                ["outboundTag"] = "direct",
                ["domain"] = CreateStringArray("geosite:cn", "geosite:private")
            });
            AddNode(rules, new JsonObject
            {
                ["type"] = "field",
                ["outboundTag"] = "direct",
                ["ip"] = CreateStringArray("geoip:cn", "geoip:private")
            });
            AddNode(rules, new JsonObject
            {
                ["type"] = "field",
                ["outboundTag"] = "proxy",
                ["network"] = "tcp,udp"
            });

            return new JsonObject
            {
                ["domainStrategy"] = "IPIfNonMatch",
                ["rules"] = rules
            };
        }

        private static JsonObject BuildDns(AppSettings settings)
        {
            var dnsList = settings.DnsServers?.Count > 0
                ? settings.DnsServers
                : new List<string> { "8.8.8.8", "1.1.1.1", "localhost" };

            return new JsonObject
            {
                ["servers"] = CreateStringArray(dnsList.ToArray())
            };
        }

        private static JsonArray CreateStringArray(params string[] values)
        {
            var array = new JsonArray();
            foreach (var value in values)
            {
                AddValue(array, value);
            }

            return array;
        }

        private static void AddNode(JsonArray array, JsonNode node)
        {
            array.Add(node);
        }

        private static void AddValue(JsonArray array, string value)
        {
            array.Add((JsonNode?)JsonValue.Create(value));
        }
    }
}
