namespace WireLink.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        StartupTrace.Start();
        LanguageResources.Install();
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
