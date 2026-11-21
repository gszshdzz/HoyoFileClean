namespace HoyoFileClean.Desktop;

public enum FileOperationState { Pending, Succeeded, Failed, NotProcessed }

public sealed record FileOperationItemResult(string Path, FileOperationState State, string Message);

public sealed record FileOperationResult(IReadOnlyList<FileOperationItemResult> Files, string? Error = null,
    bool ExecutionStarted = false)
{
    public int SucceededCount => Files.Count(file => file.State == FileOperationState.Succeeded);
}

public static class FileOperationExecutor
{
    public static FileOperationResult Execute(FileOperationRequest request, FileListSnapshot game,
        Action<FileOperationItemResult>? progress = null)
    {
        if (request.Kind == FileOperationKind.Recycle && Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            return WindowsRecycleBin.RunOnSta(() => Execute(request, game, progress));
        request = request with { Files = Array.AsReadOnly(request.Files.ToArray()) };
        var results = request.Files.Select(row => new FileOperationItemResult(row.Path,
            FileOperationState.NotProcessed, "")).ToArray();
        using var directories = new WindowsDirectoryLease();
        var identities = new WindowsFileIdentity[results.Length];
        string? target = null;
        int index = 0;
        bool executing = false;
        try
        {
            WizardSession.ValidateGameInput(game, request.Root);
            string root = WindowsDirectoryLease.Normalize(request.Root);
            if (!Enum.IsDefined(request.Kind) || request.Files.Count == 0)
                throw new InvalidDataException(L.Get("FileOperationInvalidRequest"));
            FilePathRules.ValidatePaths(request.Files.Select(row => row.Path));
            if (request.Kind == FileOperationKind.Move)
                target = ValidateMoveDestination(root, request.Destination ?? "", directories);
            directories.Pin(root);
            // Read-only whole-batch preflight. No destination directories are created here.
            for (index = 0; index < request.Files.Count; index++)
            {
                DifferenceRow row = request.Files[index];
                string relative = FilePathRules.NormalizeRelative(row.Path);
                if (!row.CanSelect || !game.Files.TryGetValue(relative, out ManifestFile? scanned)
                    || scanned != row.Game || game.IsOutsideScope(relative))
                    throw new InvalidDataException(L.Get("FileOperationOutsideSelection"));
                string source = Path.Combine(root, relative);
                directories.Pin(Path.GetDirectoryName(source)!);
                using var input = WindowsSourceFile.Open(source);
                input.Validate(scanned);
                identities[index] = input.Identity;
                if (target is not null)
                    directories.CheckDestination(Path.Combine(target, relative));
            }
            using var recycle = request.Kind == FileOperationKind.Recycle ? new WindowsRecycleBin() : null;
            executing = true;
            for (index = 0; index < request.Files.Count; index++)
            {
                DifferenceRow row = request.Files[index];
                string source = Path.Combine(root, row.Path);
                using var input = WindowsSourceFile.Open(source, allowDelete: request.Kind == FileOperationKind.Recycle);
                input.Validate(row.Game!);
                if (input.Identity != identities[index])
                    throw new IOException(L.Get("FileOperationFileChanged"));
                string message;
                if (request.Kind == FileOperationKind.Delete)
                {
                    input.Delete();
                    message = L.Get("FileOperationDeleted");
                }
                else if (request.Kind == FileOperationKind.Recycle)
                {
                    recycle!.Recycle(source, row.Game!, identities[index]);
                    message = L.Get("FileOperationRecycled");
                }
                else
                {
                    string destination = Path.Combine(target!, row.Path);
                    directories.EnsureDestination(destination);
                    input.MoveTo(destination);
                    message = destination;
                }
                results[index] = new(row.Path, FileOperationState.Succeeded, message);
                input.Dispose();
                progress?.Invoke(results[index]);
            }
            return new(Array.AsReadOnly(results), ExecutionStarted: true);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException
            or System.ComponentModel.Win32Exception or NotSupportedException or CoreException
            or System.Runtime.InteropServices.COMException)
        {
            string detail = error is System.ComponentModel.Win32Exception { NativeErrorCode: 32 or 33 }
                ? L.Get("FileOperationFileBusy") + " " + error.Message : error.Message;
            if (index < results.Length)
                results[index] = new(results[index].Path, FileOperationState.Failed, detail);
            string message = L.Format(executing ? "FileOperationExecutionStopped" : "FileOperationPreflightFailed", detail);
            return new(Array.AsReadOnly(results), message, executing);
        }
    }

    internal static string ValidateMoveDestination(string root, string target)
    {
        using var directories = new WindowsDirectoryLease();
        return ValidateMoveDestination(root, target, directories);
    }

    private static string ValidateMoveDestination(string root, string target, WindowsDirectoryLease directories)
    {
        root = WindowsDirectoryLease.Normalize(root);
        target = WindowsDirectoryLease.Normalize(target);
        if (Within(root, target) || Within(target, root))
            throw new InvalidDataException(L.Get("FileOperationOverlappingRoots"));
        string physicalRoot = directories.PhysicalPath(root), physicalTarget = directories.PhysicalPath(target);
        if (Within(physicalRoot, physicalTarget) || Within(physicalTarget, physicalRoot))
            throw new InvalidDataException(L.Get("FileOperationOverlappingRoots"));
        return target;
    }

    private static bool Within(string parent, string path) => path.Equals(parent, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
