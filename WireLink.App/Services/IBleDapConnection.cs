namespace WireLink.App.Services;

public interface IBleDapConnection
{
    Task SubscribeAsync(string id, Action<byte[]> receive, CancellationToken ct);
    Task WriteChunksAsync(string id, byte[] data, CancellationToken ct, bool response = false);
}
