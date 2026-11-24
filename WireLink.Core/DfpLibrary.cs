using System.Security.Cryptography;

namespace WireLink.Core;

public sealed class DfpLibrary(string root)
{
    public string DirectoryPath { get; } = Path.Combine(root, "data", "dfp");

    public Settings Resolve(Settings settings) => string.IsNullOrWhiteSpace(settings.Pack) ? settings
        : settings with { Pack = Path.GetFullPath(Path.Combine(root, settings.Pack)) };

    public async Task<string> ImportAsync(string path, CancellationToken ct)
    {
        var source = Validation.FilePath(Path.Combine(root, path), ".pack");
        if (source.StartsWith(DirectoryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return Reference(source);

        Directory.CreateDirectory(DirectoryPath);
        var temporary = Path.Combine(DirectoryPath, $"{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var input = File.OpenRead(source))
            await using (var output = File.Create(temporary))
                await input.CopyToAsync(output, ct);
            string digest;
            await using (var copy = File.OpenRead(temporary))
                digest = Convert.ToHexString(await SHA256.HashDataAsync(copy, ct)).ToLowerInvariant();
            var version = Directory.CreateDirectory(Path.Combine(DirectoryPath, digest));
            var destination = Path.Combine(version.FullName, Path.GetFileName(source));
            try { File.Move(temporary, destination); }
            catch (IOException) when (File.Exists(destination)) { }
            return Reference(destination);
        }
        finally { File.Delete(temporary); }
    }

    private string Reference(string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
}
