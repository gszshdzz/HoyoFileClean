namespace HoyoFileClean.Desktop;

internal static class Program
{
    internal static void InitializeApplication() => ApplicationConfiguration.Initialize();

    [STAThread]
    private static void Main()
    {
        InitializeApplication();
        Application.Run(new MainForm());
    }
}
