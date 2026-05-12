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
        private Task? _antiRecoilTask;
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
                if (_antiRecoilTask != null && !_antiRecoilTask.IsCompleted)
                    return;

                _holdStartTimestamp = Stopwatch.GetTimestamp();
                _lastUpdateTimestamp = _holdStartTimestamp;
                _antiRecoilCts = new CancellationTokenSource();
                _antiRecoilTask = Task.Run(() => AntiRecoilLoop(_antiRecoilCts.Token), _antiRecoilCts.Token);
            }
        }

        public void Stop()
        {
            lock (_syncRoot)
            {
                _antiRecoilCts?.Cancel();
                _antiRecoilCts?.Dispose();
                _antiRecoilCts = null;
                _holdStartTimestamp = 0;
                _lastUpdateTimestamp = 0;
                MouseManager.ResetAntiRecoilState();
            }
        }

        private void AntiRecoilLoop(CancellationToken cancellationToken)
        {
            try
            {
                Thread.CurrentThread.Priority = ThreadPriority.Highest;
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
                        MouseManager.DoAntiRecoil(dtSeconds);
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
        }
    }
}