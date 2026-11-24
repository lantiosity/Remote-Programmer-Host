using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using WireLink.Core;

namespace WireLink.App;

internal sealed class ThemeBackgroundStore(string root)
{
    public const string HostName = "wirelink-theme.example";
    public string DirectoryPath { get; } = Path.Combine(root, "data", "theme");
    private string? draft;

    public bool Exists(string? id) => ThemeAppearance.IsImageId(id) && File.Exists(FileName(id!));

    public async Task<string> ImportAsync(string source, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N");
        var path = FileName(id);
        Directory.CreateDirectory(DirectoryPath);
        try
        {
            await Task.Run(() => ConvertImage(source, path, ct), ct);
            ct.ThrowIfCancellationRequested();
        }
        catch { Remove(id); throw; }
        Discard();
        draft = id;
        return id;
    }

    public bool CanSave(string? id, string? current) => id == null || (id == draft || id == current) && Exists(id);

    public void Commit(string? image, string? previous)
    {
        if (image == draft) draft = null;
        Discard();
        if (image != previous) Remove(previous);
    }

    public void Discard()
    {
        Remove(draft);
        draft = null;
    }

    private string FileName(string id) => Path.Combine(DirectoryPath, id + ".png");

    private void Remove(string? id)
    {
        if (!ThemeAppearance.IsImageId(id)) return;
        try { File.Delete(FileName(id!)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void ConvertImage(string source, string destination, CancellationToken ct)
    {
        using var input = File.OpenRead(source);
        if (input.Length is <= 0 or > 20 * 1024 * 1024)
            throw new ArgumentException("请选择不超过 20 MiB 的背景图片。");
        try
        {
            using var image = Image.FromStream(input, useEmbeddedColorManagement: false, validateImageData: false);
            var format = image.RawFormat.Guid;
            if (format != ImageFormat.Png.Guid && format != ImageFormat.Jpeg.Guid && format != ImageFormat.Bmp.Guid)
                throw new ArgumentException("背景图片支持 PNG、JPG 和 BMP。");
            if ((long)image.Width * image.Height > 40_000_000)
                throw new ArgumentException("背景图片不能超过 4000 万像素。");
            var scale = Math.Min(1d, 2400d / Math.Max(image.Width, image.Height));
            using var bitmap = new Bitmap(Math.Max(1, (int)Math.Round(image.Width * scale)),
                Math.Max(1, (int)Math.Round(image.Height * scale)), PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(image, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            }
            ct.ThrowIfCancellationRequested();
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            bitmap.Save(output, ImageFormat.Png);
        }
        catch (OutOfMemoryException ex) { throw new InvalidDataException("无法读取该背景图片，请检查文件格式。", ex); }
    }
}
