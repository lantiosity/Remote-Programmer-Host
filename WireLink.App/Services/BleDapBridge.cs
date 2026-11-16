using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace WireLink.App.Services;

public sealed class BleDapBridge : IAsyncDisposable
{
    private readonly BleConnection connection;
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly Task loop;
    private readonly Channel<byte[]> notifications = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(512)
        { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    private BleDapBridge(BleConnection connection)
    {
        this.connection = connection;
        listener.Start(1);
        loop = RunAsync();
    }

    public static async Task<BleDapBridge> StartAsync(BleConnection connection, CancellationToken ct)
    {
        var bridge = new BleDapBridge(connection);
        try
        {
            await connection.SubscribeAsync("12", data =>
            {
                if (!bridge.notifications.Writer.TryWrite(data)) bridge.stop.Cancel();
            }, ct);
            return bridge;
        }
        catch { await bridge.DisposeAsync(); throw; }
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
                        await connection.WriteChunksAsync("11", buffer.AsSpan(0, count).ToArray(), session.Token);
                    }
                }
                finally
                {
                    session.Cancel();
                    try { await receive; } catch (Exception ex) when (ex is OperationCanceledException or IOException) { }
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        listener.Stop();
        try { await loop; } catch { }
        stop.Dispose();
    }
}
