using BarRaider.SdTools;
using System.Runtime.InteropServices;

namespace PhilipGerke.StreamDeck.Awake
{
    /// <summary>
    ///     The Awake service.
    /// </summary>
    /// <remarks>
    ///     This class allows talking to Win32 APIs without having to rely on PInvoke in other parts of the codebase.
    /// </remarks>
    public class AwakeService
    {
        private static CancellationTokenSource tokenSource = new();
        private static CancellationToken threadToken;

        private static Task? runnerThread;
        private static System.Timers.Timer timedLoopTimer = new();

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern ExecutionState SetThreadExecutionState(ExecutionState executionStateFlags);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        /// <summary>
        ///     Sets the computer awake state using the native Win32 SetThreadExecutionState API. This
        ///     function is just a nice-to-have wrapper that helps avoid tracking the success or failure of
        ///     the call.
        /// </summary>
        /// <param name="keepDisplayOn"><c>true</c>, if the display shall be kept on, <c>false</c> otherwise.</param>
        /// <returns><c>true</c>, if the state was set successfully, <c>false</c> otherwise.</returns>
        private static bool SetAwakeState(bool keepDisplayOn)
        {
            static bool SetAwakeState(ExecutionState state)
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
                ? SetAwakeState(ExecutionState.SystemRequired | ExecutionState.DisplayRequired | ExecutionState.Continuous)
                : SetAwakeState(ExecutionState.SystemRequired | ExecutionState.Continuous);
        }

        private readonly IAwakePlugin plugin;

        /// <summary>
        ///     Gets or sets the remaining time on a timed Awake, or <c>null</c>, if no timer is enabled.
        /// </summary>
        public uint? TimeRemaining { get; private set; }

        /// <summary>
        ///     Constructs a new instance of the <see cref="AwakeService"/>.
        /// </summary>
        /// <param name="plugin">The instance of the Awake plugin.</param>
        public AwakeService(IAwakePlugin plugin)
        {
            this.plugin = plugin;
        }

        /// <summary>
        ///     Starts keeping the machine awake indefinitely
        /// </summary>
        /// <param name="onCompletion">The callback action to be executed when the keep awake task has completed successfully.</param>
        /// <param name="onFailure">The callback action to be executed when the keep awake task ended prematurely.</param>
        /// <param name="keepDisplayOn"><c>true</c>, if the display shall be kept on, <c>false</c> otherwise.</param>
        public void StartAwakeIndefinite(Action<bool> onCompletion, Action onFailure, bool keepDisplayOn = false)
        {
            StartAwake("Confirmed background thread cancellation when setting indefinite keep awake.", () => RunIndefiniteLoop(keepDisplayOn), onCompletion, onFailure);
        }

        /// <summary>
        ///     Starts keeping the machine awake for the specified number of seconds
        /// </summary>
        /// <param name="seconds">The number of seconds that the machine shall be kept awake.</param>
        /// <param name="onCompletion">The callback action to be executed when the keep awake task has completed successfully.</param>
        /// <param name="onFailure">The callback action to be executed when the keep awake task ended prematurely.</param>
        /// <param name="keepDisplayOn"><c>true</c>, if the display shall be kept on, <c>false</c> otherwise.</param>
        public void StartAwakeTimed(uint seconds, Action<bool> onCompletion, Action onFailure, bool keepDisplayOn = true)
        {
            StartAwake("Confirmed background thread cancellation when setting timed keep awake.", () => RunTimedLoop(seconds, keepDisplayOn), onCompletion, onFailure);
        }

        /// <summary>
        ///     Stops keeping the machine awake.
        /// </summary>
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
                plugin.Logger.LogMessage(TracingLevel.INFO, message);
            }
            plugin.SetAwakeState(false).Wait();
        }

        private bool RunIndefiniteLoop(bool keepDisplayOn)
        {
            bool success = SetAwakeState(keepDisplayOn);

            try
            {
                if (success)
                {
                    plugin.Logger.LogMessage(TracingLevel.INFO, $"Initiated indefinite keep awake in background thread: {GetCurrentThreadId()}. Screen on: {keepDisplayOn}");

                    WaitHandle.WaitAny(new[] { threadToken.WaitHandle });

                    return success;
                }
                else
                {
                    plugin.Logger.LogMessage(TracingLevel.INFO, "Could not successfully set up indefinite keep awake.");
                    return success;
                }
            }
            catch (OperationCanceledException ex)
            {
                plugin.Logger.LogMessage(TracingLevel.INFO, $"Background thread termination: {GetCurrentThreadId()}. Message: {ex.Message}");
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
                    plugin.Logger.LogMessage(TracingLevel.INFO, $"Initiated temporary keep awake in background thread: {GetCurrentThreadId()}. Screen on: {keepDisplayOn}");

                    TimeRemaining = seconds;
                    uint elapsedSeconds = 0;
                    timedLoopTimer = new System.Timers.Timer(1000)
                    {
                        AutoReset = true,
                    }; // 1s timers
                    timedLoopTimer.Elapsed += (s, e) =>
                    {
                        elapsedSeconds++;
                        TimeRemaining = seconds - elapsedSeconds;
                        if (elapsedSeconds < seconds) return;

                        tokenSource.Cancel();
                        timedLoopTimer.Stop();
                    };

                    timedLoopTimer.Disposed += (s, e) =>
                    {
                        TimeRemaining = null;
                        plugin.Connection.SetTitleAsync(string.Empty, 2);
                        plugin.Logger.LogMessage(TracingLevel.INFO, "Old timer disposed.");
                    };

                    timedLoopTimer.Start();

                    WaitHandle.WaitAny(new[] { threadToken.WaitHandle });
                    timedLoopTimer.Stop();
                    timedLoopTimer.Dispose();

                    return success;
                }
                else
                {
                    plugin.Logger.LogMessage(TracingLevel.INFO, "Could not set up timed keep-awake with display on.");
                    return success;
                }
            }
            catch (OperationCanceledException ex)
            {
                plugin.Logger.LogMessage(TracingLevel.INFO, $"Background thread termination: {GetCurrentThreadId()}. Message: {ex.Message}");
                return success;
            }
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
            plugin.SetAwakeState(true).Wait();
        }
    }
}
