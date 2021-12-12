namespace PhilipGerke.StreamDeck.Awake
{
    /// <summary>
    ///     The execution state.
    /// </summary>
    [Flags]
    public enum ExecutionState : uint
    {
        /// <summary>
        ///     Away mode required.
        /// </summary>
        AwayModeRequired = 0x00000040,
        /// <summary>
        ///     Continuous
        /// </summary>
        Continuous = 0x80000000,
        /// <summary>
        ///     Display required.
        /// </summary>
        DisplayRequired = 0x00000002,
        /// <summary>
        ///     System required.
        /// </summary>
        SystemRequired = 0x00000001,
    }
}
