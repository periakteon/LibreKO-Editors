using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace LibreKO.Editors.Core.Sources;

public static class SourceIdentity
{
    public static string Root(string path)
    {
        path = SourcePaths.Canonical(path);
        SourcePaths.AssertNoLinks(path);
        if (!Directory.Exists(path)) throw new InvalidOperationException("Source root is unavailable: " + path);
        return Hash(path + "|" + Physical(path));
    }

    // Also used for overlap detection when different profiles name the same physical file.
    public static string Physical(string path)
    {
        SourcePaths.AssertNoLinks(path);
        if (!OperatingSystem.IsWindows())
            return SourcePaths.Canonical(path) + "|" + File.GetCreationTimeUtc(path).Ticks;
        using var handle = CreateFileW(path, 0, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, 3,
            0x02000000 | 0x00200000, IntPtr.Zero); // BACKUP_SEMANTICS | OPEN_REPARSE_POINT
        if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info))
            throw new IOException("Cannot identify source path.", new Win32Exception(Marshal.GetLastWin32Error()));
        return $"{info.VolumeSerialNumber:X8}:{info.FileIndexHigh:X8}{info.FileIndexLow:X8}";
    }

    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    [StructLayout(LayoutKind.Sequential)]
    struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, AccessTime, WriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFileW(string name, uint access, FileShare share, IntPtr security,
        uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation info);
}
