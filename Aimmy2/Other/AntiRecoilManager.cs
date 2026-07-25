using Aimmy2.Class;
using InputLogic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Other
{
    public class AntiRecoilManager
    {
        private CancellationTokenSource? _antiRecoilCts;
        // A dedicated thread, not Task.Run: this loop raises its own priority, and doing that on a
        // pooled thread leaked the elevated priority back into the thread pool for unrelated work.
        private Thread? _antiRecoilThread;
        private readonly object _syncRoot = new();
        private long _holdStartTimestamp;
        private long _lastUpdateTimestamp;

        public void HoldDownLoad()
        {
            // Kept for compatibility with existing startup flow.
        }

        public void Start()
        {
            lock (_syncRoot)
            {
                if (_antiRecoilThread != null && _antiRecoilThread.IsAlive)
                    return;

                _holdStartTimestamp = Stopwatch.GetTimestamp();
                _lastUpdateTimestamp = _holdStartTimestamp;
                _antiRecoilCts = new CancellationTokenSource();

                var token = _antiRecoilCts.Token;
                _antiRecoilThread = new Thread(() => AntiRecoilLoop(token))
                {
                    IsBackground = true,
                    Priority = ThreadPriority.AboveNormal,
                    Name = "AntiRecoilLoop"
                };
                _antiRecoilThread.Start();
            }
        }

        public void Stop()
        {
            lock (_syncRoot)
            {
                _antiRecoilCts?.Cancel();
                _antiRecoilCts?.Dispose();
                _antiRecoilCts = null;
                // Clear the handle too. Leaving a completed thread here made Start()'s guard see a
                // stale reference and return early, silently killing anti-recoil for the session.
                _antiRecoilThread = null;
                _holdStartTimestamp = 0;
                _lastUpdateTimestamp = 0;
                MouseManager.ResetAntiRecoilState();
            }
        }

        private void AntiRecoilLoop(CancellationToken cancellationToken)
        {
            try
            {
                bool wasHolding = true;

                while (!cancellationToken.IsCancellationRequested)
                {
                    bool isHolding = InputBindingManager.IsHoldingBinding("Anti Recoil Keybind");
                    if (!Dictionary.toggleState["Anti Recoil"] || !isHolding)
                    {
                        if (wasHolding)
                        {
                            _holdStartTimestamp = 0;
                            _lastUpdateTimestamp = 0;
                            MouseManager.ResetAntiRecoilState();
                            wasHolding = false;
                        }

                        Thread.Sleep(1);
                        continue;
                    }

                    if (!wasHolding || _holdStartTimestamp == 0)
                    {
                        _holdStartTimestamp = Stopwatch.GetTimestamp();
                        _lastUpdateTimestamp = _holdStartTimestamp;
                        wasHolding = true;
                    }

                    long now = Stopwatch.GetTimestamp();
                    double holdTimeMs = Math.Max(0.0, Convert.ToDouble(Dictionary.AntiRecoilSettings["Hold Time"]));
                    double holdElapsedMs = (now - _holdStartTimestamp) * 1000.0 / Stopwatch.Frequency;

                    if (holdElapsedMs >= holdTimeMs)
                    {
                        // dt-driven recoil: value controls movement per second; FPS/timer jitter won't change speed.
                        double dtSeconds = (now - _lastUpdateTimestamp) / (double)Stopwatch.Frequency;
                        _lastUpdateTimestamp = now;
                        double sustainedAfterDelaySec = Math.Max(0.0, (holdElapsedMs - holdTimeMs) / 1000.0);
                        MouseManager.DoAntiRecoil(dtSeconds, sustainedAfterDelaySec);
                    }
                    else
                    {
                        // Avoid a "first tick jump" right when hold time elapses.
                        _lastUpdateTimestamp = now;
                    }

                    Thread.Sleep(1);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown path.
            }
            catch (Exception ex)
            {
                // Anything else used to end the loop permanently and silently -- anti-recoil simply
                // stopped working for the rest of the session with no message.
                LogManager.Log(LogManager.LogLevel.Error,
                    $"Anti-Recoil stopped: {ex.Message}", true, 4000);
            }
            finally
            {
                MouseManager.ResetAntiRecoilState();
            }
        }
    }
}
