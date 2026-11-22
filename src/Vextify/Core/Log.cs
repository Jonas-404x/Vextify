namespace Vextify.Core;

public static class Log
{
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Paths.VextifyData);
                var info = new FileInfo(Paths.LogFile);
                if (info.Exists && info.Length > 512 * 1024) info.Delete();
                File.AppendAllText(Paths.LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
            }
        }
        catch { /* logging must never crash the app */ }
    }
}
