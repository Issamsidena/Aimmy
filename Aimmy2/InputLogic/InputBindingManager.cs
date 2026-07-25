using Gma.System.MouseKeyHook;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace InputLogic
{
    internal class InputBindingManager
    {
        private IKeyboardMouseEvents? _mEvents;
        private readonly Dictionary<string, string> bindings = [];

        // Written on the hook thread, read lock-free from the aim loop, the anti-recoil loop and the
        // rapid fire loop, so it has to be a concurrent collection rather than a plain Dictionary.
        private static readonly ConcurrentDictionary<string, bool> isHolding = new();

        // Virtual key code of the physical input that put each binding into the held state, so the
        // stuck-key watchdog can re-check it against the real keyboard.
        private static readonly ConcurrentDictionary<string, int> heldVirtualKeys = new();

        private string? settingBindingId = null;

        // The global hook runs on its own dedicated thread with its own message pump.
        // This keeps system-wide input (including the Windows taskbar) responsive even when
        // Aimmy's UI thread is busy, because the low-level hook callback is never blocked by it.
        private Thread? _hookThread;
        private readonly object _sync = new();
        private bool _hookStarted;

        // The manager that owns the hook, so the watchdog can raise release events for the bindings it
        // force-clears. There is only ever one (MainWindow holds it in a Lazy).
        private static InputBindingManager? _active;

        public event Action<string, string>? OnBindingSet;

        public event Action<string>? OnBindingPressed;

        public event Action<string>? OnBindingReleased;

        #region Stuck key watchdog

        // A low-level hook receives nothing while the secure desktop is up (UAC prompt, Ctrl-Alt-Del),
        // and Windows silently detaches a hook that overruns LowLevelHooksTimeout. Either one can eat a
        // key-UP and leave a binding held forever, with aim assist / anti-recoil / rapid fire running
        // and no way to stop them. So re-check held bindings against the real keyboard every so often.
        private const long WatchdogIntervalMs = 100;

        private static long _lastWatchdogTick;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        public static bool IsHoldingBinding(string bindingId)
        {
            if (!isHolding.TryGetValue(bindingId, out bool holding) || !holding)
                return false;

            // Only ever runs while something is held, and at most once per interval, because the aim
            // loop polls this every frame.
            ReconcileHeldBindings();

            return isHolding.TryGetValue(bindingId, out holding) && holding;
        }

        private static void ReconcileHeldBindings()
        {
            long now = Environment.TickCount64;
            long last = Interlocked.Read(ref _lastWatchdogTick);
            if (now - last < WatchdogIntervalMs) return;
            if (Interlocked.CompareExchange(ref _lastWatchdogTick, now, last) != last) return;

            foreach (var held in isHolding)
            {
                if (!held.Value) continue;

                if (!heldVirtualKeys.TryGetValue(held.Key, out int virtualKey)) continue;
                if (virtualKey == 0) continue;                               // cannot be verified, leave it alone
                if ((GetAsyncKeyState(virtualKey) & 0x8000) != 0) continue;  // still physically down

                isHolding[held.Key] = false;
                heldVirtualKeys.TryRemove(held.Key, out _);

                // Let listeners know, the same way a real key-UP would have.
                if (_active?.OnBindingReleased is Action<string> release)
                {
                    string releasedBinding = held.Key;
                    RaiseOnUi(() => release(releasedBinding));
                }
            }
        }

        // Maps the hook's input name to a virtual key code. The two name spaces overlap -- "Left" is both
        // MouseButtons.Left and Keys.Left (arrow) -- so which event it came from decides. 0 means
        // "unknown", in which case the watchdog leaves that binding alone rather than guessing.
        private static int GetVirtualKey(string input, bool fromMouse)
        {
            if (fromMouse)
            {
                return input switch
                {
                    "Left" => 0x01,      // VK_LBUTTON
                    "Right" => 0x02,     // VK_RBUTTON
                    "Middle" => 0x04,    // VK_MBUTTON
                    "XButton1" => 0x05,  // VK_XBUTTON1
                    "XButton2" => 0x06,  // VK_XBUTTON2
                    _ => 0
                };
            }

            if (!Enum.TryParse(input, out Keys key) || !Enum.IsDefined(typeof(Keys), key))
                return 0;

            int virtualKey = (int)key;
            return virtualKey > 0 && virtualKey <= 0xFF ? virtualKey : 0;
        }

        #endregion

        // Rapid Fire injects synthetic mouse clicks. When its keybind is a mouse button (e.g. "Left"),
        // those clicks echo back through this global hook and would corrupt the hold state. We record
        // a bounded, self-expiring count of expected echoes per button and drop them when they arrive.
        private static readonly Dictionary<string, Queue<long>> _injectedEchoes = new();
        private static readonly object _echoSync = new();
        private const long InjectedEchoTtlMs = 100;

        // Call once per synthetic click pair (down + up) before injecting.
        public static void RegisterInjectedClick(string button)
        {
            lock (_echoSync)
            {
                if (!_injectedEchoes.TryGetValue(button, out var q))
                {
                    q = new Queue<long>();
                    _injectedEchoes[button] = q;
                }
                long now = Environment.TickCount64;
                q.Enqueue(now); // down echo
                q.Enqueue(now); // up echo
            }
        }

        private static bool ConsumeInjectedEcho(string input)
        {
            lock (_echoSync)
            {
                if (!_injectedEchoes.TryGetValue(input, out var q) || q.Count == 0)
                    return false;

                long now = Environment.TickCount64;
                while (q.Count > 0 && now - q.Peek() > InjectedEchoTtlMs)
                    q.Dequeue();

                if (q.Count == 0)
                    return false;

                q.Dequeue();
                return true;
            }
        }

        public void SetupDefault(string bindingId, string keyCode)
        {
            lock (_sync)
            {
                bindings[bindingId] = keyCode;
                isHolding[bindingId] = false;
                heldVirtualKeys.TryRemove(bindingId, out _);
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
                _active ??= this;
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

        private void GlobalHookKeyDown(object sender, KeyEventArgs e) => HandleDown(e.KeyCode.ToString(), false);

        private void GlobalHookMouseDown(object sender, MouseEventArgs e) => HandleDown(e.Button.ToString(), true);

        private void GlobalHookKeyUp(object sender, KeyEventArgs e) => HandleUp(e.KeyCode.ToString());

        private void GlobalHookMouseUp(object sender, MouseEventArgs e) => HandleUp(e.Button.ToString());

        private void HandleDown(string input, bool fromMouse)
        {
            // Drop echoes from our own injected clicks. The echo is always consumed, even while the user
            // is rebinding, so that this down stays paired with its up in the per-button queue -- skipping
            // it here used to leave the queue one entry long forever and swallow the next real click.
            bool isRebinding = settingBindingId != null;
            bool isInjectedEcho = ConsumeInjectedEcho(input);

            // While rebinding the input still has to come through so it can be captured as the binding.
            if (isInjectedEcho && !isRebinding)
                return;

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
                            heldVirtualKeys[binding.Key] = GetVirtualKey(input, fromMouse);
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
            if (ConsumeInjectedEcho(input))
                return;

            var released = new List<string>();

            lock (_sync)
            {
                foreach (var binding in bindings)
                {
                    if (binding.Value == input)
                    {
                        isHolding[binding.Key] = false;
                        heldVirtualKeys.TryRemove(binding.Key, out _);
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
