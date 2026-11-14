using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Sibir.Native
{
    internal static class NativeMethods
    {
        public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_ALL = 0x7;
        public const uint OPEN_EXISTING = 3;
        public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000, FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
        public const uint FSCTL_SET_REPARSE_POINT = 0x000900A4, FSCTL_GET_REPARSE_POINT = 0x000900A8;
        public const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003, IO_REPARSE_TAG_SYMLINK = 0xA000000C;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);

        public const uint FILE_READ_DATA = 0x1, FILE_APPEND_DATA = 0x4, READ_CONTROL = 0x20000, SYNCHRONIZE = 0x100000;
        public const uint CREATE_ALWAYS = 2, OPEN_ALWAYS = 4, FILE_ATTRIBUTE_HIDDEN = 0x2, FILE_ATTRIBUTE_NORMAL = 0x80;
        public const uint INVALID_FILE_ATTRIBUTES = 0xFFFFFFFF, FILE_NAMED_STREAMS = 0x40000;

        [StructLayout(LayoutKind.Sequential)]
        public struct SECURITY_ATTRIBUTES { public int nLength; public IntPtr lpSecurityDescriptor; public int bInheritHandle; }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(SafeFileHandle h, byte[] buf, int count, out int written, IntPtr overlapped);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint GetFileAttributes(string name);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool DeleteFile(string name);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool GetVolumeInformation(string root, StringBuilder name, int nameLen, out uint serial, out uint maxComponent, out uint flags, StringBuilder fs, int fsLen);

        public const int SHCNE_UPDATEDIR = 0x1000, SHCNE_UPDATEITEM = 0x2000; public const uint SHCNF_PATHW = 0x5;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern void SHChangeNotify(int eventId, uint flags, string item1, IntPtr item2);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] inBuf, int inSize, byte[] outBuf, int outSize, out int returned, IntPtr overlapped);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CreateSymbolicLink(string link, string target, int flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool GetVolumePathName(string path, StringBuilder volumePath, int len);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool GetVolumeNameForVolumeMountPoint(string mountPoint, StringBuilder volumeName, int len);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int QueryDosDevice(string device, StringBuilder target, int max);

        public delegate uint CopyProgressRoutine(long total, long transferred, long streamSize, long streamTransferred, uint streamNo, uint reason, IntPtr src, IntPtr dst, IntPtr data);
        public const uint COPY_FILE_FAIL_IF_EXISTS = 0x1, COPY_FILE_NO_BUFFERING = 0x1000, COPY_FILE_COPY_SYMLINK = 0x800;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CopyFileEx(string src, string dst, CopyProgressRoutine progress, IntPtr data, ref int cancel, uint flags);

        [DllImport("kernel32.dll")]
        public static extern bool AttachConsole(int pid);

        // Restart Manager
        [StructLayout(LayoutKind.Sequential)]
        public struct RM_UNIQUE_PROCESS { public int dwProcessId; public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string strServiceShortName;
            public int ApplicationType; public uint AppStatus; public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        public static extern int RmStartSession(out uint handle, int flags, StringBuilder key);
        [DllImport("rstrtmgr.dll")]
        public static extern int RmEndSession(uint handle);
        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        public static extern int RmRegisterResources(uint handle, uint nFiles, string[] files, uint nApps, RM_UNIQUE_PROCESS[] apps, uint nServices, string[] services);
        [DllImport("rstrtmgr.dll")]
        public static extern int RmGetList(uint handle, out uint needed, ref uint count, [In, Out] RM_PROCESS_INFO[] info, ref uint rebootReasons);
    }
}
