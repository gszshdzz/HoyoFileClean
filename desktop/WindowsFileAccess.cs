using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace HoyoFileClean.Desktop;

internal sealed class WindowsDirectoryLease : IDisposable
{
    private readonly Dictionary<string, SafeFileHandle> handles = new(StringComparer.OrdinalIgnoreCase);

    internal static string Normalize(string path)
    {
        // Device paths, UNC shares and path aliases are not game-directory inputs.
        if (!Path.IsPathFullyQualified(path) || path.Length < 3 || path[1] != ':' || !char.IsAsciiLetter(path[0]))
            throw new FileOperationException("FileOperationLocalPathRequired");
        string root = Path.GetPathRoot(path)!;
        string rest = Path.TrimEndingDirectorySeparator(path)[root.Length..];
        if (rest.Length > 0) FilePathRules.NormalizeRelative(rest);
        return DirectoryScanner.NormalizeRootPath(path);
    }

    internal void Pin(string path)
    {
        path = Normalize(path);
        if (handles.ContainsKey(path)) return;
        string? parent = Path.GetDirectoryName(path);
        if (parent is not null) Pin(parent);
        // Keep directories from being renamed while permitting normal child-file
        // operations. Concurrent hostile reparse-point mutation is outside scope.
        var handle = WindowsNative.Open(path, 0x80000000, FileShare.ReadWrite); // GENERIC_READ
        try
        {
            var information = WindowsNative.Information(handle);
            if ((information.Attributes & (uint)(FileAttributes.Directory | FileAttributes.ReparsePoint)) != (uint)FileAttributes.Directory)
                throw new FileOperationException("FileOperationDirectoryUnsafe");
            handles.Add(path, handle);
        }
        catch { handle.Dispose(); throw; }
    }

    internal string PhysicalPath(string path)
    {
        Pin(path);
        var buffer = new StringBuilder(32768);
        uint length = WindowsNative.GetFinalPathNameByHandleW(handles[Normalize(path)], buffer, (uint)buffer.Capacity, 1);
        if (length == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (length >= buffer.Capacity) throw new PathTooLongException();
        return Path.TrimEndingDirectorySeparator(buffer.ToString());
    }

    internal void CheckDestination(string path)
    {
        string parent = Path.GetDirectoryName(path)!;
        // Walk from a pinned ancestor downwards; never follow a reparse point.
        CheckParents(parent, create: false);
        RejectExisting(path);
    }

    internal void EnsureDestination(string path)
    {
        CheckParents(Path.GetDirectoryName(path)!, create: true);
        RejectExisting(path);
    }

    private void CheckParents(string directory, bool create)
    {
        if (handles.ContainsKey(directory)) return;
        string? parent = Path.GetDirectoryName(directory);
        if (parent is null) { Pin(directory); return; }
        CheckParents(parent, create);
        uint attributes = WindowsNative.GetFileAttributesW(directory);
        if (attributes == uint.MaxValue)
        {
            int error = Marshal.GetLastWin32Error();
            if (error is not (2 or 3)) throw new Win32Exception(error);
            if (!create) return;
            if (!WindowsNative.CreateDirectoryW(directory, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        Pin(directory);
    }

    private static void RejectExisting(string path)
    {
        uint attributes = WindowsNative.GetFileAttributesW(path);
        if (attributes != uint.MaxValue) throw new FileOperationException("FileOperationTargetExists");
        int error = Marshal.GetLastWin32Error();
        if (error is not (2 or 3)) throw new Win32Exception(error);
    }

    public void Dispose()
    {
        foreach (var handle in handles.Values.Reverse()) handle.Dispose();
        handles.Clear();
    }
}

internal readonly record struct WindowsFileIdentity(uint Volume, uint IndexHigh, uint IndexLow,
    uint SizeHigh, uint SizeLow, uint WriteHigh, uint WriteLow);

internal sealed class WindowsSourceFile : IDisposable
{
    private readonly FileStream stream;
    private readonly string path;
    internal WindowsFileIdentity Identity { get; }

    private WindowsSourceFile(string path, SafeFileHandle handle)
    {
        this.path = path;
        var information = WindowsNative.Information(handle);
        if ((information.Attributes & (uint)(FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.ReadOnly)) != 0
            || information.Links != 1)
            throw new FileOperationException("FileOperationFileUnsafe");
        Identity = new(information.Volume, information.IndexHigh, information.IndexLow,
            information.SizeHigh, information.SizeLow, information.WriteHigh, information.WriteLow);
        stream = new FileStream(handle, FileAccess.Read);
    }

    internal static WindowsSourceFile Open(string path, bool allowDelete = false)
    {
        var handle = WindowsNative.Open(path, 0x80010000,
            allowDelete ? FileShare.Read | FileShare.Delete : FileShare.Read); // GENERIC_READ | DELETE
        try { return new(path, handle); }
        catch { handle.Dispose(); throw; }
    }

    internal void Validate(ManifestFile file)
    {
        if (stream.Length != file.Size) throw new FileOperationException("FileOperationFileChanged");
        if (file.Hash is not null)
        {
            stream.Position = 0;
            if (!Convert.ToHexString(MD5.HashData(stream)).Equals(file.Hash, StringComparison.OrdinalIgnoreCase))
                throw new FileOperationException("FileOperationFileChanged");
        }
        stream.Position = 0;
    }

    internal void MoveTo(string destination)
    {
        byte[] name = Encoding.Unicode.GetBytes(destination);
        int offset = Marshal.OffsetOf<WindowsNative.RenameInformation>(nameof(WindowsNative.RenameInformation.FileName)).ToInt32();
        byte[] buffer = new byte[offset + name.Length + 2];
        BitConverter.TryWriteBytes(buffer.AsSpan(offset - sizeof(uint)), name.Length);
        name.CopyTo(buffer, offset);
        if (WindowsNative.SetFileInformationByHandle(stream.SafeFileHandle, 3, buffer, (uint)buffer.Length)) return;
        int error = Marshal.GetLastWin32Error();
        if (error != 17) throw new Win32Exception(error); // ERROR_NOT_SAME_DEVICE only
        CopyThenDelete(destination);
    }

    internal void CopyThenDelete(string destination)
    {
        // CopyFile fails on an existing destination. The locked source cannot be replaced/written.
        if (!WindowsNative.CopyFileW(path, destination, true)) throw new Win32Exception(Marshal.GetLastWin32Error());
        using var copy = WindowsSourceFile.Open(destination);
        stream.Position = 0;
        if (stream.Length != copy.stream.Length || !SHA256.HashData(stream).AsSpan().SequenceEqual(SHA256.HashData(copy.stream)))
            throw new FileOperationException("FileOperationCopyMismatch");
        Delete();
    }

    internal void Delete()
    {
        if (!WindowsNative.SetFileInformationByHandle(stream.SafeFileHandle, 4, [1], 1))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Dispose() => stream.Dispose();
}

internal static class WindowsNative
{
    internal static SafeFileHandle Open(string path, uint access, FileShare share)
    {
        var handle = CreateFileW(path, access, share, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (!handle.IsInvalid) return handle; // BACKUP_SEMANTICS | OPEN_REPARSE_POINT
        int error = Marshal.GetLastWin32Error();
        handle.Dispose();
        throw new Win32Exception(error);
    }

    internal static FileInformation Information(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return info;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FileInformation
    {
        internal uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        internal uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RenameInformation
    {
        internal uint Flags;
        internal IntPtr RootDirectory;
        internal uint FileNameLength;
        internal char FileName;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, FileShare share, IntPtr security,
        uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetFileInformationByHandle(SafeFileHandle handle, int kind, byte[] information, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    internal static extern uint GetFileAttributesW(string path);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    internal static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint capacity, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateDirectoryW(string path, IntPtr security);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CopyFileW(string source, string destination, [MarshalAs(UnmanagedType.Bool)] bool failIfExists);
}
