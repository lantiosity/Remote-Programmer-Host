using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WireLink.Core;

public sealed record OfflinePreset(uint Address = OfflineLibrary.FlashBase, bool Reset = false);
public sealed record OfflineImage(string Name, long Size, uint Address, bool Reset, uint? EraseEnd);

public sealed class OfflineLibrary(string directory)
{
    public const uint FlashBase = 0x08000000;
    public const int FlashSize = 2 * 1024 * 1024;
    private const uint SectorSize = 128 * 1024;

    public IReadOnlyList<OfflineImage> List()
    {
        if (!Directory.Exists(directory)) return [];
        return new DirectoryInfo(directory).EnumerateFiles()
            .Where(file => IsImageName(file.Name) && (file.Attributes & FileAttributes.ReparsePoint) == 0 && file.Length is > 0 and <= FlashSize)
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Select(file => Describe(file, ReadPreset(file.Name))).ToArray();
    }

    public async Task<string> ImportAsync(string path, CancellationToken ct)
    {
        var source = new FileInfo(Validation.FilePath(path, ".bin"));
        var destination = new FileInfo(ImagePath(source.Name));
        CheckSize(source.Length);
        if (source.FullName.Equals(destination.FullName, StringComparison.OrdinalIgnoreCase)) return source.Name;
        Directory.CreateDirectory(directory);
        await CopyAsync(source.FullName, destination.FullName, ct);
        return source.Name;
    }

    public OfflineImage SetPreset(string name, string address, bool reset)
    {
        var source = GetImage(name);
        var value = (uint)Validation.Number(address, "起始地址", FlashBase, FlashBase + FlashSize - 1);
        CalculateEraseEnd(value, source.Length);
        var preset = new OfflinePreset(value, reset);
        var path = PresetPath(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(preset));
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
        return Describe(source, preset);
    }

    public void Remove(string name)
    {
        var file = GetImage(name);
        File.Delete(file.FullName);
        var preset = PresetPath(name);
        if (File.Exists(preset)) File.Delete(preset);
    }

    public async Task ExportAsync(string name, string selectedPath, CancellationToken ct)
    {
        var source = GetImage(name);
        var destination = Path.GetFullPath(selectedPath);
        if (!destination.Equals(Path.GetPathRoot(destination), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("请选择 microSD 的盘符根目录。");
        var drive = new DriveInfo(destination);
        if (!drive.IsReady || !drive.DriveFormat.Equals("FAT32", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("请选择已挂载的 FAT32 microSD。");
        if (Directory.EnumerateFiles(destination).Count(path => IsImageName(Path.GetFileName(path))) >= 48)
            throw new IOException("卡根目录已有至少 48 个 .bin，设备面板最多显示 48 个。请先整理卡上的镜像。");
        await CopyAsync(source.FullName, Path.Combine(destination, name), ct);
    }

    public static uint CalculateEraseEnd(uint address, long size)
    {
        CheckSize(size);
        if (address < FlashBase || address >= FlashBase + FlashSize || (address - FlashBase) % SectorSize != 0)
            throw new ArgumentException("起始地址必须在 STM32H743VI Flash 内并按 128 KiB 扇区对齐。");
        var end = address + ((size + SectorSize - 1) / SectorSize) * SectorSize;
        if (end > FlashBase + FlashSize) throw new ArgumentException("镜像超过该起始地址后的可用 Flash 空间。");
        return (uint)end - 1;
    }

    private static OfflineImage Describe(FileInfo file, OfflinePreset preset)
    {
        uint? end = null;
        try { end = CalculateEraseEnd(preset.Address, file.Length); }
        catch (ArgumentException) { }
        return new(file.Name, file.Length, preset.Address, preset.Reset, end);
    }

    private string PresetPath(string name) => Path.Combine(directory, ".presets",
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name.ToUpperInvariant()))) + ".json");

    private OfflinePreset ReadPreset(string name)
    {
        var metadata = PresetPath(name);
        if (!File.Exists(metadata)) return new();
        try { return JsonSerializer.Deserialize<OfflinePreset>(File.ReadAllText(metadata)) ?? new(); }
        catch (JsonException) { return new(); }
    }

    private static bool IsImageName(string name) => name.Length > 4 && name == Path.GetFileName(name)
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && Encoding.UTF8.GetByteCount(name) < 256
        && name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase);

    private string ImagePath(string name)
    {
        if (!IsImageName(name)) throw new ArgumentException("请选择文件名不超过 255 个 UTF-8 字节的 .bin 镜像。");
        return Path.Combine(directory, name);
    }

    private FileInfo GetImage(string name)
    {
        var file = new FileInfo(ImagePath(name));
        if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new FileNotFoundException("本地镜像不存在或已被移动。", name);
        CheckSize(file.Length);
        return file;
    }

    private static void CheckSize(long size)
    {
        if (size is <= 0 or > FlashSize) throw new ArgumentException("镜像大小须介于 1 字节和 2 MiB 之间。");
    }

    private static async Task CopyAsync(string source, string destination, CancellationToken ct)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".wirelink-{Guid.NewGuid():N}.tmp");
        if (File.Exists(destination)) throw new IOException("已有同名镜像。请使用不同的文件名，原文件已保留。");
        try
        {
            var bytes = new byte[FlashSize + 1];
            int length = 0;
            await using (var input = File.OpenRead(source))
            {
                while (length < bytes.Length)
                {
                    var count = await input.ReadAsync(bytes.AsMemory(length), ct);
                    if (count == 0) break;
                    length += count;
                }
            }
            CheckSize(length);
            var image = bytes.AsMemory(0, length);
            if (length >= 112 && bytes[0] == 0xE9 && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(32)) == 0xABCD5432 &&
                bytes.AsSpan(80, 12).SequenceEqual("esp_swdlink\0"u8))
                throw new ArgumentException("这是烧录器自身的 ESP32 应用镜像。请选择 STM32H743VI 的目标镜像。");
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await output.WriteAsync(image, ct);
                output.Flush(flushToDisk: true);
            }
            await using (var copy = File.OpenRead(temporary))
                if (!SHA256.HashData(image.Span).SequenceEqual(await SHA256.HashDataAsync(copy, ct)))
                    throw new IOException("复制后的镜像校验失败。");
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, destination);
        }
        finally { File.Delete(temporary); }
    }
}
