using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace HireZero.Roles.Developer;

// Keep each pathname component from being renamed, deleted or made into a reparse point.
// Unsupported filesystems/platforms fail closed; checking a path and later following it is insufficient.
public sealed class CloneDirectoryLease : IDisposable
{
    private readonly List<SafeFileHandle> _handles = [];

    private CloneDirectoryLease() { }

    public static CloneDirectoryLease Acquire(string path)
    {
        if (!OperatingSystem.IsWindows())
            throw new ReviewAccessException(
                "Automatic clone requires verified Windows directory locks. Clone the approved repository manually and connect it instead.",
                503
            );
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!);
        if (drive.DriveType != DriveType.Fixed || !drive.DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase))
            throw new ReviewAccessException(
                "Automatic clone requires a verified local NTFS folder. Clone the approved repository manually and connect it instead.",
                503
            );
        var lease = new CloneDirectoryLease();
        try
        {
            var ancestors = new Stack<string>();
            for (
                var directory = new DirectoryInfo(Path.GetFullPath(path));
                directory is not null;
                directory = directory.Parent
            )
                ancestors.Push(directory.FullName);
            foreach (var ancestor in ancestors)
                lease._handles.Add(OpenComponent(ancestor));
            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    internal static SafeFileHandle OpenComponent(string path)
    {
        // A zero-access metadata handle does not enforce the sharing exclusion on directory rename.
        // GENERIC_READ makes this an actual open participating in READ/WRITE/DELETE share checks.
        var handle = CreateFile(path, 0x80000000, 1, IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var code = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new ReviewAccessException(
                "Automatic clone is unavailable: a required directory cannot be locked (Windows error "
                    + code
                    + "). Nothing was cloned; connect a manual clone instead.",
                503
            );
        }
        try
        {
            if (
                !GetFileInformationByHandleEx(handle, 9, out var info, (uint)Marshal.SizeOf<AttributeTagInfo>())
                || (info.Attributes & 0x400) != 0
                || (info.Attributes & 0x10) == 0
            )
                throw new ReviewAccessException(
                    "The clone path is not a verified ordinary local directory. Nothing was cloned.",
                    409
                );
            var resolved = new StringBuilder(32768);
            var size = GetFinalPathNameByHandle(handle, resolved, (uint)resolved.Capacity, 0);
            if (size == 0 || size >= resolved.Capacity)
                throw new ReviewAccessException(
                    "The locked clone path could not be verified. Nothing was cloned.",
                    503
                );
            var actual = resolved.ToString();
            if (actual.StartsWith("\\\\?\\", StringComparison.Ordinal))
                actual = actual[4..];
            if (
                !Path.TrimEndingDirectorySeparator(actual)
                    .Equals(
                        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)),
                        StringComparison.OrdinalIgnoreCase
                    )
            )
                throw new ReviewAccessException(
                    "The clone path resolved outside the selected directory. Nothing was cloned.",
                    409
                );
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        for (var index = _handles.Count - 1; index >= 0; index--)
            _handles[index].Dispose();
        _handles.Clear();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeTagInfo
    {
        public uint Attributes;
        public uint ReparseTag;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string name,
        uint access,
        uint share,
        IntPtr security,
        uint creation,
        uint flags,
        IntPtr template
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle handle,
        int kind,
        out AttributeTagInfo information,
        uint size
    );

    [DllImport(
        "kernel32.dll",
        EntryPoint = "GetFinalPathNameByHandleW",
        CharSet = CharSet.Unicode,
        SetLastError = true
    )]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle handle,
        StringBuilder path,
        uint size,
        uint flags
    );
}
