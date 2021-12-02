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

        private ProcessStartInfo psi;
        private Process? process;

        /// <summary>
        ///     Gets or sets the plugin settings.
        /// </summary>
        public Settings Settings { get; private set; }

        /// <summary>
        ///     Constructs a new <see cref="AwakePlugin"/> instance.
        /// </summary>
        /// <param name="connection">The Stream Deck connection.</param>
        /// <param name="payload">The initial payload.</param>
        public AwakePlugin(ISDConnection connection, InitialPayload payload)
            : base(connection, payload)
        {
            // Deserialize settings or create a new instance.
            if (payload?.Settings == null || payload.Settings.Count == 0)
            {
                Settings = new Settings();
            }
            else
            {
                Settings = payload.Settings.ToObject<Settings>() ?? new Settings();
                if (File.Exists(Settings.AwakeExecutablePath)) Connection.ShowAlert().ConfigureAwait(true);
            }
            psi = CreateProcessStartInfo();
            DetectProcess();
        }

        /// <inheritdoc />
        public override void Dispose() => Logger.Instance.LogMessage(TracingLevel.DEBUG,
            "The Dispose method has been called but is not overridden in the plugin.");

        /// <inheritdoc />
        public override void KeyPressed(KeyPayload payload)
        {
            switch (payload.State)
            {
                case 1:
                    Logger.Instance.LogMessage(TracingLevel.INFO, "Activating Awake");
                    StartAwake();
                    return;
                case 2 when process is not null:
                    Logger.Instance.LogMessage(TracingLevel.INFO, "Deactivating Awake");
                    process.Kill();
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
        public override void OnTick() => DetectProcess();

        /// <inheritdoc />
        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) => Logger.Instance.LogMessage(TracingLevel.DEBUG,
            "The ReceivedGlobalSettings method has been called but is not overridden in the plugin.");

        /// <inheritdoc />
        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "Received settings.");
            Tools.AutoPopulateSettings(Settings, payload.Settings);
            if (File.Exists(Settings.AwakeExecutablePath)) Connection.ShowAlert().ConfigureAwait(true);
            Connection.SetSettingsAsync(JObject.FromObject(Settings)).ConfigureAwait(true);
            psi = CreateProcessStartInfo();
        }

        private ProcessStartInfo CreateProcessStartInfo()
        {
            ProcessStartInfo psi = new()
            {
                CreateNoWindow = true,
                FileName = Settings.AwakeExecutablePath,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(Settings.AwakeExecutablePath),
            };

            // Add command line arguments
            if (Settings.UsePtConfig == true)
            {
                psi.ArgumentList.Add("--use-pt-config");
                //psi.ArgumentList.Add("true");
            }
            if (Settings.DisplayOn == true)
            {
                psi.ArgumentList.Add("--display-on");
                //psi.ArgumentList.Add("true");
            }
            if (Settings.TimeLimit.HasValue && Settings.TimeLimit > 0)
            {
                psi.ArgumentList.Add("--time-limit");
                psi.ArgumentList.Add(Settings.TimeLimit.Value.ToString());
            }

            return psi;
        }

        private void DetectProcess()
        {
            // If a process is already monitored, we don't do anything here.
            if (process != null) return;

            // Try to find a running Awake process
            process = Process.GetProcessesByName("PowerToys.Awake").FirstOrDefault();
            if (process == null)
            {
                // None found, so set button inactive state.
                Connection.SetStateAsync(1).ConfigureAwait(true);
                return;
            }

            // Start monitoring process and set button active state
            process.EnableRaisingEvents = true;
            process.Exited += OnProcessExited;
            Connection.SetStateAsync(2).ConfigureAwait(true);
        }

        private void OnProcessExited(object? sender, EventArgs e)
        {
            process?.Dispose();
            process = null;
        }

        private bool StartAwake()
        {
            if (process != null && !process.HasExited)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, "The Awake process is already running.");
                return false;
            }

            process = new()
            {
                StartInfo = psi,
                EnableRaisingEvents = true,
            };
            process.Exited += OnProcessExited;
            return process.Start();
        }
    }
}
