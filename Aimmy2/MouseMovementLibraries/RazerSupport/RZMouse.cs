using Microsoft.Win32;
using Other;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using Visuality;

namespace MouseMovementLibraries.RazerSupport
{
    internal class RZMouse
    {
        #region Razer Variables

        private const string rzctlpath = "rzctl.dll";
        private const string rzctlDownloadUrl_Debug = "https://github.com/MarsQQ/rzctl/releases/download/1.0.0/rzctl.dll";
        private const string rzctlDownloadUrl_Release = "https://github.com/camilia2o7/rzctl/releases/download/Release/rzctl.dll";

        // The native bool is one byte, the default marshalling would read four.
        [DllImport(rzctlpath, CallingConvention = CallingConvention.Cdecl, EntryPoint = "init")]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool init_native();

        [DllImport(rzctlpath, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mouse_move")]
        private static extern void mouse_move_native(int x, int y, bool starting_point);

        [DllImport(rzctlpath, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mouse_click")]
        private static extern void mouse_click_native(int up_down);

        private static readonly List<string> Razer_HID = [];

        private static bool vcRedistPromptRejected = false;

        // rzctl.dll is downloaded at runtime, so it can be missing or incompatible. Movement calls come
        // straight from the AI loop, so a failed load has to degrade to a no-op with a single
        // notification instead of throwing a DllNotFoundException every frame.
        private static bool rzctlUnavailable = false;

        private static bool rzctlFailureNotified = false;

        #endregion

        #region Guarded rzctl calls

        public static bool init()
        {
            bool initialized = init_native();
            if (initialized) rzctlUnavailable = false;
            return initialized;
        }

        public static void mouse_move(int x, int y, bool starting_point)
        {
            if (rzctlUnavailable) return;

            try
            {
                mouse_move_native(x, y, starting_point);
            }
            catch (Exception ex)
            {
                HandleRzctlFailure(ex);
            }
        }

        public static void mouse_click(int up_down)
        {
            if (rzctlUnavailable) return;

            try
            {
                mouse_click_native(up_down);
            }
            catch (Exception ex)
            {
                HandleRzctlFailure(ex);
            }
        }

        private static void HandleRzctlFailure(Exception ex)
        {
            rzctlUnavailable = true;

            if (rzctlFailureNotified) return;
            rzctlFailureNotified = true;

            try
            {
                LogManager.Log(LogManager.LogLevel.Error, $"rzctl.dll could not be used ({ex.GetType().Name}), Razer mouse movement is disabled. Please try a different Mouse Movement Method.", true);
            }
            catch
            {
                // Notifying is best effort, it must never throw back into the AI loop.
            }
        }

        #endregion
        public static async Task<bool> Load()
        {
            if (!await EnsureRazerSynapseInstalled())
                return false;

            if (!File.Exists(rzctlpath))
            {
                await DownloadAppropriateRzctl();
                return false;
            }

            if (!DetectRazerDevices())
            {
                new NoticeBar("No Razer device detected. This method is unusable.", 5000).Show();
                return false;
            }
            try
            {
                return init();
            }
            catch (BadImageFormatException)
            {
                new NoticeBar("rzctl.dll is incompatible. Attempting release version...", 4000).Show();
                await DownloadRzctl(rzctlDownloadUrl_Release);
                return false;
            }
            // Hopefully this method will solve the issue for the users who have/don't have the drivers
            // If the user gets this error, Failed to initialize Razer mode, then it will attempt to download the Release version.
            // And if that still doesn't work, then it will error again, which in this case would mean they don't have the driver for vs &&|| vc 2015–2022
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize Razer mode.\n{ex.Message}\n\n" +
                                "Attempting to replace rzctl.dll with the release version...",
                                "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
                try
                {
                    if (File.Exists(rzctlpath))
                        File.Delete(rzctlpath);

                    await DownloadRzctl(rzctlDownloadUrl_Release);

                    new NoticeBar("rzctl.dll replaced with release version. Please retry loading.", 5000).Show();
                }
                catch (Exception innerEx)
                {
                    MessageBox.Show($"Failed to recover rzctl.dll.\n{innerEx.Message}",
                            "Recovery Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return false;
            }
            /* Commenting this method out to replace with a more enhanced version
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize Razer mode.\n{ex.Message}",
                        "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            */
        }
        #region Razer Synapse & Device Checks

        private static bool DetectRazerDevices()
        {
            Razer_HID.Clear();
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Manufacturer LIKE 'Razer%'");
            var devices = searcher.Get().Cast<ManagementBaseObject>();

            Razer_HID.AddRange(devices.Select(d => d["DeviceID"]?.ToString() ?? string.Empty));
            return Razer_HID.Count > 0;
        }

        private static async Task<bool> EnsureRazerSynapseInstalled()
        {
            if (Process.GetProcessesByName("RazerAppEngine").Any())
                return true;

            var response = MessageBox.Show("Razer Synapse is not running. Do you have it installed?",
                                           "Aimmy - Razer Synapse", MessageBoxButton.YesNo);
            if (response == MessageBoxResult.No)
            {
                await DownloadAndInstallRazerSynapse();
                return false;
            }

            if (!IsRazerSynapseInstalled())
            {
                var install = MessageBox.Show("Razer Synapse is not installed. Would you like to install it?",
                                              "Aimmy - Razer Synapse", MessageBoxButton.YesNo);
                if (install == MessageBoxResult.Yes)
                {
                    await DownloadAndInstallRazerSynapse();
                    return false;
                }
                return false;
            }

            return true;
        }

        private static bool IsRazerSynapseInstalled()
        {
            return Directory.Exists(@"C:\Program Files\Razer") ||
                   Directory.Exists(@"C:\Program Files (x86)\Razer") ||
                   Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Razer") != null;
        }

        private static async Task DownloadAndInstallRazerSynapse()
        {
            try
            {
                using HttpClient client = new();
                var response = await client.GetAsync("https://rzr.to/synapse-new-pc-download-beta");
                if (!response.IsSuccessStatusCode)
                {
                    new NoticeBar("Failed to download Razer Synapse installer.", 4000).Show();
                    return;
                }

                string path = Path.Combine(Path.GetTempPath(), "rz.exe");
                var content = await response.Content.ReadAsByteArrayAsync();
                await File.WriteAllBytesAsync(path, content);

                Process.Start(new ProcessStartInfo
                {
                    WindowStyle = ProcessWindowStyle.Hidden,
                    FileName = "cmd.exe",
                    Arguments = "/C start rz.exe",
                    WorkingDirectory = Path.GetTempPath()
                });

                new NoticeBar("Razer Synapse downloaded. Please confirm the UAC prompt to install.", 4000).Show();
            }
            catch
            {
                new NoticeBar("Error occurred while downloading Synapse.", 4000).Show();
            }
        }

        #endregion

        #region Visual Studio & Redist Checks

        private static bool IsVcRedistInstalled()
        {
            string[] keys =
            {
                @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64",
                @"SOFTWARE\Microsoft\VisualStudio\17.0\VC\Runtimes\x64"
            };

            foreach (string path in keys)
            {
                using var key = Registry.LocalMachine.OpenSubKey(path);
                if (key != null && Convert.ToInt32(key.GetValue("Installed", 0)) == 1)
                    return true;
            }

            return false;
        }

        private static bool IsVisualStudioInstalled()
        {
            string[] uninstallRoots =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (string root in uninstallRoots)
            {
                using var key = Registry.LocalMachine.OpenSubKey(root);
                if (key == null) continue;

                foreach (string subKey in key.GetSubKeyNames())
                {
                    using var sub = key.OpenSubKey(subKey);
                    string name = sub?.GetValue("DisplayName") as string ?? "";

                    if (name.Contains("Visual Studio", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        #endregion

        #region rzctl.dll Download Logic

        private static async Task DownloadAppropriateRzctl()
        {
            if (IsVisualStudioInstalled())
            {
                if (!await DownloadRzctl(rzctlDownloadUrl_Debug))
                {
                    await DownloadRzctl(rzctlDownloadUrl_Release);
                }
            }
            else
            {
                if (!IsVcRedistInstalled())
                {
                    if (!vcRedistPromptRejected)
                    {
                        var prompt = MessageBox.Show("VC++ 2015–2022 Redistributable (x64) is missing. Install now?",
                                                     "Missing Dependency", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        if (prompt == MessageBoxResult.Yes)
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "https://aka.ms/vs/17/release/vc_redist.x64.exe",
                                UseShellExecute = true
                            });
                        }
                        else
                        {
                            vcRedistPromptRejected = true;
                            return;
                        }
                    }
                    else
                    {
                        return;
                    }
                }

                await DownloadRzctl(rzctlDownloadUrl_Release);

            }
        }


        private static async Task<bool> DownloadRzctl(string url)
        {
            try
            {
                new NoticeBar("rzctl.dll is missing, attempting to download rzctl.dll.", 4000).Show();

                using HttpClient client = new();
                var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode)
                {
                    new NoticeBar("Failed to download rzctl.dll from the given URL.", 4000).Show();
                    return false;
                }

                using var stream = await response.Content.ReadAsStreamAsync();
                using var file = new FileStream(rzctlpath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
                await stream.CopyToAsync(file);

                new NoticeBar("rzctl.dll has downloaded successfully, please re-select Razer Synapse to load the DLL.", 5000).Show();
                return true;
            }
            catch
            {
                new NoticeBar("Error downloading rzctl.dll.", 4000).Show();
                return false;
            }
        }
        #endregion
    }
}
