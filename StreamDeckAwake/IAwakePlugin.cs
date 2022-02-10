using BarRaider.SdTools;

namespace PhilipGerke.StreamDeck.Awake
{
    /// <summary>
    ///     Defines the properties and methods of the Awake plugin.
    /// </summary>
    public interface IAwakePlugin
    {
        /// <summary>
        ///     Gets the connection to the Stream Deck.
        /// </summary>
        public ISDConnection Connection { get; }

        /// <summary>
        ///     Gets the Stream Deck tools logger instance.
        /// </summary>
        public Logger Logger { get; }

        /// <summary>
        ///     The callback action to be executed when the Awake process was cancelled or failed.
        /// </summary>
        void OnAwakeFailureOrCancelled();

        /// <summary>
        ///     The callback action to be executed when the Awake process was completed normally.
        /// </summary>
        /// <param name="result">Determines if the process was completed successfully.</param>
        void OnAwakeSuccess(bool result);

        /// <summary>
        ///     Sets the new Awake state.
        /// </summary>
        /// <param name="enabled"><c>true</c>, if Awake shall be enabled, <c>false</c> if it shall be disabled and <c>null</c>, if the new state is unknown.</param>
        Task SetAwakeState(bool? enabled);
    }
}
