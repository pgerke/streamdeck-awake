using Newtonsoft.Json;

namespace PhilipGerke.StreamDeck.Awake
{
    /// <summary>
    ///     The settings for the Awake plugin.
    /// </summary>
    [Serializable]
    public sealed class Settings
    {
        /// <summary>
        ///     Gets or sets the path to the Awake executable.
        /// </summary>
        [JsonProperty("awakeExePath", Required = Required.Always)]
        public string AwakeExecutablePath { get; set; } = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"\PowerToys\modules\Awake\PowerToys.Awake.exe");

        /// <summary>
        ///     Gets or sets whether to use the PowerToys config from settings.json. If enabled, other options will be ignored.
        /// </summary>
        [JsonProperty("usePtConfig")]
        public bool? UsePtConfig { get; set; }

        /// <summary>
        ///     Gets or sets whether the screens should be kept on or off while the machine is kept awake.
        /// </summary>
        [JsonProperty("displayOn")]
        public bool? DisplayOn { get; set; }

        /// <summary>
        ///     Gets or sets the duration, in seconds, during which Awake keeps the computer awake. Can be used in combination with <see cref="DisplayOn"/>.
        /// </summary>
        [JsonProperty("timeLimit")]
        public uint? TimeLimit { get; set; }
    }
}
