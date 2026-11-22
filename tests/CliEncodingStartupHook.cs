using System.Text;

// The runtime requires this test-only startup hook to be in the global namespace.
internal static class StartupHook
{
    public static void Initialize()
    {
        Console.OutputEncoding = Encoding.ASCII;
    }
}
