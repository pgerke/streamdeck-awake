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
        ///     Gets or sets whether the screens should be kept on or off while the machine is kept awake.
        /// </summary>
        [JsonProperty("displayOn")]
        public bool? DisplayOn { get; set; }

        /// <summary>
        ///     Gets or sets the duration, in seconds, during which Awake keeps the computer awake. Can be used in combination with <see cref="DisplayOn"/>.
        /// </summary>
        [JsonProperty("timeLimit")]
        public string? TimeLimit { get; set; }
    }
}
