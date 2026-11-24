using System.Diagnostics;
using System.Text.Json;

namespace WireLink.App;

internal sealed class StartupTrace
{
    private static readonly Stopwatch clock = new();
    private readonly Dictionary<string, double> stages = [];
    public static void Start() => clock.Restart();
    public void Mark(string name) => stages.TryAdd(name, Math.Round(clock.Elapsed.TotalMilliseconds, 1));

    public void Save(string root, string presentation)
    {
        var report = new { startedAt = DateTimeOffset.Now, processId = Environment.ProcessId, presentation, stages = new Dictionary<string, double>(stages) };
        _ = Task.Run(() =>
        {
            try
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WireLink");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "startup-last.json"),
                    JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        });
    }
}
