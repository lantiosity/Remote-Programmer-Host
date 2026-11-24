using WireLink.Core;

internal static class LocalLibraryTests
{
    public static void Register(List<(string Name, Func<Task> Run)> tests, string temporary)
    {
        tests.Add(("DFP 导入保留版本并在原文件移除和发布目录迁移后可用", async () =>
        {
            var path = Path.Combine(temporary, "Example.pack");
            await File.WriteAllBytesAsync(path, [1, 2, 3]);
            var root = Path.Combine(temporary, "portable-a");
            var library = new DfpLibrary(root);
            var first = await library.ImportAsync(path, CancellationToken.None);
            Check(first.StartsWith("data/dfp/") && first == await library.ImportAsync(path, CancellationToken.None));
            await File.WriteAllBytesAsync(path, [4, 5, 6]);
            var second = await library.ImportAsync(path, CancellationToken.None);
            Check(second != first);
            File.Delete(path);
            var relocated = Path.Combine(temporary, "portable-b");
            Directory.Move(root, relocated);
            var moved = new DfpLibrary(relocated);
            var resolved = moved.Resolve(new Settings { Pack = first }).Pack;
            Check((await File.ReadAllBytesAsync(resolved)).SequenceEqual(new byte[] { 1, 2, 3 }));
            Check(await moved.ImportAsync(resolved, CancellationToken.None) == first);
        }));

        tests.Add(("离线镜像保留参数并保护同名文件和卡根目录", async () =>
        {
            var path = Path.Combine(temporary, "target-h743.bin");
            await File.WriteAllBytesAsync(path, new byte[128 * 1024 + 1]);
            var dir = Path.Combine(temporary, "offline-library");
            var library = new OfflineLibrary(dir);
            Check(await library.ImportAsync(path, CancellationToken.None) == "target-h743.bin");
            var image = library.List().Single();
            Check(image.EraseEnd == 0x0803FFFF && !image.Reset);
            await File.WriteAllBytesAsync(path, [7]);
            await Reject<IOException>(() => library.ImportAsync(path, CancellationToken.None));
            Check(library.List().Single().Size == 128 * 1024 + 1);
            await Reject<ArgumentException>(() => library.ExportAsync(image.Name, dir, CancellationToken.None));
            var updated = library.SetPreset(image.Name, "0x08040000", true);
            Check(updated.EraseEnd == 0x0807FFFF && new OfflineLibrary(dir).List().Single() == updated);
            await Reject<ArgumentException>(() => Task.FromResult(library.SetPreset(image.Name, "0x08040001", false)));
            await Reject<ArgumentException>(() => Task.FromResult(library.SetPreset(image.Name, "0x081E0000", false)));
            await Reject<ArgumentException>(() => Task.FromResult(library.SetPreset("../target-h743.bin", "0x08000000", false)));
            library.Remove(image.Name);
            Check(!library.List().Any() && File.Exists(path));
        }));

        tests.Add(("离线镜像拒绝超限和烧录器自身的 ESP32 应用", async () =>
        {
            var source = Path.Combine(temporary, "oversized.bin");
            var library = new OfflineLibrary(Path.Combine(temporary, "offline-rejected"));
            await File.WriteAllBytesAsync(source, new byte[OfflineLibrary.FlashSize + 1]);
            await Reject<ArgumentException>(() => library.ImportAsync(source, CancellationToken.None));
            var bytes = new byte[288];
            bytes[0] = 0xE9;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(32), 0xABCD5432);
            "esp_swdlink\0"u8.CopyTo(bytes.AsSpan(80));
            await File.WriteAllBytesAsync(source, bytes);
            await Reject<ArgumentException>(() => library.ImportAsync(source, CancellationToken.None));
            Check(!library.List().Any());
        }));
    }

    private static void Check(bool value)
    {
        if (!value) throw new Exception("Local library assertion failed");
    }

    private static async Task Reject<T>(Func<Task> operation) where T : Exception
    {
        try { await operation(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}");
    }
}
