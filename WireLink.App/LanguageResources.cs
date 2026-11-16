using System.Runtime.Loader;

namespace WireLink.App;

internal static class LanguageResources
{
    public static void Install() => AssemblyLoadContext.Default.Resolving += (context, name) =>
    {
        if (string.IsNullOrEmpty(name.CultureName) || name.Name == null || !name.Name.EndsWith(".resources", StringComparison.Ordinal)) return null;
        var path = Path.Combine(AppContext.BaseDirectory, "lang", Path.GetFileName(name.CultureName), Path.GetFileName(name.Name) + ".dll");
        return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
    };
}
