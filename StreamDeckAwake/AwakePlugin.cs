using BarRaider.SdTools;
using Newtonsoft.Json.Linq;
using System.Drawing;
using System.Reflection;

namespace PhilipGerke.StreamDeck.Awake
{
    /// <summary>
    ///     The Stream Deck Awake plugin.
    /// </summary>
    [PluginActionId("com.philipgerke.awake.toggle")]
    public sealed class AwakePlugin : PluginBase, IAwakePlugin
    {
        private bool? state = null;
        private readonly Image imgOn, imgOff, imgUnknown;

        ISDConnection IAwakePlugin.Connection => Connection;
        Logger IAwakePlugin.Logger => Logger.Instance;

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
            // Load embedded images
            Assembly assembly = Assembly.GetExecutingAssembly();
#pragma warning disable CS8604 // Possible null reference argument.
            imgOn = Image.FromStream(assembly.GetManifestResourceStream("PhilipGerke.StreamDeck.Awake.Images.awakeOn@2x.png"));
            imgOff = Image.FromStream(assembly.GetManifestResourceStream("PhilipGerke.StreamDeck.Awake.Images.awakeOff@2x.png"));
            imgUnknown = Image.FromStream(assembly.GetManifestResourceStream("PhilipGerke.StreamDeck.Awake.Images.awakeUnknown@2x.png"));
#pragma warning restore CS8604 // Possible null reference argument.

            // Connect to service instance
            AwakeService.GetInstance().SetPluginInstance(this);

            // Set status from service
            SetAwakeState(AwakeService.IsEnabled).Wait();
            SetRemainingTime();

            // Deserialize settings or create a new instance.
            if (payload?.Settings == null || payload.Settings.Count == 0)
            {
                Settings = new Settings();
            }
            else
            {
                Settings = payload.Settings.ToObject<Settings>() ?? new Settings();
            }

        }

        /// <inheritdoc />
        public override void Dispose()
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "The plugin instance is being disposed. Disconnecting from Awake service.");
            AwakeService.GetInstance().ClearPluginInstance();
        }

        /// <inheritdoc />
        public override void KeyPressed(KeyPayload payload)
        {
            switch (state)
            {
                case false:
                    if (Settings.TimeLimit is null || !uint.TryParse(Settings.TimeLimit, out uint timeLimit))
                    {
                        Logger.Instance.LogMessage(TracingLevel.INFO, "Activating indefinite Awake");
                        AwakeService.GetInstance().StartAwakeIndefinite(Settings.DisplayOn.HasValue && Settings.DisplayOn.Value);
                    }
                    else
                    {
                        Logger.Instance.LogMessage(TracingLevel.INFO, $"Activating timed Awake: {Settings.TimeLimit}s");
                        AwakeService.GetInstance().StartAwakeTimed(timeLimit, Settings.DisplayOn.HasValue && Settings.DisplayOn.Value);
                    }
                    return;
                case true:
                    Logger.Instance.LogMessage(TracingLevel.INFO, "Deactivating Awake");
                    AwakeService.GetInstance().StopAwake(false);
                    return;
                default:
                    Logger.Instance.LogMessage(TracingLevel.ERROR, "Plugin is in an unexpected state.");
                    AwakeService.GetInstance().StopAwake(false);
                    return;
            }
        }

        /// <inheritdoc />
        public override void KeyReleased(KeyPayload payload) => Logger.Instance.LogMessage(TracingLevel.DEBUG,
            "The KeyReleased method has been called but is not overridden in the plugin.");

        /// <inheritdoc />
        public override void OnTick() => SetRemainingTime();

        /// <inheritdoc />
        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) => Logger.Instance.LogMessage(TracingLevel.DEBUG,
            "The ReceivedGlobalSettings method has been called but is not overridden in the plugin.");

        /// <inheritdoc />
        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "Received settings.");

            Tools.AutoPopulateSettings(Settings, payload.Settings);
            Connection.SetSettingsAsync(JObject.FromObject(Settings)).Wait();
        }

        /// <inheritdoc />
        public async Task SetAwakeState(bool? enabled)
        {
            state = enabled;
            switch (enabled)
            {
                case true:
                    await Connection.SetImageAsync(imgOn);
                    return;
                case false:
                    await Connection.SetImageAsync(imgOff);
                    return;
                default:
                    await Connection.SetImageAsync(imgUnknown);
                    return;
            }
        }

        private void SetRemainingTime()
        {
            if (!AwakeService.GetInstance().TimeRemaining.HasValue) return;

            string message;
            if (AwakeService.GetInstance().TimeRemaining > 3600)
            {
                message = $"{AwakeService.GetInstance().TimeRemaining / 3600}h";
            }
            else if (AwakeService.GetInstance().TimeRemaining > 60)
            {
                message = $"{AwakeService.GetInstance().TimeRemaining / 60}m";
            }
            else
            {
                message = $"{AwakeService.GetInstance().TimeRemaining}s";
            }

            Connection.SetTitleAsync(message, 2);
        }

        /// <inheritdoc />
        public void OnAwakeFailureOrCancelled()
        {
            string? errorMessage = "The keep-awake thread was terminated early.";
            Logger.Instance.LogMessage(TracingLevel.INFO, errorMessage);
            Logger.Instance.LogMessage(TracingLevel.DEBUG, errorMessage);
            Connection.ShowAlert().Wait();
            SetAwakeState(false).Wait();
        }

        /// <inheritdoc />
        public void OnAwakeSuccess(bool result)
        {
            Logger.Instance.LogMessage(TracingLevel.INFO, $"Exited keep-awake thread successfully: {result}");
            SetAwakeState(false).Wait();
        }
    }
}
