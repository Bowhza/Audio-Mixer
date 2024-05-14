using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Audio_Driver
{
    internal static class ActiveProcess
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        public static string GetActiveProcessName()
        {
            IntPtr hWnd = GetForegroundWindow();
            int length = GetWindowTextLength(hWnd);
            StringBuilder title = new StringBuilder(length + 1);
            GetWindowText(hWnd, title, title.Capacity);
            return title.ToString();
        }

        public static Process GetActiveProcess()
        {
            IntPtr hWnd = GetForegroundWindow();
            uint processId;
            GetWindowThreadProcessId(hWnd, out processId);
            return Process.GetProcessById((int)processId);
        }

        public static bool IsForegroundWindow(uint processId)
        {
            IntPtr hWnd = GetForegroundWindow();
            uint activeProcessId;
            GetWindowThreadProcessId(hWnd, out activeProcessId);
            return processId == activeProcessId;
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }
}
