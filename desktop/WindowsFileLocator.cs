using System.Diagnostics;
using System.ComponentModel;

namespace HoyoFileClean.Desktop;

internal static class WindowsFileLocator
{
    internal static void Locate(IWin32Window owner, string path, Action<string>? launch = null,
        Action<string>? reportError = null)
    {
        try
        {
            path = WindowsDirectoryLease.Normalize(path);
            if (!File.Exists(path)) throw new FileOperationException("FileLocationMissing");
            using var directories = new WindowsDirectoryLease();
            directories.Pin(Path.GetDirectoryName(path)!);
            if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new FileOperationException("FileLocationUnsafe");
            (launch ?? OpenExplorer)(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or Win32Exception or CoreException)
        {
            string message = error is FileOperationException ? error.Message : L.Get("FileLocationFailed");
            ReportError(owner, message, reportError);
        }
    }

    internal static void ReportError(IWin32Window owner, string message, Action<string>? reportError = null)
    {
        if (reportError is not null) reportError(message);
        else MessageBox.Show(owner, message, L.Get("FileLocationTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static void OpenExplorer(string path)
    {
        // Normalize rejects quotes and aliases; pass a quoted path only to Explorer's select mode,
        // never launch the listed file or resolve an executable from the working directory.
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
            Arguments = $"/select,\"{path}\"", UseShellExecute = false, CreateNoWindow = true
        });
    }
}
