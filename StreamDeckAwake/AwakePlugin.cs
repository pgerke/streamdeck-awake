using BarRaider.SdTools;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace PhilipGerke.StreamDeck.Awake
{
    /// <summary>
    ///     The Stream Deck Awake plugin.
    /// </summary>
    [PluginActionId("com.philipgerke.awake.toggle")]
    public sealed class AwakePlugin : PluginBase
    {
        /// <summary>
        ///     Gets or sets the plugin settings.
        /// </summary>
        public Settings Settings { get; private set; }

        public AwakeService AwakeService { get; private set; }

        /// <summary>
        ///     Constructs a new <see cref="AwakePlugin"/> instance.
        /// </summary>
        /// <param name="connection">The Stream Deck connection.</param>
        /// <param name="payload">The initial payload.</param>
        public AwakePlugin(ISDConnection connection, InitialPayload payload)
            : base(connection, payload)
        {
            AwakeService = new AwakeService(Logger.Instance, connection);
            // Deserialize settings or create a new instance.
            if (payload?.Settings == null || payload.Settings.Count == 0)
            {
                Settings = new Settings();
            }
            else
            {
                Settings = payload.Settings.ToObject<Settings>() ?? new Settings();
                //if (File.Exists(Settings.AwakeExecutablePath)) Connection.ShowAlert().ConfigureAwait(true);
            }
            Connection.SetStateAsync(1).ConfigureAwait(true);
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "The plugin instance is being disposed. Awake process will be stopped.");
            AwakeService.StopAwake();
        }

        /// <inheritdoc />
        public override void KeyPressed(KeyPayload payload)
        {
            switch (payload.State)
            {
                case 1 when Settings.UsePtConfig.HasValue && Settings.UsePtConfig.Value:
                    Logger.Instance.LogMessage(TracingLevel.INFO, "Activating Awake using Microsoft PowerToys settings");
                    // TODO: File System Watcher for PowerToys config file
                    Connection.ShowAlert().ConfigureAwait(true);
                    Connection.SetStateAsync(1).ConfigureAwait(true);
                    return;
                case 1 when Settings.TimeLimit is null:
                    Logger.Instance.LogMessage(TracingLevel.INFO, "Activating indefinite Awake");
                    AwakeService.StartAwakeIndefinite(OnAwakeSuccess, OnAwakeFailureOrCancelled, Settings.DisplayOn.HasValue && Settings.DisplayOn.Value);
                    return;
                case 1:
                    Logger.Instance.LogMessage(TracingLevel.INFO, $"Activating timed Awake: {Settings.TimeLimit}s");
                    AwakeService.StartAwakeTimed(Settings.TimeLimit.Value, OnAwakeSuccess, OnAwakeFailureOrCancelled, Settings.DisplayOn.HasValue && Settings.DisplayOn.Value);
                    return;
                case 2:
                    Logger.Instance.LogMessage(TracingLevel.INFO, "Deactivating Awake");
                    AwakeService.StopAwake();
                    return;
                default:
                    Logger.Instance.LogMessage(TracingLevel.ERROR, "State is unknown!");
                    Connection.ShowAlert().ConfigureAwait(true);
                    return;
            }
        }

        /// <inheritdoc />
        public override void KeyReleased(KeyPayload payload) => Logger.Instance.LogMessage(TracingLevel.DEBUG,
            "The KeyReleased method has been called but is not overridden in the plugin.");

        /// <inheritdoc />
        public override void OnTick()
        {
            // TODO: Update timer if Awake is active and in timed mode.
        }

        /// <inheritdoc />
        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) => Logger.Instance.LogMessage(TracingLevel.DEBUG,
            "The ReceivedGlobalSettings method has been called but is not overridden in the plugin.");

        /// <inheritdoc />
        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "Received settings.");
            Tools.AutoPopulateSettings(Settings, payload.Settings);
            //if (File.Exists(Settings.AwakeExecutablePath)) Connection.ShowAlert().ConfigureAwait(true);
            Connection.SetSettingsAsync(JObject.FromObject(Settings)).ConfigureAwait(true);
            //psi = CreateProcessStartInfo();
        }

        //private ProcessStartInfo CreateProcessStartInfo()
        //{
        //    Settings.SetExecutablePathIfEmpty();
        //    ProcessStartInfo psi = new()
        //    {
        //        CreateNoWindow = true,
        //        FileName = Settings.AwakeExecutablePath,
        //        UseShellExecute = false,
        //        WindowStyle = ProcessWindowStyle.Hidden,
        //        WorkingDirectory = Path.GetDirectoryName(Settings.AwakeExecutablePath),
        //    };

        //    // Add command line arguments
        //    if (Settings.UsePtConfig.HasValue && Settings.UsePtConfig.Value)
        //    {
        //        psi.ArgumentList.Add("--use-pt-config");
        //    }
        //    if (Settings.DisplayOn.HasValue && Settings.DisplayOn.Value)
        //    {
        //        psi.ArgumentList.Add("--display-on");
        //    }
        //    if (Settings.TimeLimit.HasValue && Settings.TimeLimit > 0)
        //    {
        //        psi.ArgumentList.Add("--time-limit");
        //        psi.ArgumentList.Add(Settings.TimeLimit.Value.ToString());
        //    }

        //    return psi;
        //}

        //private void DetectProcess()
        //{
        //    // If a process is already monitored, we don't do anything here.
        //    if (process != null) return;

        //    // Try to find a running Awake process
        //    process = Process.GetProcessesByName("PowerToys.Awake").FirstOrDefault();
        //    if (process == null)
        //    {
        //        // None found, so set button inactive state.
        //        Connection.SetStateAsync(1).ConfigureAwait(true);
        //        return;
        //    }

        //    // Start monitoring process and set button active state
        //    process.EnableRaisingEvents = true;
        //    process.Exited += OnProcessExited;
        //    Connection.SetStateAsync(2).ConfigureAwait(true);
        //}

        //private void OnProcessExited(object? sender, EventArgs e)
        //{
        //    process?.Dispose();
        //    process = null;
        //}

        //private bool StartAwake()
        //{
        //    if (process != null && !process.HasExited)
        //    {
        //        Logger.Instance.LogMessage(TracingLevel.ERROR, "The Awake process is already running.");
        //        return false;
        //    }

        //    process = new()
        //    {
        //        StartInfo = psi,
        //        EnableRaisingEvents = true,
        //    };
        //    process.Exited += OnProcessExited;
        //    return process.Start();
        //}

        private void OnAwakeFailureOrCancelled()
        {
            string? errorMessage = "The keep-awake thread was terminated early.";
            Logger.Instance.LogMessage(TracingLevel.INFO, errorMessage);
            Logger.Instance.LogMessage(TracingLevel.DEBUG, errorMessage);
            Connection.ShowAlert().ConfigureAwait(true);
            Connection.SetStateAsync(1).ConfigureAwait(true);
        }

        private void OnAwakeSuccess(bool result)
        {
            Logger.Instance.LogMessage(TracingLevel.INFO, $"Exited keep-awake thread successfully: {result}");
            Connection.SetStateAsync(1).ConfigureAwait(true);
        }
    }
}
