using Gma.System.MouseKeyHook;
using System.Windows.Forms;
using MouseMovementLibraries.MakcuSupport;

namespace InputLogic
{
    internal class InputBindingManager
    {
        private IKeyboardMouseEvents? _mEvents;
        private readonly Dictionary<string, string> bindings = [];
        private static readonly Dictionary<string, bool> isHolding = [];
        private string? settingBindingId = null;
        // Track expected release times for simulated events
        private static readonly Dictionary<MouseButtons, DateTime> _expectedReleaseTimes = new();
        private static readonly object _lock = new();
        private const int SYNTHETIC_WINDOW_MS = 10;

        public event Action<string, string>? OnBindingSet;

        private const string MakcuButtonPrefix = "Makcu_";

        public event Action<string>? OnBindingPressed;

        public event Action<string>? OnBindingReleased;

        public static bool IsHoldingBinding(string bindingId) => isHolding.TryGetValue(bindingId, out bool holding) && holding;

        public static void MarkExpectedRelease(MouseButtons button, DateTime releaseTime)
        {
            lock (_lock)
            {
                _expectedReleaseTimes[button] = releaseTime;
            }
        }
        private bool IsSyntheticRelease(MouseButtons button)
        {
            lock (_lock)
            {
                if (_expectedReleaseTimes.TryGetValue(button, out DateTime expectedTime))
                {
                    // Calculate how far off this release is from the expected time
                    double timeDiff = Math.Abs((DateTime.UtcNow - expectedTime).TotalMilliseconds);
                    
                    // Always remove the expected time regardless
                    _expectedReleaseTimes.Remove(button);
                    
                    // If this release happened within the synthetic window, it's synthetic
                    if (timeDiff < SYNTHETIC_WINDOW_MS)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public void SetupDefault(string bindingId, string keyCode)
        {
            bindings[bindingId] = keyCode;
            isHolding[bindingId] = false;
            OnBindingSet?.Invoke(bindingId, keyCode);
            EnsureHookEvents();
        }
        public void SetupMakcuEvents()
        {
            if (MakcuMain.MakcuInstance != null && MakcuMain.MakcuInstance.IsInitializedAndConnected)
            {
                MakcuMain.MakcuInstance.ButtonStateChanged -= MakcuMouseButtonStateChanged;
                MakcuMain.MakcuInstance.ButtonStateChanged += MakcuMouseButtonStateChanged;
                if (_mEvents != null)
                {
                    _mEvents.MouseDown -= GlobalHookMouseDown!;
                    _mEvents.MouseUp -= GlobalHookMouseUp!;
                }
            }
        }

        private void MakcuMouseButtonStateChanged(MakcuMouseButton button, bool isPressed)
        {
            string makcuButtonCodeStr = button.ToString();

            if (settingBindingId != null && isPressed)
            {

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    bindings[settingBindingId] = makcuButtonCodeStr;
                    isHolding[settingBindingId] = false;
                    OnBindingSet?.Invoke(settingBindingId, makcuButtonCodeStr);
                    settingBindingId = null;
                });
            }
            else
            {
                foreach (var bindingEntry in bindings)
                {
                    if (bindingEntry.Value == makcuButtonCodeStr)
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            isHolding[bindingEntry.Key] = isPressed;
                            if (isPressed)
                                OnBindingPressed?.Invoke(bindingEntry.Key);
                            else
                                OnBindingReleased?.Invoke(bindingEntry.Key);
                        });

                    }
                }
            }
        }

        public void StartListeningForBinding(string bindingId)
        {
            settingBindingId = bindingId;
            EnsureHookEvents();
        }
        public void RestoreMouseEvents()
        {
            if (_mEvents != null)
            {
                _mEvents.MouseDown -= GlobalHookMouseDown!;
                _mEvents.MouseUp -= GlobalHookMouseUp!;
                _mEvents.MouseDown += GlobalHookMouseDown!;
                _mEvents.MouseUp += GlobalHookMouseUp!;
            }
        }

        private void EnsureHookEvents()
        {
            if (_mEvents == null)
            {
                _mEvents = Hook.GlobalEvents();
                _mEvents.KeyDown += GlobalHookKeyDown!;
                _mEvents.MouseDown += GlobalHookMouseDown!;
                _mEvents.KeyUp += GlobalHookKeyUp!;
                _mEvents.MouseUp += GlobalHookMouseUp!;
            }
            SetupMakcuEvents();
        }

        private void GlobalHookMouseDown(object sender, MouseEventArgs e)
        {
            if (settingBindingId != null)
            {
                bindings[settingBindingId] = e.Button.ToString();
                OnBindingSet?.Invoke(settingBindingId, e.Button.ToString());
                settingBindingId = null;
            }
            else
            {
                foreach (var binding in bindings)
                {
                    if (binding.Value == e.Button.ToString())
                    {
                        isHolding[binding.Key] = true;
                        OnBindingPressed?.Invoke(binding.Key);
                    }
                }
            }
        }

        private void GlobalHookMouseUp(object sender, MouseEventArgs e)
        {
            // Check if this is a synthetic release
            if (IsSyntheticRelease(e.Button))
            {
                // It's a simulated release from rapid fire - ignore it
                return;
            }

            // This is a physical release - process it normally
            foreach (var binding in bindings)
            {
                if (binding.Value == e.Button.ToString())
                {
                    isHolding[binding.Key] = false;
                    OnBindingReleased?.Invoke(binding.Key);
                }
            }
        }
        private void GlobalHookKeyDown(object sender, KeyEventArgs e)
        {
            if (settingBindingId != null)
            {
                bindings[settingBindingId] = e.KeyCode.ToString();
                OnBindingSet?.Invoke(settingBindingId, e.KeyCode.ToString());
                settingBindingId = null;
            }
            else
            {
                foreach (var binding in bindings)
                {
                    if (binding.Value == e.KeyCode.ToString())
                    {
                        isHolding[binding.Key] = true;
                        OnBindingPressed?.Invoke(binding.Key);
                    }
                }
            }
        }

        private void GlobalHookKeyUp(object sender, KeyEventArgs e)
        {
            foreach (var binding in bindings)
            {
                if (binding.Value == e.KeyCode.ToString())
                {
                    isHolding[binding.Key] = false;
                    OnBindingReleased?.Invoke(binding.Key);
                }
            }
        }

        public void StopListening()
        {
            if (_mEvents != null)
            {
                _mEvents.KeyDown -= GlobalHookKeyDown!;
                _mEvents.MouseDown -= GlobalHookMouseDown!;
                _mEvents.KeyUp -= GlobalHookKeyUp!;
                _mEvents.MouseUp -= GlobalHookMouseUp!;
                _mEvents.Dispose();
                _mEvents = null;
                if (MakcuMain.MakcuInstance != null)
                {
                    try
                    {
                        MakcuMain.MakcuInstance.ButtonStateChanged -= MakcuMouseButtonStateChanged;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"DEBUG: Error MakcuMouse: {ex.Message}");
                    }
                }
            }
        }
    }
}