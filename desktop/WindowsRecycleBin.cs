using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace HoyoFileClean.Desktop;

// Windows Shell is needed only for the system Recycle Bin. Move/delete use file handles.
internal sealed class WindowsRecycleBin : IDisposable
{
    private readonly IFileOperation operation;

    internal WindowsRecycleBin()
    {
        operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(
            new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"), throwOnError: true)!)!;
    }

    internal static T RunOnSta<T>(Func<T> work)
    {
        Exception? failure = null;
        T? result = default;
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception error) { failure = error; }
        }) { IsBackground = true, Name = "Recycle selected files" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        return result!;
    }

    internal string Recycle(string path, ManifestFile expected, WindowsFileIdentity identity)
    {
        Guid shellItemId = typeof(IShellItem).GUID;
        Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, IntPtr.Zero, ref shellItemId, out var item));
        try
        {
            // RECYCLEONDELETE, EARLYFAILURE, NOCOPYHOOKS, NO_CONNECTED_ELEMENTS,
            // NOERRORUI, NOCONFIRMMKDIR, NOCONFIRMATION, SILENT. No elevation or permanent fallback.
            operation.SetOperationFlags(0x00982614);
            var sink = new RecycleGuard(path, expected, identity);
            operation.DeleteItem(item, sink);
            operation.PerformOperations();
            operation.GetAnyOperationsAborted(out bool aborted);
            if (sink.Failure is not null) ExceptionDispatchInfo.Capture(sink.Failure).Throw();
            if (aborted || !sink.Completed)
                throw new FileOperationException("FileOperationRecycleFailed");
            return sink.RecycledPath!;
        }
        finally { Marshal.FinalReleaseComObject(item); }
    }

    public void Dispose() => Marshal.FinalReleaseComObject(operation);

    // Reject a Shell attempt to permanently delete or to touch a connected item.
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    internal sealed class RecycleGuard(string path, ManifestFile expected, WindowsFileIdentity identity) : IFileOperationProgressSink
    {
        internal bool Completed { get; private set; }
        internal Exception? Failure { get; private set; }
        internal string? RecycledPath { get; private set; }
        private const int Abort = unchecked((int)0x80004004);
        public int PreDeleteItem(uint flags, IShellItem item)
        {
            try
            {
                if ((flags & 0x80) == 0) throw new FileOperationException("FileOperationRecycleFailed");
                item.GetDisplayName(0x80058000, out string actual); // SIGDN_FILESYSPATH
                if (!string.Equals(actual, path, StringComparison.OrdinalIgnoreCase))
                    throw new FileOperationException("FileOperationOutsideSelection");
                using var input = WindowsSourceFile.Open(path, allowDelete: true);
                input.Validate(expected);
                if (input.Identity != identity) throw new FileOperationException("FileOperationFileChanged");
                return 0;
            }
            catch (Exception error) { Failure = error; return Abort; }
        }
        public int PostDeleteItem(uint flags, IShellItem item, int result, IShellItem? created)
        {
            try
            {
                if (result < 0) Failure = Marshal.GetExceptionForHR(result);
                if (result >= 0 && (flags & 0x80) != 0 && created is not null)
                {
                    created.GetDisplayName(0x80058000, out string actual);
                    RecycledPath = actual;
                    Completed = true;
                }
            }
            catch (Exception error) { Failure = error; return Abort; }
            return 0;
        }
        public int StartOperations() => 0;
        public int FinishOperations(int result) => 0;
        public int PreRenameItem(uint flags, IShellItem item, string name) => Abort;
        public int PostRenameItem(uint flags, IShellItem item, string name, int result, IShellItem created) => 0;
        public int PreMoveItem(uint flags, IShellItem item, IShellItem destination, string name) => Abort;
        public int PostMoveItem(uint flags, IShellItem item, IShellItem destination, string name, int result, IShellItem created) => 0;
        public int PreCopyItem(uint flags, IShellItem item, IShellItem destination, string name) => Abort;
        public int PostCopyItem(uint flags, IShellItem item, IShellItem destination, string name, int result, IShellItem created) => 0;
        public int PreNewItem(uint flags, IShellItem destination, string name) => Abort;
        public int PostNewItem(uint flags, IShellItem destination, string name, string template, uint attributes, int result, IShellItem created) => 0;
        public int UpdateProgress(uint total, uint done) => 0;
        public int ResetTimer() => 0;
        public int PauseTimer() => 0;
        public int ResumeTimer() => 0;
    }

    // Vtable order and signatures: Windows SDK shobjidl_core.h (no external COM package).
    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileOperation
    {
        void Advise(IFileOperationProgressSink sink, out uint cookie);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        void SetProgressDialog(IntPtr dialog);
        void SetProperties(IntPtr properties);
        void SetOwnerWindow(IntPtr owner);
        void ApplyPropertiesToItem(IShellItem item);
        void ApplyPropertiesToItems(IntPtr items);
        void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink sink);
        void RenameItems(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] string name);
        void MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink? sink);
        void MoveItems(IntPtr items, IShellItem destination);
        void CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink sink);
        void CopyItems(IntPtr items, IShellItem destination);
        void DeleteItem(IShellItem item, IFileOperationProgressSink sink);
        void DeleteItems(IntPtr items);
        void NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name,
            [MarshalAs(UnmanagedType.LPWStr)] string template, IFileOperationProgressSink sink);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem
    {
        void BindToHandler(IntPtr context, ref Guid handler, ref Guid id, out IntPtr result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint kind, [MarshalAs(UnmanagedType.LPWStr)] out string name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }

    [ComVisible(true), Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int result);
        [PreserveSig] int PreRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem created);
        [PreserveSig] int PreMoveItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostMoveItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem created);
        [PreserveSig] int PreCopyItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostCopyItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem created);
        [PreserveSig] int PreDeleteItem(uint flags, IShellItem item);
        [PreserveSig] int PostDeleteItem(uint flags, IShellItem item, int result, IShellItem? created);
        [PreserveSig] int PreNewItem(uint flags, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostNewItem(uint flags, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name,
            [MarshalAs(UnmanagedType.LPWStr)] string template, uint attributes, int result, IShellItem created);
        [PreserveSig] int UpdateProgress(uint total, uint done);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int SHCreateItemFromParsingName(string path, IntPtr context, ref Guid id, out IShellItem item);
}
