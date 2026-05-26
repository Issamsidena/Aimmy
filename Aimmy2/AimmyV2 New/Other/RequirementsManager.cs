using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Visuality;

namespace Other
{
    internal class RequirementsManager
    {
        #region LGHub n' Stuff
        public static bool IsVCRedistInstalled()
        {
            // Visual C++ Redistributable for Visual Studio 2015, 2017, and 2019 check
            string regKeyPath = @"SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\x64";

            using (var key = Registry.LocalMachine.OpenSubKey(regKeyPath))
            {
                if (key != null && key.GetValue("Installed") != null)
                {
                    object? installedValue = key.GetValue("Installed");
                    return installedValue != null && (int)installedValue == 1;
                }
            }

            return false;
        }

        public static bool IsMemoryIntegrityEnabled() // false if enabled true if disabled, you want it disabled
        {
            //credits to Themida
            string keyPath = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforceCodeIntegrity";
            string valueName = "Enabled";
            object? value = Registry.GetValue(keyPath, valueName, null);
            if (value != null && Convert.ToInt32(value) == 1)
            {
                LogManager.Log(LogManager.LogLevel.Warning, "Memory Integrity is enabled, please disable it to use Logitech Driver.", true, 7000);
                return false;
            }
            else return true;
        }

        public static bool CheckForGhub()
        {
            try
            {
                Process? process = Process.GetProcessesByName("lghub").FirstOrDefault(); //gets the first process named "lghub"
                if (process == null)
                {
                    ShowLGHubNotRunningMessage();
                    return false;
                }

                string ghubfilepath = process.MainModule.FileName;
                if (ghubfilepath == null)
                {
                    LogManager.Log(LogManager.LogLevel.Error, "An error occurred. Run as admin and try again.", true);
                    return false;
                }

                FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(ghubfilepath);

                if (!versionInfo.ProductVersion.Contains("2021"))
                {
                    ShowLGHubImproperInstallMessage();
                    return false;
                }

                return true;
            }
            catch (AccessViolationException ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, $"An error occured: {ex.Message}\nRun as admin and try again.", true);
                return false;
            }
        }

        private static void ShowLGHubNotRunningMessage()
        {
            if (MessageBox.Show("LG HUB is not running, is it installed?", "Vector - LG HUB Mouse Movement", MessageBoxButton.YesNo, MessageBoxImage.Error) == MessageBoxResult.No)
            {
                if (MessageBox.Show("Would you like to install it?", "Vector - LG HUB Mouse Movement", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    new LGDownloader().Show();
                }
            }
        }

        private static void ShowLGHubImproperInstallMessage()
        {
            if (MessageBox.Show("LG HUB install is improper, would you like to install it?", "Vector - LG HUB Mouse Movement", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                new LGDownloader().Show();
            }
        }
        #endregion
        #region Cuda n' Friends
        public static bool IsCUDAInstalled()
        {
            try
            {
                string baseDirectory = @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA";
                string envCudaPath = Environment.GetEnvironmentVariable("CUDA_PATH") ?? "";

                if (Directory.Exists(baseDirectory))
                {
                    var cudaDirectories = Directory.GetDirectories(baseDirectory, "v12.*");

                    foreach (var directory in cudaDirectories)
                    {
                        if (Directory.Exists(Path.Combine(directory, "bin")))
                        {
                            LogManager.Log(LogManager.LogLevel.Info, $"CUDA 12.x found in directory: {directory}");
                            return true;
                        }
                    }
                }

                //maybe they installed it on a different harddrive, or what if they wanna be different and change their local drive from C to D
                if (!string.IsNullOrEmpty(envCudaPath) && envCudaPath.Contains("CUDA\\v12."))
                {
                    LogManager.Log(LogManager.LogLevel.Info, "CUDA 12.x found via CUDA_PATH environment variable.");
                    return true;
                }

                string registryBasePath = @"SOFTWARE\NVIDIA Corporation\GPU Computing Toolkit\CUDA";
                using var baseKey = Registry.LocalMachine.OpenSubKey(registryBasePath);

                if (baseKey != null)
                {
                    var versionKeys = baseKey.GetSubKeyNames()
                                             .Where(name => name.StartsWith("v12."));
                    foreach (var versionKey in versionKeys)
                    {
                        using var key = baseKey.OpenSubKey(versionKey);
                        object? installedValue = key?.GetValue("64BitInstalled");

                        if (installedValue != null && (int)installedValue == 1)
                        {
                            LogManager.Log(LogManager.LogLevel.Info, $"CUDA {versionKey} is installed as per registry.");
                            return true;
                        }
                    }
                }

                string[] dlls =
                    [
                    "cublasLt64_12.dll",
                    "cublas64_12.dll",
                    "cufft64_11.dll",
                    "cudart64_12.dll",
                    ];

                string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;

                bool dllExists = false;
                foreach (string dll in dlls)
                {
                    if (File.Exists(Path.Combine(exeDirectory, dll)))
                    {
                        LogManager.Log(LogManager.LogLevel.Info, $"Found CUDA DLLs {dll} in executable directory");
                        dllExists = true;
                    }
                }

                if (dllExists)
                {
                    LogManager.Log(LogManager.LogLevel.Info, "Found all CUDA DLLS in Vector Directory");
                    return true;
                }

                LogManager.Log(LogManager.LogLevel.Error, "CUDA 12.x is not installed");
                return false;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, $"Error while checking for CUDA 12.x: {ex}");
                return false;
            }
        }
        public static bool IsCUDNNInstalled()
        {
            try
            {
                string cudnnDirectoryBase = @"C:\Program Files\NVIDIA\CUDNN";
                string vectorDirectory = AppDomain.CurrentDomain.BaseDirectory;

                // Check if there's a folder starting with "v9." in the NVIDIA CUDNN directory
                if (Directory.Exists(cudnnDirectoryBase))
                {
                    var cudnnDirectories = Directory.GetDirectories(cudnnDirectoryBase, "v9.*");

                    foreach (var directory in cudnnDirectories)
                    {
                        if (Directory.Exists(Path.Combine(directory, "bin")))
                        {
                            LogManager.Log(LogManager.LogLevel.Info, $"cuDNN 9.x found in directory: {directory}");
                            return true;
                        }
                    }
                }

                if (File.Exists(Path.Combine(vectorDirectory, "cudnn64_9.dll")))
                {
                    LogManager.Log(LogManager.LogLevel.Info, "cuDNN 9.x was found in the Vector directory.");
                    return true;
                }

                // maybe they installed it on a different harddrive, or what if they wanna be different and change their local drive from C to D
                string path = Environment.GetEnvironmentVariable("PATH") ?? "";

                if (!string.IsNullOrEmpty(path) && path.Contains("CUDNN\\v9."))
                {
                    LogManager.Log(LogManager.LogLevel.Info, "cuDNN 9.x may be installed and referenced in PATH.");
                    return true;
                }

                LogManager.Log(LogManager.LogLevel.Error, "CUDNN 9.x is not installed");
                return false;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, $"Error while checking for CUDNN: {ex}");
                return false;
            }
        }
        public static bool IsTensorRTInstalled() // This should rarely ever return false, as aimmy-cuda is preinstalled with tensorrt.
        {
            //Installation varies, this may be wrong... Program will not exit based on findings.
            try
            {
                string[] dlls =
                    [
                    "nvinfer_10.dll",
                    "nvinfer_plugin_10.dll",
                    "nvonnxparser_10.dll",
                    ];
                string baseDirectory = @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA";
                string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;

                bool dllExists = false;

                foreach (string dll in dlls)
                {
                    // Search through CUDA directories for any version containing the DLLs
                    if (Directory.Exists(baseDirectory))
                    {
                        var cudaDirectories = Directory.GetDirectories(baseDirectory, "v*");
                        foreach (var directory in cudaDirectories)
                        {
                            if (File.Exists(Path.Combine(directory, "bin", dll)))
                            {
                                LogManager.Log(LogManager.LogLevel.Info, $"Found TensorRT DLL {dll} in {directory}\\bin");
                                dllExists = true;
                            }
                        }
                    }

                    // Check if the DLL is in the application's executable directory
                    if (File.Exists(Path.Combine(exeDirectory, dll)))
                    {
                        LogManager.Log(LogManager.LogLevel.Info, $"Found TensorRT DLL {dll} in executable directory");
                        dllExists = true;
                    }
                }

                if (dllExists)
                {
                    LogManager.Log(LogManager.LogLevel.Info, "Found all TensorRT DLLS in CUDA Path");
                    return true;
                }

                // maybe they installed it on a different harddrive, or what if they wanna be different and change their local drive from C to D

                string path = Environment.GetEnvironmentVariable("PATH") ?? "";

                if (!string.IsNullOrEmpty(path) && path.Contains("TensorRT"))
                {
                    LogManager.Log(LogManager.LogLevel.Info, "TensorRT may be installed based on PATH environment variable.");
                    return true;
                }


                LogManager.Log(LogManager.LogLevel.Error, "TensorRT not found.");
                return false;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, $"Error while checking for TensorRT: {ex}");
                return false;
            }
        }
        #endregion
    }
}