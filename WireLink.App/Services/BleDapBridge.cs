using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace WireLink.App.Services;

public sealed class BleDapBridge : IAsyncDisposable
{
    private readonly IBleDapConnection connection;
    private readonly Action<string>? report;
    private Exception? failure;
    public Exception? Failure => Volatile.Read(ref failure);
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly Task loop;
    private readonly Channel<byte[]> notifications = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(512)
        { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    private BleDapBridge(IBleDapConnection connection, Action<string>? report)
    {
        this.connection = connection;
        this.report = report;
        listener.Start(1);
        loop = RunAsync();
    }

    public static async Task<BleDapBridge> StartAsync(IBleDapConnection connection, CancellationToken ct, Action<string>? report = null)
    {
        var bridge = new BleDapBridge(connection, report);
        try
        {
            await connection.SubscribeAsync("12", data =>
            {
                if (!bridge.notifications.Writer.TryWrite(data)) bridge.Fail(new IOException("BLE DAP 通知缓冲区溢出，连接已关闭以避免截断数据，请重新连接。"));
            }, ct);
            return bridge;
        }
        catch { await bridge.DisposeAsync(); throw; }
    }

    private void Fail(Exception error)
    {
        if (Interlocked.CompareExchange(ref failure, error, null) != null) return;
        report?.Invoke(error.Message);
        stop.Cancel();
    }

    private async Task RunAsync()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(stop.Token);
                client.NoDelay = true;
                using var stream = client.GetStream();
                using var session = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                while (notifications.Reader.TryRead(out _)) { }
                async Task ForwardNotifications()
                {
                    await foreach (var packet in notifications.Reader.ReadAllAsync(session.Token))
                        await stream.WriteAsync(packet, session.Token);
                }
                var receive = ForwardNotifications();
                try
                {
                    var buffer = new byte[4096];
                    while (true)
                    {
                        var count = await stream.ReadAsync(buffer, session.Token);
                        if (count == 0) break;
                        await connection.WriteChunksAsync("11", buffer.AsSpan(0, count).ToArray(), session.Token, response: true);
                    }
                }
                finally
                {
                    session.Cancel();
                    try { await receive; } catch (Exception ex) when (ex is OperationCanceledException or IOException) { }
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        { if (!stop.IsCancellationRequested) Fail(new IOException("BLE DAP 桥连接失败：" + ex.Message, ex)); }
    }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        listener.Stop();
        try { await loop; } catch { }
        stop.Dispose();
    }
}
