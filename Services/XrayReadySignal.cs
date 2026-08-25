using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LinqqXrayVPN.Services
{
    /// <summary>
    /// Resolves when a launched xray process prints the "core: Xray ... started" line.
    /// </summary>
    internal sealed class XrayReadySignal
    {
        public enum Outcome { Ready, Exited, TimedOut }

        private readonly TaskCompletionSource<Outcome> _outcome =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private XrayReadySignal()
        {
        }

        public static XrayReadySignal Attach(Process process)
        {
            var signal = new XrayReadySignal();
            process.EnableRaisingEvents = true;
            process.OutputDataReceived += (_, e) => signal.OnOutputLine(e.Data);
            process.ErrorDataReceived += (_, e) => signal.OnOutputLine(e.Data);
            process.Exited += (_, _) => signal._outcome.TrySetResult(Outcome.Exited);
            return signal;
        }

        private void OnOutputLine(string? line)
        {
            if (line is not null
                && line.EndsWith(" started", StringComparison.Ordinal)
                && line.Contains("core:", StringComparison.Ordinal))
            {
                _outcome.TrySetResult(Outcome.Ready);
            }
        }

        public async Task<Outcome> WaitAsync(TimeSpan cap, CancellationToken ct = default)
        {
            try
            {
                return await _outcome.Task.WaitAsync(cap, ct).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return Outcome.TimedOut;
            }
        }
    }
}
