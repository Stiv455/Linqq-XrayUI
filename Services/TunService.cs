using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using LinqqXrayVPN.Helpers;

namespace LinqqXrayVPN.Services;

/// <summary>
/// TUN Model related services。
/// Now mainly responsible for wintun.dll Detection and fallback Route cleanup：
/// xray Used at startup elevated Permission passed autoSystemRoutingTable Add your own route，
/// This is just the bottom of the pocket (clear the remaining routes when xray exits abnormally)。
/// </summary>
public class TunService
{
    private readonly string _engineDirectory;

    /// <summary>Default TUN interface name (must be the same as XrayConfigBuilder.BuildTunInbound The name field in is the same）</summary>
    private const string DefaultTunInterfaceName = "xray-tun";

    public TunService()
    {
        _engineDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "engine");
    }

    /// <summary>Check wintun.dll Does the dll exist?</summary>
    public bool IsWintunAvailable()
    {
        var wintunPath = Path.Combine(_engineDirectory, "wintun.dll");
        var exists = File.Exists(wintunPath);
        Debug.WriteLine($"[TunService] wintun.dll path: {wintunPath}, exist: {exists}");
        return exists;
    }

    /// <summary>Get wintun.dll The expected path of the dll (used for error prompts)</summary>
    public string GetExpectedWintunPath() => Path.Combine(_engineDirectory, "wintun.dll");

    /// <summary>
    /// Finds the physical interface Windows would use for normal outbound IPv4 traffic.
    /// TUN mode binds xray outbounds to this interface to avoid sending xray's own
    /// proxy connection back into the TUN adapter.
    /// 
    /// <param name="preferIPv6">Try to define an IPv6 interface first</param>
    /// </summary>
    public string? DetectDefaultOutboundInterfaceName(bool preferIPv6 = true)
    {
        try
        {
            IPAddress? localAddress = null;

            // IPv6
            if (preferIPv6)
            {
                localAddress = GetDefaultOutboundAddress(useIPv6: true);
                if (localAddress is null)
                {
                    Debug.WriteLine("[TunService] IPv6 outbound address not available, falling back to IPv4.");
                }
            }

            // Fallback IPv4
            if (localAddress is null)
            {
                localAddress = GetDefaultOutboundAddress(useIPv6: false);
            }

            if (localAddress is null)
            {
                Debug.WriteLine("[TunService] Could not determine default outbound address (both IPv4 and IPv6 failed).");
                return null;
            }

            var targetFamily = localAddress.AddressFamily;

            var match = NetworkInterface.GetAllNetworkInterfaces()
                .Where(IsCandidateOutboundInterface)
                .Select(nic => new
                {
                    Interface = nic,
                    Properties = nic.GetIPProperties()
                })
                .Where(item => item.Properties.UnicastAddresses.Any(addr =>
                    addr.Address.AddressFamily == targetFamily &&
                    addr.Address.Equals(localAddress)))
                .Select(item => item.Interface)
                .FirstOrDefault();

            if (match is null)
            {
                Debug.WriteLine($"[TunService] Could not map outbound address {localAddress} to a usable interface.");
                return null;
            }

            Debug.WriteLine($"[TunService] Default outbound interface: {match.Name} ({localAddress})");
            return match.Name;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TunService] Default outbound interface detection failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Clean up the bottom of the pocket: xray will delete the route it added by itself when it exits normally; this method only exits when xray exits abnormally.
    /// Or use it when the route remains after exiting.Delete the bottom route of 0.0.0.0/0 + server direct connection route.
    /// </summary>
    public void CleanupTunRoutes(string? serverAddress)
    {
        try
        {
            // Older versions left direct routes for these public DNS resolvers; clean
            // them up if they happen to be there. xray no longer adds them.
            string[] legacyDnsServers = ["223.5.5.5", "119.29.29.29"];

            var batch = new List<string>
            {
                // 0.0.0.0/0 is what current xray adds; the /1 split-routes are residue
                // from earlier routing schemes that may still be lying around.
                $"netsh interface ipv4 delete route 0.0.0.0/0 \"{DefaultTunInterfaceName}\" store=active",
                $"netsh interface ipv4 delete route 0.0.0.0/1 \"{DefaultTunInterfaceName}\" store=active",
                $"netsh interface ipv4 delete route 128.0.0.0/1 \"{DefaultTunInterfaceName}\" store=active",
                // Legacy route.exe form for the same /1 split-routes.
                "route delete 0.0.0.0 mask 128.0.0.0",
                "route delete 128.0.0.0 mask 128.0.0.0",
            };

            // serverAddress May be the host name (e.g. proxy.example.com)，但 Windows `route delete`
            // If the domain name is not resolved, it cannot be processed directly; if it is not IPv4, server-IP cleanup is skipped.
            if (TryParseSafeIPv4Address(serverAddress, out var serverIPv4))
            {
                batch.Add($"netsh interface ipv4 delete route {serverIPv4}/32 \"{DefaultTunInterfaceName}\" store=active");
                batch.Add($"route delete {serverIPv4} mask 255.255.255.255");
            }

            foreach (var dns in legacyDnsServers)
                batch.Add($"route delete {dns} mask 255.255.255.255");

            RunElevatedBatch(batch);
            Debug.WriteLine("[TunService] TUN The bottom of the routing pocket is cleaned up");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TunService] Failed to clean up the TUN route: {ex.Message}");
        }
    }

    private static bool TryParseSafeIPv4Address(string? value, out string address)
    {
        address = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (!IPAddress.TryParse(value, out var parsed) || parsed.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        address = parsed.ToString();
        return true;
    }

    private static IPAddress? GetDefaultOutboundAddress(bool useIPv6 = false)
    {
        try
        {
            using var socket = new Socket(
                useIPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork,
                SocketType.Dgram,
                ProtocolType.Udp);

            // Google Public DNS
            var testEndpoint = useIPv6
                ? new IPEndPoint(IPAddress.Parse("2001:4860:4860::8888"), 53)
                : new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53);

            socket.Connect(testEndpoint);
            return (socket.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch
        {
            return null;
        }
    }

    private bool IsCandidateOutboundInterface(NetworkInterface nic)
    {
        if (nic.OperationalStatus != OperationalStatus.Up)
            return false;

        if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            return false;

        var name = nic.Name ?? string.Empty;
        var description = nic.Description ?? string.Empty;
        var combined = $"{name} {description}";

        return !ContainsAny(combined,
            DefaultTunInterfaceName,
            "wintun",
            "xray",
            "loopback",
            "pseudo-interface",
            "virtualbox",
            "vmware",
            "hyper-v virtual",
            "vethernet");
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (value.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Runs a batch of full command lines (e.g. "netsh interface ipv4 ...", "route delete ...")
    /// in a single cmd.exe — chained with `&amp;` so a failure in one doesn't abort the rest.
    /// One UAC prompt total when not already admin; zero when admin.
    /// </summary>
    private static bool RunElevatedBatch(IReadOnlyList<string> commandLines)
    {
        if (commandLines.Count == 0)
            return true;

        var combined = string.Join(" & ", commandLines);
        var cmdPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var isAdmin = AdminHelper.IsAdministrator();

        var psi = new ProcessStartInfo
        {
            FileName = cmdPath,
            Arguments = "/c " + combined,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        if (isAdmin)
        {
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
        }
        else
        {
            psi.UseShellExecute = true;
            psi.Verb = "runas";
        }

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return false;

            process.WaitForExit(5000);
            // Exit code reflects only the LAST command in the chain — best-effort cleanup,
            // not an authoritative "all succeeded" signal.
            Debug.WriteLine($"[TunService] cleanup Batch exit code: {process.ExitCode}");
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Debug.WriteLine("[TunService] Administrator authorization is cancelled");
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TunService] cleanup Batch execution failed: {ex.Message}");
            return false;
        }
    }
}
