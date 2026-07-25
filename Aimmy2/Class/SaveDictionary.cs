using Newtonsoft.Json;
using Other;
using System.IO;
using MessageBox = System.Windows.MessageBox;

namespace Class
{
    internal class SaveDictionary
    {
        // Ensure all required directories exist at startup
        public static void EnsureDirectoriesExist()
        {
            var requiredDirectories = new[]
            {
                "bin",
                "bin\\configs",
                "bin\\labels",
                "bin\\models",
                "bin\\anti_recoil_configs"
            };

            foreach (var dir in requiredDirectories)
            {
                if (!Directory.Exists(dir))
                {
                    try
                    {
                        Directory.CreateDirectory(dir);
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, $"Failed to create directory {dir}: {ex.Message}", true);
                    }
                }
            }
        }
        public static void WriteJSON(Dictionary<string, dynamic> dictionary, string path = "bin\\configs\\Default.cfg", string SuggestedModel = "", string ExtraStrings = "")
        {
            try
            {
                // Ensure the directory exists
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var SavedJSONSettings = new Dictionary<string, dynamic>(dictionary);
                if (!string.IsNullOrEmpty(SuggestedModel) && SavedJSONSettings.ContainsKey("Suggested Model"))
                {
                    SavedJSONSettings["Suggested Model"] = SuggestedModel + ".onnx" + ExtraStrings;
                }

                string json = JsonConvert.SerializeObject(SavedJSONSettings, Formatting.Indented);

                // Write to a temporary file and swap it in, a crash while writing can never
                // leave a half written config behind that way
                string tempPath = path + ".tmp";
                File.WriteAllText(tempPath, json);

                if (File.Exists(path))
                {
                    File.Replace(tempPath, path, null);
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            catch (Exception ex)
            {
                // Only show error if it's not a directory creation issue
                MessageBox.Show($"Error writing JSON, please note:\n{ex}");
            }
        }

        public static void LoadJSON(Dictionary<string, dynamic> dictionary, string path = "bin\\configs\\Default.cfg", bool strict = true)
        {
            try
            {
                // Ensure the directory exists before checking for the file
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (!File.Exists(path))
                {
                    WriteJSON(dictionary, path);
                    return;
                }

                var configuration = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(File.ReadAllText(path));
                if (configuration == null) return;

                foreach (var (key, value) in configuration)
                {
                    if (dictionary.ContainsKey(key))
                    {
                        dictionary[key] = value;
                    }
                    else if (!strict)
                    {
                        dictionary.Add(key, value);
                    }
                }
            }
            catch (Exception ex)
            {
                // Never overwrite the config with defaults, move the unreadable file aside so
                // it can still be recovered by hand and leave the loaded settings alone
                string message;

                try
                {
                    if (File.Exists(path))
                    {
                        string backupPath = GetCorruptBackupPath(path);
                        File.Move(path, backupPath);
                        message = $"Could not read \"{Path.GetFileName(path)}\", it was backed up as \"{Path.GetFileName(backupPath)}\" and your current settings were kept.\n{ex.Message}";
                    }
                    else
                    {
                        message = $"Error loading \"{Path.GetFileName(path)}\":\n{ex.Message}";
                    }
                }
                catch (Exception backupEx)
                {
                    message = $"Could not read \"{Path.GetFileName(path)}\" and backing it up failed as well:\n{backupEx.Message}";
                }

                try
                {
                    LogManager.Log(LogManager.LogLevel.Error, message, true, 8000);
                }
                catch
                {
                    // Configs are loaded before the UI exists, the notification layer may not be up yet
                }
            }
        }

        // Never overwrite an existing backup, pick the next free "<name>.corrupt-<n>.bak"
        private static string GetCorruptBackupPath(string path)
        {
            string directory = Path.GetDirectoryName(path) ?? string.Empty;
            string fileName = Path.GetFileName(path);

            int backupIndex = 1;
            string backupPath;

            do
            {
                backupPath = Path.Combine(directory, $"{fileName}.corrupt-{backupIndex}.bak");
                backupIndex++;
            } while (File.Exists(backupPath));

            return backupPath;
        }
    }
}