using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LinqqXrayVPN.Helpers;
using LinqqXrayVPN.Models;

namespace LinqqXrayVPN.Services
{
    /// <summary>
    /// Real-delay probe: HTTP round-trip through a throwaway xray core, one socks inbound
    /// per server. Independent of the live session owned by <see cref="XrayService"/>.
    /// </summary>
    public sealed class RealLatencyProbeService
    {
        private readonly SettingsService _settings;
        private readonly TunService _tunService;

        public Func<bool> IsTunActive { get; set; } = () => false;

        public RealLatencyProbeService(SettingsService settings, TunService tunService)
        {
            _settings = settings;
            _tunService = tunService;
        }

        private const string TestUrl = "http://www.gstatic.com/generate_204";
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(7);
        private const int ProbeIterations = 2;
        private const int InterProbeDelayMs = 100;
        private const int MaxConcurrency = 32;
        private static readonly TimeSpan CoreReadyCap = TimeSpan.FromSeconds(2);

        private static readonly string ConfigPath = Path.Combine(AppPaths.LocalAppDataDir, "xray_speedtest.json");

        public string LastError { get; private set; } = string.Empty;

        public async Task ProbeAllAsync(
            IReadOnlyList<ServerEntry> servers,
            Action<ServerEntry, int> onResult,
            CancellationToken ct = default)
        {
            LastError = string.Empty;
            if (servers.Count == 0)
                return;

            var ui = SynchronizationContext.Current;
            void Report(ServerEntry s, int ms)
            {
                if (ui is not null)
                    ui.Post(_ => onResult(s, ms), null);
                else
                    onResult(s, ms);
            }

            if (!File.Exists(XrayService.ExePath))
            {
                LastError = $"xray.exe not found: {XrayService.ExePath}";
                foreach (var s in servers)
                    Report(s, -1);
                return;
            }

            var entries = new List<(ServerEntry server, int port)>(servers.Count);
            foreach (var s in servers)
                entries.Add((s, GetFreeLoopbackPort()));

            var settings = await _settings.LoadSettingsAsync().ConfigureAwait(false);
            var outboundInterface = IsTunActive()
                ? _tunService.ResolveOutboundInterface(settings.TunOutboundInterface)
                : null;

            string configJson = XrayConfigBuilder.BuildSpeedtestConfig(entries, outboundInterface);
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            await File.WriteAllTextAsync(ConfigPath, configJson, ct).ConfigureAwait(false);

            Process? process = null;
            JobObjectGuard? jobGuard = null;
            var output = new StringBuilder();

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = XrayService.ExePath,
                    Arguments = $"run -config \"{ConfigPath}\"",
                    WorkingDirectory = Path.GetDirectoryName(XrayService.ExePath)!,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                psi.EnvironmentVariables["XRAY_LOCATION_ASSET"] = XrayService.RulesDir;

                process = new Process { StartInfo = psi };
                var readySignal = XrayReadySignal.Attach(process);
                void Capture(string? line)
                {
                    if (line is null) return;
                    lock (output) output.AppendLine(line);
                }
                process.OutputDataReceived += (_, e) => Capture(e.Data);
                process.ErrorDataReceived += (_, e) => Capture(e.Data);

                process.Start();
                jobGuard = JobObjectGuard.Assign(process);
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await readySignal.WaitAsync(CoreReadyCap, ct).ConfigureAwait(false);

                if (process.HasExited)
                {
                    string log;
                    lock (output) log = output.ToString().Trim();
                    LastError = log.Length > 0
                        ? log
                        : $"xray exited immediately: {process.ExitCode}";
                    foreach (var s in servers)
                        Report(s, -1);
                    return;
                }

                using var throttle = new SemaphoreSlim(MaxConcurrency);
                var tasks = new List<Task>(entries.Count);
                foreach (var (server, port) in entries)
                    tasks.Add(ProbeOneAsync(server, port, throttle, Report, ct));

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                foreach (var s in servers)
                    Report(s, -1);
            }
            finally
            {
                try
                {
                    if (process is not null && !process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch { }

                jobGuard?.Dispose();
                process?.Dispose();

                try
                {
                    if (File.Exists(ConfigPath))
                        File.Delete(ConfigPath);
                }
                catch { }
            }
        }

        private static async Task ProbeOneAsync(
            ServerEntry server,
            int port,
            SemaphoreSlim throttle,
            Action<ServerEntry, int> report,
            CancellationToken ct)
        {
            await throttle.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                using var handler = new SocketsHttpHandler
                {
                    Proxy = new WebProxy($"socks5://127.0.0.1:{port}"),
                    UseProxy = true
                };
                using var client = new HttpClient(handler);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(ProbeTimeout);

                var best = -1;
                for (var i = 0; i < ProbeIterations; i++)
                {
                    var stopwatch = Stopwatch.StartNew();
                    using (await client.GetAsync(TestUrl, timeoutCts.Token).ConfigureAwait(false))
                    {
                        stopwatch.Stop();
                    }

                    var ms = (int)Math.Round(stopwatch.Elapsed.TotalMilliseconds);
                    if (ms >= 0 && (best < 0 || ms < best))
                        best = ms;

                    if (i < ProbeIterations - 1)
                        await Task.Delay(InterProbeDelayMs, timeoutCts.Token).ConfigureAwait(false);
                }

                report(server, best);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                report(server, -1);
            }
            finally
            {
                throttle.Release();
            }
        }

        private static int GetFreeLoopbackPort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }
}
