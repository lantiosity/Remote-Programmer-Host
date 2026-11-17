using System.Diagnostics;
using System.ComponentModel;
using System.Text;
using System.Text.Json.Nodes;
using WireLink.Core;

namespace WireLink.App.Services;

public sealed class PyOcdService(string root, Action<string> log)
{
    internal static void TryTerminate(Process process)
    {
        try { if (!process.HasExited) process.Kill(true); }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException) { }
    }
    public string Python => File.Exists(Path.Combine(AppContext.BaseDirectory, "engine", "python.exe"))
        ? Path.Combine(AppContext.BaseDirectory, "engine", "python.exe") : Environment.GetEnvironmentVariable("WIRELINK_PYTHON") ?? Path.Combine(root, ".venv-ble", "Scripts", "python.exe");

    public async Task<string> RunAsync(IEnumerable<string> args, CancellationToken ct, bool capture = false, int timeout = 1800)
    {
        if (!File.Exists(Python)) throw new FileNotFoundException("缺少 pyOCD 引擎：请运行项目中的安装上位机依赖.cmd");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(Python)
            {
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            }
        };
        var arguments = args.ToList();
        var targetIndex = arguments.IndexOf("-t");
        var mapFile = Path.Combine(AppContext.BaseDirectory, "devices", "target-packs.json");
        if (targetIndex >= 0 && File.Exists(mapFile))
        {
            var mapping = JsonNode.Parse(await File.ReadAllTextAsync(mapFile, ct));
            var pack = mapping?[arguments[targetIndex + 1].ToLowerInvariant()]?.GetValue<string>();
            if (pack != null) {
                var index = arguments.IndexOf("--pack");
                arguments.InsertRange(index < 0 ? arguments.Count : index,
                    ["--pack", Path.Combine(AppContext.BaseDirectory, "devices", "packs", Path.GetFileName(pack))]);
            }
        }
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.StartInfo.Environment["PYTHONUTF8"] = "1";
        process.StartInfo.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        log("pyOCD · " + string.Join(" ", process.StartInfo.ArgumentList.Select(s => s.Contains(' ') ? $"\"{s}\"" : s)));
        process.Start();
        // Engine stdin is separate from the WebView message channel and immediately closed.
        process.StandardInput.Close();
        using var timer = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timer.Token);
        using var cancellation = linked.Token.Register(() =>
        {
            TryTerminate(process);
        });
        var output = new StringBuilder();
        async Task ReadAsync(StreamReader reader)
        {
            var buffer = new char[1024];
            var pending = new StringBuilder();
            void Line()
            {
                var line = pending.ToString(); pending.Clear();
                if (capture) { lock (output) output.AppendLine(line); }
                else if (!string.IsNullOrWhiteSpace(line)) log(line);
            }
            while (true)
            {
                var count = await reader.ReadAsync(buffer, linked.Token);
                if (count == 0) break;
                for (var i = 0; i < count; i++)
                    if (buffer[i] is '\r' or '\n') Line(); else pending.Append(buffer[i]);
            }
            if (pending.Length != 0) Line();
        }
        try
        {
            await Task.WhenAll(ReadAsync(process.StandardOutput), ReadAsync(process.StandardError), process.WaitForExitAsync(linked.Token));
            ct.ThrowIfCancellationRequested();
            if (timer.IsCancellationRequested) throw new TimeoutException("pyOCD 操作超时，请检查设备连接");
            if (process.ExitCode != 0) throw new IOException($"pyOCD 退出码 {process.ExitCode}" + (capture ? "\n" + output : "，详情见运行日志"));
            return output.ToString();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new TimeoutException("pyOCD 操作超时，请检查设备连接"); }
        finally
        {
            TryTerminate(process);
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or Win32Exception) { }
        }
    }

    public async Task<JsonNode> CatalogAsync(string kind, Settings settings, CancellationToken ct)
    {
        var bundledFile = Path.Combine(AppContext.BaseDirectory, "devices", "targets.json");
        JsonArray? bundled = kind == "targets" && File.Exists(bundledFile)
            ? JsonNode.Parse(await File.ReadAllTextAsync(bundledFile, ct))!.AsArray() : null;
        if (bundled != null && string.IsNullOrWhiteSpace(settings.Pack)) return bundled;
        List<string> args = ["-u", "-m", "pyocd", "json", "--" + kind, "--no-config"];
        if (kind == "targets" && !string.IsNullOrWhiteSpace(settings.Pack))
            args.AddRange(["--pack", Validation.FilePath(settings.Pack, ".pack")]);
        var text = await RunAsync(args, ct, true, 45);
        var start = text.IndexOf('{');
        if (start < 0) throw new InvalidDataException("pyOCD 未返回有效设备信息：" + text);
        var json = JsonNode.Parse(text[start..])!;
        if (json["status"]?.GetValue<int>() is not (null or 0)) throw new IOException(text);
        var result = json[kind == "probes" ? "boards" : "targets"]?.DeepClone() ?? new JsonArray();
        return kind == "targets" ? MergeTargets(bundled, result.AsArray()) : result;
    }

    public static JsonArray MergeTargets(JsonArray? bundled, JsonArray current)
    {
        var entries = new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var list in new[] { bundled, current })
            if (list != null) foreach (var item in list)
                if (item?["name"] is JsonValue name && name.TryGetValue<string>(out var key) && !string.IsNullOrWhiteSpace(key))
                    entries[key] = item.DeepClone();
        return new JsonArray(entries.Values.ToArray());
    }
}
