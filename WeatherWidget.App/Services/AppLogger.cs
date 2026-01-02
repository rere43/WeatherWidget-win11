using System.IO;

namespace WeatherWidget.App.Services;

public static class AppLogger
{
    private static readonly object Gate = new();
    private static string? _path;
    private const int MaxLines = 100;

    public static void Init(string appDataRoot)
    {
        try
        {
            _path = Path.Combine(appDataRoot, "app.log");
        }
        catch
        {
            _path = null;
        }
    }

    public static void Info(string message)
    {
        try
        {
            if (_path is null)
            {
                return;
            }

            var line = $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}";
            lock (Gate)
            {
                File.AppendAllText(_path, line);
                TrimToMaxLines_NoThrow(_path);
            }
        }
        catch
        {
            // ignore logging errors
        }
    }

    private static void TrimToMaxLines_NoThrow(string path)
    {
        try
        {
            if (MaxLines <= 0 || !File.Exists(path))
            {
                return;
            }

            var queue = new Queue<string>(MaxLines + 1);
            var trimmed = false;
            foreach (var line in File.ReadLines(path))
            {
                queue.Enqueue(line);
                if (queue.Count <= MaxLines)
                {
                    continue;
                }

                queue.Dequeue();
                trimmed = true;
            }

            if (!trimmed)
            {
                return;
            }

            File.WriteAllLines(path, queue);
        }
        catch
        {
            // ignore trimming errors
        }
    }
}
