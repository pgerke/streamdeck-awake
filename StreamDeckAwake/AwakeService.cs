using BarRaider.SdTools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace PhilipGerke.StreamDeck.Awake
{
    [Flags]
    public enum EXECUTION_STATE : uint
    {
        ES_AWAYMODE_REQUIRED = 0x00000040,
        ES_CONTINUOUS = 0x80000000,
        ES_DISPLAY_REQUIRED = 0x00000002,
        ES_SYSTEM_REQUIRED = 0x00000001,
    }

    //// See: https://docs.microsoft.com/windows/console/handlerroutine
    //public enum ControlType
    //{
    //    CTRL_C_EVENT = 0,
    //    CTRL_BREAK_EVENT = 1,
    //    CTRL_CLOSE_EVENT = 2,
    //    CTRL_LOGOFF_EVENT = 5,
    //    CTRL_SHUTDOWN_EVENT = 6,
    //}

    //public delegate bool ConsoleEventHandler(ControlType ctrlType);

    /// <summary>
    ///     This class allows talking to Win32 APIs without having to rely on PInvoke in other parts of the codebase.
    /// </summary>
    public class AwakeService
    {
        //private const int StdOutputHandle = -11;
        //private const uint GenericWrite = 0x40000000;
        //private const uint GenericRead = 0x80000000;

        private static CancellationTokenSource tokenSource = new();
        private static CancellationToken threadToken;

        private static Task? runnerThread;
        private static System.Timers.Timer timedLoopTimer = new();

        //[DllImport("kernel32.dll", SetLastError = true)]
        //private static extern bool SetConsoleCtrlHandler(ConsoleEventHandler handler, bool add);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

        //[DllImport("kernel32.dll", SetLastError = true)]
        //[return: MarshalAs(UnmanagedType.Bool)]
        //private static extern bool AllocConsole();

        //[DllImport("kernel32.dll", SetLastError = true)]
        //private static extern bool SetStdHandle(int nStdHandle, IntPtr hHandle);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        //[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        //private static extern IntPtr CreateFile(
        //    [MarshalAs(UnmanagedType.LPTStr)] string filename,
        //    [MarshalAs(UnmanagedType.U4)] uint access,
        //    [MarshalAs(UnmanagedType.U4)] FileShare share,
        //    IntPtr securityAttributes,
        //    [MarshalAs(UnmanagedType.U4)] FileMode creationDisposition,
        //    [MarshalAs(UnmanagedType.U4)] FileAttributes flagsAndAttributes,
        //    IntPtr templateFile);

        private readonly ISDConnection connection;
        private readonly Logger logger;

        public AwakeService(Logger logger, ISDConnection connection)
        {
            this.connection = connection;            
            this.logger = logger;
        }

        //public static void SetConsoleControlHandler(ConsoleEventHandler handler, bool addHandler)
        //{
        //    SetConsoleCtrlHandler(handler, addHandler);
        //}

        //public static void AllocateConsole(Logger logger)
        //{
        //    logger.LogMessage(TracingLevel.DEBUG, "Bootstrapping the console allocation routine.");
        //    AllocConsole();
        //    logger.LogMessage(TracingLevel.DEBUG, $"Console allocation result: {Marshal.GetLastWin32Error()}");

        //    var outputFilePointer = CreateFile("CONOUT$", GenericRead | GenericWrite, FileShare.Write, IntPtr.Zero, FileMode.OpenOrCreate, 0, IntPtr.Zero);
        //    logger.LogMessage(TracingLevel.DEBUG, $"CONOUT creation result: {Marshal.GetLastWin32Error()}");

        //    SetStdHandle(StdOutputHandle, outputFilePointer);
        //    logger.LogMessage(TracingLevel.DEBUG, $"SetStdHandle result: {Marshal.GetLastWin32Error()}");

        //    Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), Console.OutputEncoding) { AutoFlush = true });
        //}

        public void StartAwakeIndefinite(Action<bool> onCompletion, Action onFailure, bool keepDisplayOn = false)
        {
            StartAwake("Confirmed background thread cancellation when setting indefinite keep awake.", () => RunIndefiniteLoop(keepDisplayOn), onCompletion, onFailure);
        }

        public void StartAwakeTimed(uint seconds, Action<bool> onCompletion, Action onFailure, bool keepDisplayOn = true)
        {
            StartAwake("Confirmed background thread cancellation when setting timed keep awake.", () => RunTimedLoop(seconds, keepDisplayOn), onCompletion, onFailure);
        }

        public void StopAwake()
        {
            CancelRunnerThread("Confirmed background thread cancellation when disabling explicit keep awake.");
        }

        private void CancelRunnerThread(string message)
        {
            tokenSource.Cancel();

            try
            {
                if (runnerThread != null && !runnerThread.IsCanceled)
                {
                    runnerThread.Wait(threadToken);
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogMessage(TracingLevel.INFO, message);
            }
        }

        private bool RunIndefiniteLoop(bool keepDisplayOn)
        {
            bool success = SetAwakeState(keepDisplayOn);

            try
            {
                if (success)
                {
                    logger.LogMessage(TracingLevel.INFO, $"Initiated indefinite keep awake in background thread: {GetCurrentThreadId()}. Screen on: {keepDisplayOn}");

                    connection.SetStateAsync(2).ConfigureAwait(true);
                    WaitHandle.WaitAny(new[] { threadToken.WaitHandle });

                    return success;
                }
                else
                {
                    logger.LogMessage(TracingLevel.INFO, "Could not successfully set up indefinite keep awake.");
                    return success;
                }
            }
            catch (OperationCanceledException ex)
            {
                logger.LogMessage(TracingLevel.INFO, $"Background thread termination: {GetCurrentThreadId()}. Message: {ex.Message}");
                return success;
            }
        }

        private bool RunTimedLoop(uint seconds, bool keepDisplayOn)
        {
            bool success = false;

            // In case cancellation was already requested
            threadToken.ThrowIfCancellationRequested();
            try
            {
                success = SetAwakeState(keepDisplayOn);

                if (success)
                {
                    logger.LogMessage(TracingLevel.INFO, $"Initiated temporary keep awake in background thread: {GetCurrentThreadId()}. Screen on: {keepDisplayOn}");

                    connection.SetStateAsync(2).ConfigureAwait(true);
                    timedLoopTimer = new System.Timers.Timer(seconds * 1000);
                    timedLoopTimer.Elapsed += (s, e) =>
                    {
                        tokenSource.Cancel();

                        timedLoopTimer.Stop();
                    };

                    timedLoopTimer.Disposed += (s, e) =>
                    {
                        logger.LogMessage(TracingLevel.INFO, "Old timer disposed.");
                    };

                    timedLoopTimer.Start();

                    WaitHandle.WaitAny(new[] { threadToken.WaitHandle });
                    timedLoopTimer.Stop();
                    timedLoopTimer.Dispose();

                    return success;
                }
                else
                {
                    logger.LogMessage(TracingLevel.INFO, "Could not set up timed keep-awake with display on.");
                    return success;
                }
            }
            catch (OperationCanceledException ex)
            {
                logger.LogMessage(TracingLevel.INFO, $"Background thread termination: {GetCurrentThreadId()}. Message: {ex.Message}");
                return success;
            }
        }

        /// <summary>
        ///     Sets the computer awake state using the native Win32 SetThreadExecutionState API. This
        ///     function is just a nice-to-have wrapper that helps avoid tracking the success or failure of
        ///     the call.
        /// </summary>
        /// <param name="keepDisplayOn"><c>true</c>, if the display shall be kept on, <c>false</c> otherwise.</param>
        /// <returns><c>true</c>, if the state was set successfully, <c>false</c> otherwise.</returns>
        private static bool SetAwakeState(bool keepDisplayOn)
        {
            static bool SetAwakeState(EXECUTION_STATE state)
            {
                try
                {
                    var stateResult = SetThreadExecutionState(state);
                    return stateResult != 0;
                }
                catch
                {
                    return false;
                }
            }
            return keepDisplayOn
                ? SetAwakeState(EXECUTION_STATE.ES_SYSTEM_REQUIRED | EXECUTION_STATE.ES_DISPLAY_REQUIRED | EXECUTION_STATE.ES_CONTINUOUS)
                : SetAwakeState(EXECUTION_STATE.ES_SYSTEM_REQUIRED | EXECUTION_STATE.ES_CONTINUOUS);
        }

        private void StartAwake(string cancellationMessage, Func<bool> runnerFunction, Action<bool> onCompletion, Action onFailure)
        {
            // Cancel existing runner thread (if exists)
            CancelRunnerThread(cancellationMessage);

            // Recreate token source and token
            tokenSource = new CancellationTokenSource();
            threadToken = tokenSource.Token;

            // Create new runner thread
            runnerThread = Task.Run(runnerFunction, threadToken)
                .ContinueWith((result) => onCompletion(result.Result), TaskContinuationOptions.OnlyOnRanToCompletion)
                .ContinueWith((result) => onFailure, TaskContinuationOptions.NotOnRanToCompletion);
        }
    }
}
