using BarRaider.SdTools;

namespace PhilipGerke.StreamDeck.Awake
{
    /// <summary>
    ///     The entry point class.
    /// </summary>
    public sealed class Program
    {
        private static void Main(string[] args)
        {
#if DEBUG
            while (!System.Diagnostics.Debugger.IsAttached)
            {
                System.Threading.Thread.Sleep(100);
            }
#endif
            SDWrapper.Run(args);
        }
    }
}
