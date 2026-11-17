using System.Text.Json.Nodes;
using WireLink.App.Services;

internal static class ReviewTests
{
    public static void Register(List<(string Name, Func<Task> Run)> tests)
    {
        tests.Add(("目录合并按 ID 去重，外部包覆盖同名条目", () =>
        {
            var merged = PyOcdService.MergeTargets(JsonNode.Parse("[{\"name\":\"part\",\"vendor\":\"builtin\"}]")!.AsArray(),
                JsonNode.Parse("[{\"name\":\"PART\",\"vendor\":\"custom\"},{\"name\":\"other\"},{}]")!.AsArray());
            Check(merged.Count == 2 && merged[0]!["vendor"]!.GetValue<string>() == "custom");
            return Task.CompletedTask;
        }));
        tests.Add(("pyOCD 子进程取消和快速退出不导致取消线程异常", async () =>
        {
            var service = new PyOcdService(Environment.CurrentDirectory, _ => { });
            for (var i = 0; i < 3; i++)
            {
                using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                try { await service.RunAsync(["-c", "import time; time.sleep(2)"], cancellation.Token); throw new Exception("Cancellation was ignored"); }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            }
            Check((await service.RunAsync(["-c", "print('done')"], CancellationToken.None, true)).Trim() == "done");
        }));
    }
    private static void Check(bool condition) { if (!condition) throw new Exception("Review regression assertion failed"); }
}
