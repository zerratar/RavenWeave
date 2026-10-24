using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace Ravenfall.Updater.Core
{
    /// <summary>
    /// Which programs hold files open, from Windows' Restart Manager (what installers use to say "close these
    /// programs first"). Windows only; elsewhere it finds nobody.
    /// </summary>
    public static class FileLocks
    {
        private const int ErrorMoreData = 234;
        private const int CchRmMaxAppName = 255;
        private const int CchRmMaxSvcName = 63;

        [StructLayout(LayoutKind.Sequential)]
        private struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxAppName + 1)] public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxSvcName + 1)] public string strServiceShortName;
            public int ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmEndSession(uint pSessionHandle);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmRegisterResources(uint pSessionHandle, uint nFiles, string[] rgsFilenames,
            uint nApplications, RM_UNIQUE_PROCESS[] rgApplications, uint nServices, string[] rgsServiceNames);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded, ref uint pnProcInfo,
            [In, Out] RM_PROCESS_INFO[] rgAffectedApps, ref uint lpdwRebootReasons);

        /// <summary>The processes that have any of the files open; none when it cannot be told.</summary>
        public static List<Process> WhoIsLocking(IEnumerable<string> files)
        {
            var result = new List<Process>();
            var paths = files?.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToArray();
            if (paths == null || paths.Length == 0 || !OperatingSystem.IsWindows())
            {
                return result;
            }

            uint session;
            try
            {
                if (RmStartSession(out session, 0, Guid.NewGuid().ToString()) != 0) return result;
            }
            catch
            {
                return result; // no Restart Manager
            }

            try
            {
                if (RmRegisterResources(session, (uint)paths.Length, paths, 0, null, 0, null) != 0) return result;

                uint needed;
                uint count = 0;
                uint reasons = 0;
                var status = RmGetList(session, out needed, ref count, null, ref reasons);
                if (status != ErrorMoreData || needed == 0) return result;

                var infos = new RM_PROCESS_INFO[needed];
                count = needed;
                if (RmGetList(session, out needed, ref count, infos, ref reasons) != 0) return result;

                var self = Process.GetCurrentProcess().Id;
                for (var i = 0; i < count; ++i)
                {
                    var id = infos[i].Process.dwProcessId;
                    if (id == self) continue;
                    try { result.Add(Process.GetProcessById(id)); } catch { }
                }
            }
            finally
            {
                RmEndSession(session);
            }

            return result;
        }

        /// <summary>The program's file, or null when Windows will not say (a protected or elevated process).</summary>
        public static string ExecutableOf(Process process)
        {
            try { return process.MainModule?.FileName; } catch { return null; }
        }
    }
}
