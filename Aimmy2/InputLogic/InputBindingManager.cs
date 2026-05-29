using Gma.System.MouseKeyHook;
using System.Threading;
using System.Windows.Forms;

namespace InputLogic
{
    internal class InputBindingManager
    {
        private IKeyboardMouseEvents? _mEvents;
        private readonly Dictionary<string, string> bindings = [];
        private static readonly Dictionary<string, bool> isHolding = [];
        private string? settingBindingId = null;

        // The global hook runs on its own dedicated thread with its own message pump.
        // This keeps system-wide input (including the Windows taskbar) responsive even when
        // Aimmy's UI thread is busy, because the low-level hook callback is never blocked by it.
        private Thread? _hookThread;
        private readonly object _sync = new();
        private bool _hookStarted;

        public event Action<string, string>? OnBindingSet;

        public event Action<string>? OnBindingPressed;

        public event Action<string>? OnBindingReleased;

        public static bool IsHoldingBinding(string bindingId) => isHolding.TryGetValue(bindingId, out bool holding) && holding;

        public void SetupDefault(string bindingId, string keyCode)
        {
            lock (_sync)
            {
                bindings[bindingId] = keyCode;
                isHolding[bindingId] = false;
            }
            OnBindingSet?.Invoke(bindingId, keyCode);
            EnsureHookEvents();
        }

        public void StartListeningForBinding(string bindingId)
        {
            lock (_sync)
            {
                settingBindingId = bindingId;
            }
            EnsureHookEvents();
        }

        private void EnsureHookEvents()
        {
            lock (_sync)
            {
                if (_hookStarted) return;
                _hookStarted = true;
            }

            _hookThread = new Thread(() =>
            {
                _mEvents = Hook.GlobalEvents();
                _mEvents.KeyDown += GlobalHookKeyDown!;
                _mEvents.MouseDown += GlobalHookMouseDown!;
                _mEvents.KeyUp += GlobalHookKeyUp!;
                _mEvents.MouseUp += GlobalHookMouseUp!;

                // Pump messages so the low-level hook callbacks are serviced on this thread.
                System.Windows.Forms.Application.Run();
            })
            {
                IsBackground = true,
                Name = "AimmyInputHook"
            };
            _hookThread.SetApartmentState(ApartmentState.STA);
            _hookThread.Start();
        }

        // Raise UI-facing events on the WPF UI thread without blocking the hook thread.
        private static void RaiseOnUi(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                action();
            else
                dispatcher.BeginInvoke(action);
        }

        private void GlobalHookKeyDown(object sender, KeyEventArgs e) => HandleDown(e.KeyCode.ToString());

        private void GlobalHookMouseDown(object sender, MouseEventArgs e) => HandleDown(e.Button.ToString());

        private void GlobalHookKeyUp(object sender, KeyEventArgs e) => HandleUp(e.KeyCode.ToString());

        private void GlobalHookMouseUp(object sender, MouseEventArgs e) => HandleUp(e.Button.ToString());

        private void HandleDown(string input)
        {
            string? bindingToSet = null;
            var pressed = new List<string>();

            lock (_sync)
            {
                if (settingBindingId != null)
                {
                    bindingToSet = settingBindingId;
                    bindings[settingBindingId] = input;
                    settingBindingId = null;
                }
                else
                {
                    foreach (var binding in bindings)
                    {
                        if (binding.Value == input)
                        {
                            isHolding[binding.Key] = true;
                            pressed.Add(binding.Key);
                        }
                    }
                }
            }

            if (bindingToSet != null)
            {
                RaiseOnUi(() => OnBindingSet?.Invoke(bindingToSet, input));
            }
            else
            {
                foreach (var key in pressed)
                    RaiseOnUi(() => OnBindingPressed?.Invoke(key));
            }
        }

        private void HandleUp(string input)
        {
            var released = new List<string>();

            lock (_sync)
            {
                foreach (var binding in bindings)
                {
                    if (binding.Value == input)
                    {
                        isHolding[binding.Key] = false;
                        released.Add(binding.Key);
                    }
                }
            }

            foreach (var key in released)
                RaiseOnUi(() => OnBindingReleased?.Invoke(key));
        }

        public void StopListening()
        {
            var events = _mEvents;
            if (events != null)
            {
                events.KeyDown -= GlobalHookKeyDown!;
                events.MouseDown -= GlobalHookMouseDown!;
                events.KeyUp -= GlobalHookKeyUp!;
                events.MouseUp -= GlobalHookMouseUp!;
                events.Dispose();
                _mEvents = null;
            }
            // The hook thread is a background thread; with the hook disposed it idles and is
            // reclaimed automatically when the app exits.
        }
    }
}
