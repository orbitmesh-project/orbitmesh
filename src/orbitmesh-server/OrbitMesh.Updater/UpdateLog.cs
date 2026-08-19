namespace OrbitMesh.Updater;

/// <summary>Logs to both stdout and a file next to the exe - by the time something goes wrong here, the
/// console that would normally show it (the Server's/Edge's own log output) is exactly what just
/// stopped, so a persistent file is the only place left to look.</summary>
internal sealed class UpdateLog
{
    private readonly string _path;

    public UpdateLog(string directory) => _path = Path.Combine(directory, "updater.log");

    public void Info(string message) => Write("INFO", message);

    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} [{level}] {message}";
        Console.WriteLine(line);
        File.AppendAllText(_path, line + Environment.NewLine);
    }
}
