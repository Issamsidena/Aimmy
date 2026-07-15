using Aimmy2.Class;
using InputLogic;
using System.Threading;
using System.Threading.Tasks;

namespace Other
{
    public class RapidFireManager
    {
        private CancellationTokenSource? _cts;
        private Task? _task;
        private readonly object _syncRoot = new();

        public void Start()
        {
            lock (_syncRoot)
            {
                if (_task != null && !_task.IsCompleted)
                    return;

                _cts = new CancellationTokenSource();
                _task = Task.Run(() => RapidFireLoop(_cts.Token), _cts.Token);
            }
        }

        public void Stop()
        {
            lock (_syncRoot)
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;
            }
        }

        private async Task RapidFireLoop(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    bool enabled = Dictionary.toggleState.TryGetValue("Rapid Fire", out var v) && (bool)v;
                    bool holding = InputBindingManager.IsHoldingBinding("Rapid Fire Keybind");

                    if (!enabled || !holding)
                    {
                        await Task.Delay(5, cancellationToken);
                        continue;
                    }

                    await MouseManager.DoRapidFireClick();

                    int delay = 50;
                    if (Dictionary.sliderSettings.TryGetValue("Rapid Fire Delay", out var d))
                        delay = (int)System.Convert.ToDouble(d);
                    delay = System.Math.Clamp(delay, 1, 500);

                    await Task.Delay(delay, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown path.
            }
        }
    }
}
