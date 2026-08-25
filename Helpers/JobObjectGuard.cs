using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LinqqXrayVPN.Helpers
{
    /// <summary>
    /// Kill-on-close job object so a helper xray process dies with the host.
    /// </summary>
    public sealed partial class JobObjectGuard : IDisposable
    {
        private IntPtr _handle;

        private JobObjectGuard(IntPtr handle) => _handle = handle;

        public static JobObjectGuard? Create()
        {
            var handle = CreateKillOnCloseJob();
            return handle == IntPtr.Zero ? null : new JobObjectGuard(handle);
        }

        public bool TryAssign(Process process)
            => _handle != IntPtr.Zero && AssignProcessToJobObject(_handle, process.Handle);

        public static JobObjectGuard? Assign(Process process)
        {
            var guard = Create();
            if (guard is null)
                return null;

            if (!guard.TryAssign(process))
            {
                guard.Dispose();
                return null;
            }

            return guard;
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                _ = CloseHandle(_handle);
                _handle = IntPtr.Zero;
            }
        }

        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
        private const int JobObjectExtendedLimitInformation = 9;

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [LibraryImport("kernel32.dll", SetLastError = true)]
        private static partial IntPtr CreateJobObjectW(IntPtr lpJobAttributes, IntPtr lpName);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetInformationJobObject(
            IntPtr hJob, int jobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CloseHandle(IntPtr hObject);

        private static IntPtr CreateKillOnCloseJob()
        {
            var handle = CreateJobObjectW(IntPtr.Zero, IntPtr.Zero);
            if (handle == IntPtr.Zero)
                return IntPtr.Zero;

            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                }
            };

            int size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, buf, fDeleteOld: false);
                if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformation, buf, (uint)size))
                {
                    _ = CloseHandle(handle);
                    return IntPtr.Zero;
                }
                return handle;
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }
    }
}
