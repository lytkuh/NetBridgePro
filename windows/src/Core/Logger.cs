using System.Collections.ObjectModel;
using System.Text;

using System.IO;

namespace NetBridge.Windows.Core;

/// <summary>
/// File + in-memory logger with a UI-facing ring buffer (2000 lines).
/// Also persists 7 days of daily files under ProgramData\NetBridge\logs.
/// </summary>
public static class Logger
{
    public class Entry
    {
        public DateTime Time { get; init; }
        public string Level { get; init; } = "I";
        public string Tag { get; init; } = "";
        public string Message { get; init; } = "";
        public string Render => $"{Time:MM-dd HH:mm:ss.fff} {Level}/{Tag}: {Message}";
    }

    private const int Capacity = 2000;
    private static readonly object Lock = new();
    private static readonly Queue<Entry> Ring = new(Capacity);

    public static event Action<Entry>? OnLogged;

    public static ObservableCollection<Entry> Lines
    {
        get
        {
            lock (Lock) return new ObservableCollection<Entry>(Ring);
        }
    }

    public static void Debug(string tag, string msg) => Log("D", tag, msg);
    public static void Info(string tag, string msg) => Log("I", tag, msg);
    public static void Warn(string tag, string msg) => Log("W", tag, msg);
    public static void Error(string tag, string msg) => Log("E", tag, msg);

    private static void Log(string level, string tag, string msg)
    {
        var e = new Entry { Time = DateTime.Now, Level = level, Tag = tag, Message = msg };
        lock (Lock)
        {
            if (Ring.Count >= Capacity) Ring.Dequeue();
            Ring.Enqueue(e);
        }
        try { OnLogged?.Invoke(e); } catch { }
        WriteFile(e);
    }

    private static void WriteFile(Entry e)
    {
        try
        {
            Directory.CreateDirectory(Constants.LogDir);
            var path = Path.Combine(Constants.LogDir, $"app-{DateTime.Now:yyyyMMdd}.log");
            File.AppendAllText(path, e.Render + Environment.NewLine, Encoding.UTF8);
            CleanupOldLogs();
        }
        catch { /* logging must never crash the app */ }
    }

    private static DateTime _lastCleanup = DateTime.MinValue;
    private static void CleanupOldLogs()
    {
        if ((DateTime.Now - _lastCleanup).TotalHours < 12) return;
        _lastCleanup = DateTime.Now;
        try
        {
            foreach (var f in Directory.GetFiles(Constants.LogDir, "app-*.log"))
            {
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-7))
                    File.Delete(f);
            }
        }
        catch { }
    }

    public static List<string> Recent(int limit = 400)
    {
        lock (Lock) return Ring.TakeLast(limit).Select(x => x.Render).ToList();
    }
}
