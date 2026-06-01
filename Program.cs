using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace MasselGUARD
{
    public static class Program
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetDllDirectory(string lpPathName);

        // Used to suppress the console window on GUI / service launches now that
        // OutputType=Exe (console subsystem) is required for proper CLI behaviour.
        [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
        [DllImport("kernel32.dll")] private static extern bool   FreeConsole();
        [DllImport("kernel32.dll")] private static extern uint   GetConsoleProcessList(
            [Out] uint[] lpdwProcessList, uint dwProcessCount);
        [DllImport("user32.dll")]   private static extern bool   ShowWindow(IntPtr hWnd, int nCmdShow);

        [STAThread]
        public static int Main(string[] args)
        {
            // ── Early UAC bypass for managed installs ─────────────────────────
            // If a scheduled task 'MasselGUARD' exists and we're NOT already
            // elevated, relaunch via the task (which runs at RunLevel=Highest
            // without a UAC prompt). This only applies to managed installs.
            if (!IsElevated() && ScheduledTaskExists("MasselGUARD"))
            {
                try
                {
                    Process.Start(new ProcessStartInfo("schtasks.exe",
                        "/run /tn MasselGUARD /i")
                    {
                        CreateNoWindow  = true,
                        UseShellExecute = false,
                    });
                    return 0; // exit this non-elevated instance
                }
                catch { /* fall through to normal launch */ }
            }
            // Resolve exe dir via MainModule.FileName — same approach as working
            // WireGuardClient. Environment.ProcessPath is unreliable in some
            // self-contained publish configurations.
            string exeDir;
            try
            {
                exeDir = Path.GetDirectoryName(
                    Process.GetCurrentProcess().MainModule?.FileName
                    ?? AppContext.BaseDirectory)
                    ?? AppContext.BaseDirectory;
            }
            catch { exeDir = AppContext.BaseDirectory; }

            // CRITICAL: set CWD and DLL search path BEFORE everything else.
            // When the SCM launches this process as a service child
            //   (MasselGUARD.exe /service "...")
            // CWD is System32. tunnel.dll calls LoadLibrary("wireguard.dll")
            // using CWD + DLL search order, so both must point at the exe dir.
            try { Directory.SetCurrentDirectory(exeDir); } catch { }
            SetDllDirectory(exeDir);

            // /service dispatch — must happen before any WPF initialisation.
            int svcResult = TunnelDll.HandleServiceArgs(args, exeDir);
            if (svcResult >= 0)
                return svcResult;

            // CLI mode — runs without WPF when any non-/service arg is present.
            // The GUI (if running) coexists safely: both talk to the same WireGuard
            // kernel driver; the GUI's 1-second poll timer picks up any state change.
            if (Cli.CliRunner.IsCliInvocation(args))
                return Cli.CliRunner.Run(args);

            // Normal GUI launch — detach from (and hide) the console that the
            // console-subsystem exe always gets, so no black window ever appears.
            HideConsoleForGuiLaunch();
            var app = new App();
            app.InitializeComponent();
            app.Run();
            return 0;
        }

        /// <summary>
        /// Suppresses the console window for GUI and service launches.
        ///
        /// OutputType=Exe means Windows always allocates a console.
        /// • Launched from a terminal  → multiple processes share the console →
        ///   just detach; the terminal window stays open.
        /// • Double-click / Start Menu → Windows created a console only for us →
        ///   hide it first (prevents the flash), then free it.
        /// </summary>
        private static void HideConsoleForGuiLaunch()
        {
            try
            {
                var list  = new uint[2];
                uint count = GetConsoleProcessList(list, (uint)list.Length);
                if (count <= 1)                       // console created for us alone
                {
                    var hwnd = GetConsoleWindow();
                    if (hwnd != IntPtr.Zero)
                        ShowWindow(hwnd, 0);           // SW_HIDE — instant, no flash
                }
                FreeConsole();
            }
            catch { /* non-critical */ }
        }
        private static bool IsElevated()
        {
            try
            {
                var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                var p  = new System.Security.Principal.WindowsPrincipal(id);
                return p.IsInRole(
                    System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private static bool ScheduledTaskExists(string taskName)
        {
            try
            {
                using var proc = Process.Start(new ProcessStartInfo(
                    "schtasks.exe", $"/query /tn \"{taskName}\"")
                {
                    CreateNoWindow         = true,
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                });
                proc?.WaitForExit(3000);
                return proc?.ExitCode == 0;
            }
            catch { return false; }
        }
    }
}
