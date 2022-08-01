using BarRaider.SdTools;
using Newtonsoft.Json;
using System.Runtime.InteropServices;

namespace PhilipGerke.StreamDeck.Awake
{
    /// <summary>
    ///     The Awake service.
    /// </summary>
    /// <remarks>
    ///     This class allows talking to Win32 APIs without having to rely on PInvoke in other parts of the codebase.
    /// </remarks>
    public sealed class AwakeService
    {
        private const string AwakeFileName = ".awake";

        private static AwakeService? instance;
        private static readonly string awakeFilePath = Path.Combine(Path.GetTempPath(), AwakeFileName);
        private static readonly JsonSerializer serializer = new();
        private static CancellationTokenSource tokenSource = new();
        private static CancellationToken threadToken;

        private static Task? runnerThread;
        private static System.Timers.Timer timedLoopTimer = new();

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern ExecutionState SetThreadExecutionState(ExecutionState executionStateFlags);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        private static void CreateAwakeFile(bool displayOn, long? timerTicks = null)
        {
            FileStream awakeFileStream = File.Create(awakeFilePath);
            using StreamWriter streamWriter = new(awakeFileStream);
            serializer.Serialize(streamWriter, new AwakeFile() { DisplayOn = displayOn, Ticks = timerTicks });
            if (timerTicks is not null) streamWriter.WriteLine(timerTicks);
            streamWriter.Close();
        }

        private static void DeleteAwakeFile() => File.Delete(awakeFilePath);

        /// <summary>
        /// Gets the singleton <see cref="AwakeService"/> instance.
        /// </summary>
        /// <returns>The instance.</returns>
        public static AwakeService GetInstance()
        {
            instance ??= new();
            return instance;
        }

        private static AwakeFile? ReadAwakeFile()
        {
            if (!File.Exists(awakeFilePath))
            {
                return null;
            }

            using StreamReader streamReader = File.OpenText(awakeFilePath);
            using JsonReader jsonReader = new JsonTextReader(streamReader);
            try
            {
                return serializer.Deserialize<AwakeFile>(jsonReader);
            }
            catch // I don't really care if something goes wrong during deserialization
            {
                return null;
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

        private IAwakePlugin? plugin;

        /// <summary>
        ///     Disconnects the currently connected plugin instance from the service.
        /// </summary>
        public void ClearPluginInstance() => plugin = null;

        /// <summary>
        ///     Connects the specified plugin instance to the Awake service
        /// </summary>
        /// <remarks>An existing instance will be overridden.</remarks>
        /// <param name="instance">The <see cref="IAwakePlugin"/> instance to be connected.</param>
        public void SetPluginInstance(IAwakePlugin instance)
        {
            if (plugin != null)
            {
                plugin.Logger.LogMessage(TracingLevel.WARN, "Connection to the Awake service was overriden by another plugin instance.");
            }

            instance.Logger.LogMessage(TracingLevel.INFO, "Connected to the Awake service.");
            plugin = instance;

        }

        /// <summary>
        ///     Gets or sets the remaining time on a timed Awake, or <c>null</c>, if no timer is enabled.
        /// </summary>
        public uint? TimeRemaining { get; private set; }

        /// <summary>
        ///     Gets whether or not the machine is currently being kept awake.
        /// </summary>
        public static bool IsEnabled => !runnerThread?.IsCompleted ?? false;

        /// <summary>
        ///     Constructs a new instance of the <see cref="AwakeService"/>.
        /// </summary>
        private AwakeService() 
        {
            AwakeFile? awakeFile = ReadAwakeFile();
            if (awakeFile == null)
            {
                return;
            }

            if (awakeFile.Ticks.HasValue)
            {
                long expires = awakeFile.Ticks.Value - DateTimeOffset.UtcNow.Ticks;
                if (expires <= 0) // timer has expired already
                {
                    DeleteAwakeFile();
                    return;
                }

                var seconds = expires / TimeSpan.TicksPerSecond;
                StartAwakeTimed(Convert.ToUInt32(seconds), awakeFile.DisplayOn);
            }
            else
            {
                StartAwakeIndefinite(awakeFile.DisplayOn);
            }
        }

        /// <summary>
        ///     Starts keeping the machine awake indefinitely
        /// </summary>
        /// <param name="keepDisplayOn"><c>true</c>, if the display shall be kept on, <c>false</c> otherwise.</param>
        public void StartAwakeIndefinite(bool keepDisplayOn = false)
        {
            StartAwake("Confirmed background thread cancellation when setting indefinite keep awake.", () => RunIndefiniteLoop(keepDisplayOn));
        }

        /// <summary>
        ///     Starts keeping the machine awake for the specified number of seconds
        /// </summary>
        /// <param name="seconds">The number of seconds that the machine shall be kept awake.</param>
        /// <param name="keepDisplayOn"><c>true</c>, if the display shall be kept on, <c>false</c> otherwise.</param>
        public void StartAwakeTimed(uint seconds, bool keepDisplayOn = true)
        {
            StartAwake("Confirmed background thread cancellation when setting timed keep awake.", () => RunTimedLoop(seconds, keepDisplayOn));
        }

        /// <summary>
        ///     Stops keeping the machine awake.
        /// </summary>
        /// <param name="leaveAwakeFile">Determines if the Awake file shall be deleted.</param>
        public void StopAwake(bool leaveAwakeFile) => CancelRunnerThread("Confirmed background thread cancellation when disabling explicit keep awake.", leaveAwakeFile);

        private void CancelRunnerThread(string message, bool leaveAwakeFile)
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
                plugin?.Logger.LogMessage(TracingLevel.INFO, message);
            }
            
            if(!leaveAwakeFile) DeleteAwakeFile();
        }

        private bool RunIndefiniteLoop(bool keepDisplayOn)
        {
            bool success = SetAwakeState(keepDisplayOn);

            try
            {
                if (success)
                {
                    plugin?.Logger.LogMessage(TracingLevel.INFO, $"Initiated indefinite keep awake in background thread: {GetCurrentThreadId()}. Screen on: {keepDisplayOn}");
                    CreateAwakeFile(keepDisplayOn);

                    WaitHandle.WaitAny(new[] { threadToken.WaitHandle });

                    return success;
                }
                else
                {
                    plugin?.Logger.LogMessage(TracingLevel.INFO, "Could not successfully set up indefinite keep awake.");
                    return success;
                }
            }
            catch (OperationCanceledException ex)
            {
                plugin?.Logger.LogMessage(TracingLevel.INFO, $"Background thread termination: {GetCurrentThreadId()}. Message: {ex.Message}");
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
                    plugin?.Logger.LogMessage(TracingLevel.INFO, $"Initiated temporary keep awake in background thread: {GetCurrentThreadId()}. Screen on: {keepDisplayOn}");
                    CreateAwakeFile(keepDisplayOn, DateTimeOffset.UtcNow.AddSeconds(seconds).UtcTicks);

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
                        DeleteAwakeFile();
                    };

                    timedLoopTimer.Disposed += (s, e) =>
                    {
                        TimeRemaining = null;
                        plugin?.Connection.SetTitleAsync(string.Empty, 2);
                        plugin?.Logger.LogMessage(TracingLevel.INFO, "Old timer disposed.");
                    };

                    timedLoopTimer.Start();

                    WaitHandle.WaitAny(new[] { threadToken.WaitHandle });
                    timedLoopTimer.Stop();
                    timedLoopTimer.Dispose();

                    return success;
                }
                else
                {
                    plugin?.Logger.LogMessage(TracingLevel.INFO, "Could not set up timed keep-awake with display on.");
                    return success;
                }
            }
            catch (OperationCanceledException ex)
            {
                plugin?.Logger.LogMessage(TracingLevel.INFO, $"Background thread termination: {GetCurrentThreadId()}. Message: {ex.Message}");
                return success;
            }
        }

        private void StartAwake(string cancellationMessage, Func<bool> runnerFunction)
        {
            // Cancel existing runner thread (if exists)
            CancelRunnerThread(cancellationMessage, false);

            // Recreate token source and token
            tokenSource = new CancellationTokenSource();
            threadToken = tokenSource.Token;

            // Create new runner thread
            runnerThread = Task.Run(runnerFunction, threadToken)
                .ContinueWith((result) => plugin?.OnAwakeSuccess(result.Result), TaskContinuationOptions.OnlyOnRanToCompletion)
                .ContinueWith((result) => plugin?.OnAwakeFailureOrCancelled(), TaskContinuationOptions.NotOnRanToCompletion);            
            plugin?.SetAwakeState(true).Wait();
        }
    }
}
