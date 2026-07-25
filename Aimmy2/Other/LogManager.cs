using Aimmy2.Class;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Visuality;

namespace Other
{
    internal class LogManager
    {
        public enum LogLevel
        {
            Info,
            Warning,
            Error
        }

        // debug.txt is written from the AI loop and the UI thread at the same time; without this
        // lock the concurrent StreamWriters throw IOException ("file in use").
        private static readonly object _fileLock = new();

        // A notice creates a WPF Window. Per-frame code paths would otherwise create hundreds per
        // second, so the same text can only pop again after this cooldown.
        private static readonly object _noticeLock = new();
        private static readonly System.Collections.Generic.Dictionary<string, DateTime> _recentNotices = new();
        private static readonly TimeSpan _noticeCooldown = TimeSpan.FromSeconds(3);

        public static void Log(LogLevel lvl, string message, bool notifyUser = false, int waitingTime = 4000)
        {
            if (notifyUser)
            {
                try
                {
                    // Application.Current is null once the app is shutting down.
                    var app = Application.Current;
                    if (app != null && !app.Dispatcher.HasShutdownStarted && ShouldNotify(message))
                    {
                        // BeginInvoke, never Invoke: the caller (the AI loop) must not block on the
                        // UI thread while a Window is constructed.
                        app.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            new NoticeBar(message, waitingTime).Show();
                        }));
                    }
                }
                catch
                {
                    // A failed notification must never take the caller down with it.
                }
            }
#if DEBUG
            Debug.WriteLine(message);
#endif
            try
            {
                if (Dictionary.toggleState["Debug Mode"])
                {
                    lock (_fileLock)
                    {
                        string logFilepath = "debug.txt";
                        using StreamWriter w = new(logFilepath, true);
                        string lvlPrefix = lvl.ToString().ToUpper();
                        w.WriteLine($"[{DateTime.Now}] [{lvlPrefix}]: {message}");
                    }
                }
            }
            catch
            {
                // Logging must never throw into the caller.
            }
        }

        /// <summary>
        /// Rate-limits duplicate notices so the same message cannot spawn a notice window more than
        /// once every <see cref="_noticeCooldown"/>.
        /// </summary>
        private static bool ShouldNotify(string message)
        {
            lock (_noticeLock)
            {
                var now = DateTime.Now;

                if (_recentNotices.TryGetValue(message, out var last) && now - last < _noticeCooldown)
                {
                    return false;
                }

                _recentNotices[message] = now;

                // Keep the de-dupe table from growing without bound.
                if (_recentNotices.Count > 64)
                {
                    var stale = new System.Collections.Generic.List<string>();
                    foreach (var entry in _recentNotices)
                    {
                        if (now - entry.Value > _noticeCooldown)
                            stale.Add(entry.Key);
                    }

                    foreach (var key in stale)
                    {
                        _recentNotices.Remove(key);
                    }
                }

                return true;
            }
        }
    }
}
