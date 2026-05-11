using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LinqqXrayVPN.Services
{
    /// <summary>
    /// Result of an AI API reachability check.
    /// </summary>
    public enum UnlockStatus
    {
        /// <summary>Not yet checked.</summary>
        Unknown,
        /// <summary>API endpoint is reachable (unlocked).</summary>
        Unlocked,
        /// <summary>API endpoint is blocked or unreachable.</summary>
        Blocked
    }

    public sealed class UnlockCheckService
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        public async Task<UnlockStatus> CheckYouTubeAsync(int httpProxyPort, CancellationToken ct = default)
        {
            try
            {
                using var handler = new HttpClientHandler
                {
                    Proxy = new WebProxy($"http://127.0.0.1:{httpProxyPort}"),
                    UseProxy = true
                };
                using var client = new HttpClient(handler) { Timeout = Timeout };

                var response = await client.GetAsync("https://www.youtube.com/", ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                // Region / country block
                if (body.Contains("unsupported_country_region_territory", StringComparison.OrdinalIgnoreCase))
                    return UnlockStatus.Blocked;

                // Explicit 403 = blocked
                if ((int)response.StatusCode == 403)
                    return UnlockStatus.Blocked;

                return UnlockStatus.Unlocked;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // let caller handle external cancellation
            }
            catch
            {
                return UnlockStatus.Blocked;
            }
        }

        public async Task<UnlockStatus> CheckTelegramAsync(int httpProxyPort, CancellationToken ct = default)
        {
            try
            {
                using var handler = new HttpClientHandler
                {
                    Proxy = new WebProxy($"http://127.0.0.1:{httpProxyPort}"),
                    UseProxy = true     
                };
                using var client = new HttpClient(handler) { Timeout = Timeout };

                // Use GET like the bash curl -sI approach (HEAD may be blocked)
                var request = new HttpRequestMessage(HttpMethod.Get, "https://web.telegram.org/a/");
                var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                var code = (int)response.StatusCode;

                // 401, 400, 405 → API is reachable (just not authenticated)
                if (code == 401 || code == 400 || code == 405)
                    return UnlockStatus.Unlocked;

                // 403 → IP ban / blocked
                if (code == 403)
                    return UnlockStatus.Blocked;

                // Other codes: 2xx, 3xx → also reachable
                if (code >= 200 && code < 400)
                    return UnlockStatus.Unlocked;

                // 5xx or unknown → treat as blocked
                return UnlockStatus.Blocked;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {   
                // Timeout or connection refused → blocked
                return UnlockStatus.Blocked;
            }
        }

        public async Task<UnlockStatus> CheckDiscordAsync(int httpProxyPort, CancellationToken ct = default)
        {
            try
            {
                using var handler = new HttpClientHandler
                {
                    Proxy = new WebProxy($"http://127.0.0.1:{httpProxyPort}"),
                    UseProxy = true
                };
                using var client = new HttpClient(handler) { Timeout = Timeout };

                // Use GET like the bash curl -sI approach (HEAD may be blocked)
                var request = new HttpRequestMessage(HttpMethod.Get, "https://discord.com");
                var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                var code = (int)response.StatusCode;

                // 401, 400, 405 → API is reachable (just not authenticated)
                if (code == 401 || code == 400 || code == 405)
                    return UnlockStatus.Unlocked;

                // 403 → IP ban / blocked
                if (code == 403)
                    return UnlockStatus.Blocked;

                // Other codes: 2xx, 3xx → also reachable
                if (code >= 200 && code < 400)
                    return UnlockStatus.Unlocked;

                // 5xx or unknown → treat as blocked
                return UnlockStatus.Blocked;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Timeout or connection refused → blocked
                return UnlockStatus.Blocked;
            }
        }

        /// <summary>
        /// Parse loc= from Cloudflare cdn-cgi/trace response.
        /// </summary>
        private static async Task<string?> GetCountryFromCloudflareAsync(HttpClient client, CancellationToken ct)
        {
            try
            {
                var body = await client.GetStringAsync("https://www.cloudflare.com/cdn-cgi/trace", ct);
                foreach (var line in body.Split('\n'))
                {
                    if (line.StartsWith("loc=", StringComparison.OrdinalIgnoreCase))
                        return line.Substring(4).Trim();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // fall through to return null
            }
            return null;
        }

        /// <summary>
        /// Parse country from ipinfo.io/json response.
        /// </summary>
        private static async Task<string?> GetCountryFromIpInfoAsync(HttpClient client, CancellationToken ct)
        {
            try
            {
                var body = await client.GetStringAsync("https://ipinfo.io/json", ct);
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("country", out var prop))
                    return prop.GetString();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // fall through to return null
            }
            return null;
        }
    }
}
