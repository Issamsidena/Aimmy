using System.IO;

namespace Aimmy2.AILogic
{
    internal static class ModelClassLabels
    {
        private static readonly object LabelLock = new();
        private static List<string> _labels = new() { "Enemy" };

        public static IReadOnlyList<string> Labels
        {
            get { lock (LabelLock) { return _labels; } }
        }

        public static void LoadFromDefaultPath()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "labels", "labels.txt");
            LoadFromFile(path);
        }

        public static void LoadFromFile(string path)
        {
            lock (LabelLock)
            {
                if (!File.Exists(path))
                {
                    _labels = new List<string> { "Enemy" };
                    return;
                }

                var lines = File.ReadAllLines(path)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToList();

                _labels = lines.Count > 0 ? lines : new List<string> { "Enemy" };
            }
        }

        public static string GetLabel(int classIndex)
        {
            lock (LabelLock)
            {
                if (classIndex >= 0 && classIndex < _labels.Count)
                    return _labels[classIndex];
                return $"class_{classIndex}";
            }
        }

        public static int? GetIndexByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            lock (LabelLock)
            {
                for (int i = 0; i < _labels.Count; i++)
                {
                    if (string.Equals(_labels[i], name, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }

            return null;
        }
    }
}
