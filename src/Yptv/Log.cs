using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Yptv;

// Rolling log in <data dir>\logs: app.log plus app.1.log and app.2.log, 1 MB each. Every line goes through Redact,
// so no URL (stream, playlist or panel address) and no absolute path ever reaches the disk or the clipboard.
static partial class Log
{
    const int MaxBytes = 1_000_000, Files = 3, KeepLines = 100;

    public static readonly string Dir = Path.Combine(AppPaths.Dir, "logs");
    static readonly string Main = Path.Combine(Dir, "app.log");
    static readonly object Gate = new();
    static readonly Queue<string> Recent = new();
    static StreamWriter? _writer;

    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9+.\-]*://[^\s""'<>]+")]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"(?:[A-Za-z]:\\|\\\\)[^\s""'<>|?*]+")]
    private static partial Regex PathPattern();

    public static string Redact(string text) => PathPattern().Replace(UrlPattern().Replace(text, "<url>"), "<path>");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);

    // Type and redacted message only; stack traces name source files, not secrets, but keep the first frames.
    public static void Exception(string where, Exception e) =>
        Write("ERROR", $"{where}: {e.GetType().Name}: {e.Message}{Environment.NewLine}{FirstFrames(e)}");

    static string FirstFrames(Exception e) =>
        string.Join(Environment.NewLine, (e.StackTrace ?? "").Split('\n').Take(6).Select(l => l.TrimEnd()));

    static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level,-5} {Redact(message)}";
        lock (Gate)
        {
            Recent.Enqueue(line);
            while (Recent.Count > KeepLines) Recent.Dequeue();
            try
            {
                if (_writer == null)
                {
                    Directory.CreateDirectory(Dir);
                    _writer = new StreamWriter(new FileStream(Main, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete),
                        new UTF8Encoding(false)) { AutoFlush = true };
                }
                _writer.WriteLine(line);
                if (_writer.BaseStream.Length >= MaxBytes) Rotate();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _writer = null; // logging must never take the app down
            }
        }
    }

    // app.log -> app.1.log -> app.2.log, the oldest is dropped.
    static void Rotate()
    {
        _writer?.Dispose();
        _writer = null;
        for (int i = Files - 1; i >= 1; i--)
        {
            var from = i == 1 ? Main : Path.Combine(Dir, $"app.{i - 1}.log");
            if (File.Exists(from)) File.Move(from, Path.Combine(Dir, $"app.{i}.log"), overwrite: true);
        }
    }

    public static string LastLines()
    {
        lock (Gate) return string.Join(Environment.NewLine, Recent);
    }
}
