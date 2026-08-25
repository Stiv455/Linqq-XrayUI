using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Windows.AppLifecycle;
using LinqqXrayVPN.Services;

namespace LinqqXrayVPN
{
    public partial class App
    {
        private const string SingleInstanceKey = "LinqqXrayVPN.MainInstance";
        private const string ParentPidArgumentPrefix = "--parent-pid=";
        private const string TunArgument = "--tun";
        private const uint ShutdownNoRetry = 0x00000001;
        private const uint ShutdownLevel = 0x280;
        private Window? _window;
        private AppInstance? _mainInstance;
        private bool _cleanupStarted;
        private volatile bool _pendingExternalActivation;

        public Window? Window => _window;

        public App()
        {
            this.InitializeComponent();

            ConfigureProcessShutdownBehavior();
            this.UnhandledException += (_, _) => CleanupOnExit();
            AppDomain.CurrentDomain.ProcessExit += (_, _) => CleanupOnExit();
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs args)
        {
            await SetAppLanguageAsync();

            var cmdArgs = Environment.GetCommandLineArgs();
            var parentPid = TryGetParentProcessId(cmdArgs);
            var startMinimized = cmdArgs.Contains(StartupService.StartupMinimizedArgument, StringComparer.OrdinalIgnoreCase);
            var isTunLaunch = cmdArgs.Contains(TunArgument, StringComparer.OrdinalIgnoreCase);
            // Language restart (and TUN elevation) spawn a sibling with --parent-pid while
            // the old process is still alive. Skip single-instance redirect or the new
            // process is treated as a duplicate, killed, and the old one appears hung.
            var isRestartTakeover = parentPid.HasValue;

            Debug.WriteLine($"[Launch] startMinimized = {startMinimized}, isTunLaunch = {isTunLaunch}");

            if (!isRestartTakeover && await TryRedirectToExistingInstanceAsync(startMinimized))
            {
                Debug.WriteLine("[Launch] Redirected to existing instance");
                return;
            }

            try
            {
                _window = new MainWindow(startMinimized);
                _window.Closed += (_, _) => CleanupOnExit();

                if (isTunLaunch)
                {
                    if (_window is MainWindow mw)
                        mw.ViewModel.ControlPanel.SetTunEnabledSilently(true);
                }

                if (startMinimized)
                {
                    _window.AppWindow.Move(new Windows.Graphics.PointInt32(-32000, -32000));
                }

                _window.Activate();
                Debug.WriteLine("[Launch] Window.Activate()");

                if (startMinimized)
                {
                    _window.AppWindow.IsShownInSwitchers = false;
                    _window.AppWindow.Hide();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Launch] Error: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
                return;
            }

            if (parentPid.HasValue)
            {
                _ = TakeOverPreviousInstanceAsync(parentPid.Value, registerSingleInstanceAfterTakeover: true);
            }

            if (_pendingExternalActivation && _window is MainWindow mainWindow)
            {
                _pendingExternalActivation = false;
                mainWindow.RestoreFromTray();
            }
        }
        private async Task SetAppLanguageAsync()
        {
            Debug.WriteLine("[] SetAppLanguageAsync started");

            try
            {
                var settingsService = new SettingsService();
                var settings = await settingsService.LoadSettingsAsync();

                string lang = !string.IsNullOrEmpty(settings.Language) ? settings.Language : "en-US";

                Debug.WriteLine($"[Language] Set: {lang}");

                // Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = lang;

                await LocalizationService.Instance.LoadLanguageAsync(lang);

                Debug.WriteLine("[Language] Good");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Language] Error: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
            }
        }
        public void RequestShutdown(bool fastShutdown = false)
        {
            CleanupOnExit(fastShutdown);
            Environment.Exit(0);
        }

        public void HandleSessionEnding()
        {
            CleanupOnExit(fastShutdown: true);
        }

        private void CleanupOnExit(bool fastShutdown = false)
        {
            if (_cleanupStarted)
            {
                return;
            }

            _cleanupStarted = true;

            SystemProxyService.ClearProxy();

            if (_window is MainWindow mainWindow)
            {
                mainWindow.StopBackgroundServicesOnExit(fastShutdown);
            }
        }

        private static void ConfigureProcessShutdownBehavior()
        {
            try
            {
                if (!SetProcessShutdownParameters(ShutdownLevel, ShutdownNoRetry))
                {
                    Debug.WriteLine($"[Shutdown] SetProcessShutdownParameters failed: {Marshal.GetLastWin32Error()}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Shutdown] Failed to configure shutdown behavior: {ex.Message}");
            }
        }

        private static int? TryGetParentProcessId(string[] cmdArgs)
        {
            foreach (var arg in cmdArgs)
            {
                if (!arg.StartsWith(ParentPidArgumentPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = arg[ParentPidArgumentPrefix.Length..];
                if (int.TryParse(value, out var pid) && pid > 0)
                {
                    return pid;
                }
            }

            return null;
        }

        private async Task<bool> TryRedirectToExistingInstanceAsync(bool startMinimized)
        {
            AppInstance mainInstance;
            try
            {
                mainInstance = AppInstance.FindOrRegisterForKey(SingleInstanceKey);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SingleInstance] Failed to register app instance: {ex}");
                return false;
            }

            if (mainInstance.IsCurrent)
            {
                RegisterCurrentAsMainInstance(mainInstance);
                return false;
            }

            if (!startMinimized)
            {
                try
                {
                    var activatedEventArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
                    if (activatedEventArgs is not null)
                    {
                        await mainInstance.RedirectActivationToAsync(activatedEventArgs);
                    }
                    else
                    {
                        Debug.WriteLine("[SingleInstance] Activation args were null; exiting duplicate instance without redirect.");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SingleInstance] Failed to redirect activation: {ex}");
                }
            }

            ExitDuplicateInstance();
            return true;
        }

        private bool RegisterCurrentAsMainInstance(AppInstance? appInstance = null)
        {
            try
            {
                appInstance ??= AppInstance.FindOrRegisterForKey(SingleInstanceKey);
                if (!appInstance.IsCurrent)
                {
                    Debug.WriteLine($"[SingleInstance] Instance key is still owned by process {appInstance.ProcessId}.");
                    return false;
                }

                if (_mainInstance is null)
                {
                    _mainInstance = appInstance;
                    _mainInstance.Activated += OnAppInstanceActivated;
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SingleInstance] Failed to register current instance: {ex}");
                return false;
            }
        }

        private async Task RegisterCurrentAsMainInstanceAfterTakeoverAsync()
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                if (RegisterCurrentAsMainInstance())
                {
                    return;
                }

                await Task.Delay(150);
            }
        }

        private void OnAppInstanceActivated(object? sender, AppActivationArguments args)
        {
            var window = _window;
            if (window is not null && window.DispatcherQueue.TryEnqueue(RestoreOrDeferActivation))
            {
                return;
            }

            _pendingExternalActivation = true;
        }

        private void RestoreOrDeferActivation()
        {
            if (_window is MainWindow mainWindow)
            {
                mainWindow.RestoreFromTray();
            }
            else
            {
                _pendingExternalActivation = true;
            }
        }

        private static void ExitDuplicateInstance()
        {
            try
            {
                Process.GetCurrentProcess().Kill();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SingleInstance] Failed to terminate duplicate process cleanly: {ex}");
                Environment.FailFast("Duplicate LinqqXrayVPN instance could not exit without running shutdown cleanup.", ex);
            }
        }

        private async Task TakeOverPreviousInstanceAsync(int parentPid, bool registerSingleInstanceAfterTakeover)
        {
            try
            {
                if (parentPid <= 0 || parentPid == Environment.ProcessId)
                {
                    return;
                }

                await Task.Delay(150);

                using var previousInstance = Process.GetProcessById(parentPid);
                if (!previousInstance.HasExited)
                {
                    try
                    {
                        previousInstance.CloseMainWindow();
                    }
                    catch (InvalidOperationException)
                    {
                        // Ignore; some startup states have no main window handle yet.
                    }

                    if (!previousInstance.WaitForExit(350))
                    {
                        previousInstance.Kill(entireProcessTree: true);
                        previousInstance.WaitForExit(3000);
                    }
                }
            }
            catch (ArgumentException)
            {
                // The previous instance already exited.
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TUN] Failed to take over previous instance {parentPid}: {ex}");
            }
            finally
            {
                if (registerSingleInstanceAfterTakeover)
                {
                    await RegisterCurrentAsMainInstanceAfterTakeoverAsync();
                }
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessShutdownParameters(uint dwLevel, uint dwFlags);
    }
}
