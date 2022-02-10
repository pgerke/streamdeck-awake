using Newtonsoft.Json;

namespace PhilipGerke.StreamDeck.Awake
{
    /// <summary>
    ///     The serialization class for the persisted awake settings.
    /// </summary>
    [Serializable]
    internal sealed class AwakeFile
    {
        /// <summary>
        ///     Gets or sets whether the screens should be kept on or off while the machine is kept awake.
        /// </summary>
        [JsonProperty("displayOn")]
        public bool DisplayOn { get; set; }

        /// <summary>
        ///     Gets or sets the ticks representing the <see cref="DateTimeOffset"/> when the Awake timer expires in UTC.
        /// </summary>
        [JsonProperty("ticks")]
        public long? Ticks { get; set; }
    }
}
