using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WindowTileTool
{
    internal static class Program
    {
        private const string AppGuid = "Global\\WindowTileHotkeys_2026_Antigravity";

        [STAThread]
        private static void Main()
        {
            // Verhindert mehrfaches Starten im Hintergrund
            bool isNewInstance;
            using (var mutex = new Mutex(false, AppGuid, out isNewInstance))
            {
                if (!isNewInstance)
                {
                    MessageBox.Show(
                        "Window Tile läuft bereits im Hintergrund.\n\nDas Icon befindet sich im Infobereich der Windows-Taskleiste (neben der Uhr).",
                        "Bereits aktiv",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApplicationContext());
            }
        }
    }

    /// <summary>
    /// Repräsentiert das Ergebnis der Registrierung einer Tastenkombination.
    /// </summary>
    internal class HotKeyStatus
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public bool IsRegistered { get; set; }
        public int ErrorCode { get; set; }
    }

    /// <summary>
    /// Steuert das Tray-Icon, das Kontextmenü und die Lebensdauer der Hintergrundanwendung.
    /// </summary>
    internal class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly HotKeyWindow _hotKeyWindow;
        private readonly Icon _customIcon;
        private readonly ToolStripMenuItem _autostartMenuItem;

        public TrayApplicationContext()
        {
            _hotKeyWindow = new HotKeyWindow();
            _customIcon = IconFactory.CreateTileGridIcon();

            var contextMenu = new ContextMenuStrip();

            var titleItem = new ToolStripMenuItem("Window Tile (Win + Alt + Strg)")
            {
                Enabled = false,
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold)
            };
            contextMenu.Items.Add(titleItem);

            contextMenu.Items.Add("Tastenbelegung / Status prüfen...", null, (s, e) => ShowStatusDialog());
            contextMenu.Items.Add(new ToolStripSeparator());

            contextMenu.Items.Add("2 Fenster anordnen (Win+Alt+Strg+2)", null, (s, e) => WindowTiler.TileWindows(2));
            contextMenu.Items.Add("3 Fenster anordnen (Win+Alt+Strg+3)", null, (s, e) => WindowTiler.TileWindows(3));
            contextMenu.Items.Add("4 Fenster anordnen (Win+Alt+Strg+4)", null, (s, e) => WindowTiler.TileWindows(4));

            contextMenu.Items.Add(new ToolStripSeparator());

            _autostartMenuItem = new ToolStripMenuItem("Mit Windows starten")
            {
                CheckOnClick = true,
                Checked = AutostartManager.IsEnabled()
            };
            _autostartMenuItem.Click += (s, e) => ToggleAutostart();
            contextMenu.Items.Add(_autostartMenuItem);

            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("Beenden", null, (s, e) => ExitThread());

            _notifyIcon = new NotifyIcon
            {
                Icon = _customIcon,
                ContextMenuStrip = contextMenu,
                Text = "Window Tile (Win + Alt + Strg + 2/3/4)",
                Visible = true
            };

            // Status beim Start prüfen und Benachrichtigung kompakt anzeigen
            ShowStartupNotification();
        }

        private void ToggleAutostart()
        {
            bool newState = _autostartMenuItem.Checked;
            bool success = AutostartManager.SetEnabled(newState);

            if (success)
            {
                string msg = newState
                    ? "Autostart ist jetzt aktiviert. Window Tile startet künftig automatisch mit Windows."
                    : "Autostart wurde deaktiviert.";
                _notifyIcon.ShowBalloonTip(2000, "Window Tile - Autostart", msg, ToolTipIcon.Info);
            }
            else
            {
                _autostartMenuItem.Checked = !newState;
                MessageBox.Show(
                    "Die Autostart-Einstellung konnte nicht in der Windows-Registry aktualisiert werden.",
                    "Fehler",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void ShowStartupNotification()
        {
            var statuses = _hotKeyWindow.GetStatuses();
            bool allOk = statuses.TrueForAll(s => s.IsRegistered);

            if (allOk)
            {
                // Kompakt gehalten (3 Zeilen), damit Windows 11 keine Zeile abschneidet
                _notifyIcon.ShowBalloonTip(
                    3000,
                    "Window Tile aktiv",
                    "✔ Win + Alt + Strg + 2 (2 Spalten)\n" +
                    "✔ Win + Alt + Strg + 3 (3 Spalten)\n" +
                    "✔ Win + Alt + Strg + 4 (4 Spalten)",
                    ToolTipIcon.Info);
            }
            else
            {
                var sb = new StringBuilder();
                foreach (var s in statuses)
                {
                    if (s.IsRegistered)
                    {
                        sb.AppendLine("✔ " + s.Name);
                    }
                    else
                    {
                        string errText = (s.ErrorCode == 1409) ? "Bereits belegt!" : "Fehler " + s.ErrorCode;
                        sb.AppendLine("✖ " + s.Name + " (" + errText + ")");
                    }
                }

                _notifyIcon.ShowBalloonTip(
                    5000,
                    "Achtung: Shortcut-Konflikt",
                    sb.ToString().TrimEnd(),
                    ToolTipIcon.Warning);
            }
        }

        private void ShowStatusDialog()
        {
            var statuses = _hotKeyWindow.GetStatuses();
            var sb = new StringBuilder();
            sb.AppendLine("Aktueller Status der Tastenkombinationen:\n");

            foreach (var s in statuses)
            {
                if (s.IsRegistered)
                {
                    sb.AppendLine("  ✔  " + s.Name + "  ->  AKTIV");
                }
                else
                {
                    string reason = (s.ErrorCode == 1409)
                        ? "BLOCKIERT (von anderem Programm belegt)"
                        : "FEHLER (" + s.ErrorCode + ")";
                    sb.AppendLine("  ✖  " + s.Name + "  ->  " + reason);
                }
            }

            sb.AppendLine("\nAutostart mit Windows: " + (AutostartManager.IsEnabled() ? "Aktiviert" : "Deaktiviert"));
            sb.AppendLine("\nTipp: Rechtsklick auf das Taskleisten-Icon erlaubt manuelles Ausführen, Autostart-Umschalten und Beenden.");

            MessageBox.Show(
                sb.ToString(),
                "Window Tile - Status & Belegung",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        protected override void ExitThreadCore()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _customIcon.Dispose();
            _hotKeyWindow.Dispose();
            base.ExitThreadCore();
        }
    }

    /// <summary>
    /// Verwaltet den Autostart über den benutzereigenen Windows-Registry-Schlüssel (HKCU Run).
    /// </summary>
    internal static class AutostartManager
    {
        private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "WindowTileHotkeys";

        public static bool IsEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false))
                {
                    if (key == null) return false;
                    var val = key.GetValue(AppName) as string;
                    return !string.IsNullOrEmpty(val);
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool SetEnabled(bool enable)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true))
                {
                    if (key == null) return false;
                    if (enable)
                    {
                        key.SetValue(AppName, "\"" + Application.ExecutablePath + "\"");
                    }
                    else
                    {
                        key.DeleteValue(AppName, false);
                    }
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Erzeugt ein modernes, mehrfarbiges 3-Spalten-Raster-Icon für die Taskleiste.
    /// </summary>
    internal static class IconFactory
    {
        public static Icon CreateTileGridIcon()
        {
            const int iconSize = 32;
            using (var bmp = new Bitmap(iconSize, iconSize))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.Transparent);

                    // 3 Spalten mit lebendigen, modernen Akzentfarben:
                    // 1. Spalte: Lebendiges Windows-Blau
                    // 2. Spalte: Warmes Koralle / Orange
                    // 3. Spalte: Frisches Smaragd-Grün
                    var colHeaderColors = new Color[]
                    {
                        Color.FromArgb(0, 140, 255),    // Blau
                        Color.FromArgb(249, 115, 22),   // Koralle/Orange
                        Color.FromArgb(16, 185, 129)    // Grün
                    };

                    var colBodyColors = new Color[]
                    {
                        Color.FromArgb(220, 240, 255),  // Helles Blau
                        Color.FromArgb(255, 238, 225),  // Helles Orange
                        Color.FromArgb(220, 248, 235)   // Helles Grün
                    };

                    int[] xPositions = new int[] { 2, 12, 22 };
                    const int colWidth = 8;
                    const int colHeight = 24;
                    const int titleHeight = 5;
                    const int startY = 4;

                    var borderColor = Color.FromArgb(35, 45, 60);

                    using (var borderPen = new Pen(borderColor, 1.2f))
                    using (var highlightPen = new Pen(Color.FromArgb(180, 255, 255, 255), 0.7f))
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            int x = xPositions[i];
                            using (var headerBrush = new SolidBrush(colHeaderColors[i]))
                            using (var bodyBrush = new SolidBrush(colBodyColors[i]))
                            {
                                // Fensterkörper
                                g.FillRectangle(bodyBrush, x, startY + titleHeight, colWidth, colHeight - titleHeight);
                                // Titelleiste
                                g.FillRectangle(headerBrush, x, startY, colWidth, titleHeight);
                                // Dunkler Kontrastrahmen
                                g.DrawRectangle(borderPen, x, startY, colWidth, colHeight);
                                // Subtile helle Innenkante für modernen Look
                                g.DrawRectangle(highlightPen, x + 1, startY + 1, colWidth - 2, colHeight - 2);
                            }
                        }
                    }
                }

                IntPtr hIcon = bmp.GetHicon();
                Icon icon = Icon.FromHandle(hIcon);
                return icon;
            }
        }
    }

    /// <summary>
    /// Unsichtbares Nachrichtenfenster zum Registrieren und Abfangen globaler Windows-Hotkeys.
    /// </summary>
    internal class HotKeyWindow : NativeWindow, IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const int MOD_ALT = 0x0001;
        private const int MOD_CONTROL = 0x0002;
        private const int MOD_WIN = 0x0008;
        private const int MOD_NOREPEAT = 0x4000;

        private readonly List<HotKeyStatus> _statuses = new List<HotKeyStatus>();
        private readonly List<int> _activeIds = new List<int>();

        public HotKeyWindow()
        {
            CreateHandle(new CreateParams());
            RegisterAllHotKeys();
        }

        private void RegisterAllHotKeys()
        {
            int modifiers = MOD_WIN | MOD_ALT | MOD_CONTROL | MOD_NOREPEAT;

            // Haupt-Zifferntasten
            RegisterKey(2, "Win + Alt + Strg + 2", modifiers, (uint)Keys.D2);
            RegisterKey(3, "Win + Alt + Strg + 3", modifiers, (uint)Keys.D3);
            RegisterKey(4, "Win + Alt + Strg + 4", modifiers, (uint)Keys.D4);

            // Numpad Zifferntasten
            RegisterKey(102, "Win + Alt + Strg + Numpad 2", modifiers, (uint)Keys.NumPad2, isSecondary: true);
            RegisterKey(103, "Win + Alt + Strg + Numpad 3", modifiers, (uint)Keys.NumPad3, isSecondary: true);
            RegisterKey(104, "Win + Alt + Strg + Numpad 4", modifiers, (uint)Keys.NumPad4, isSecondary: true);
        }

        private void RegisterKey(int id, string name, int modifiers, uint vk, bool isSecondary = false)
        {
            bool ok = NativeMethods.RegisterHotKey(Handle, id, (uint)modifiers, vk);
            int err = ok ? 0 : Marshal.GetLastWin32Error();

            if (ok)
            {
                _activeIds.Add(id);
            }

            // Nur die Haupttasten im Status-Dialog aufführen, um die Liste kompakt zu halten
            if (!isSecondary)
            {
                _statuses.Add(new HotKeyStatus
                {
                    Id = id,
                    Name = name,
                    IsRegistered = ok,
                    ErrorCode = err
                });
            }
        }

        public List<HotKeyStatus> GetStatuses()
        {
            return new List<HotKeyStatus>(_statuses);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                switch (id)
                {
                    case 2:
                    case 102:
                        WindowTiler.TileWindows(2);
                        break;
                    case 3:
                    case 103:
                        WindowTiler.TileWindows(3);
                        break;
                    case 4:
                    case 104:
                        WindowTiler.TileWindows(4);
                        break;
                }
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            foreach (int id in _activeIds)
            {
                NativeMethods.UnregisterHotKey(Handle, id);
            }
            _activeIds.Clear();
            DestroyHandle();
        }
    }

    /// <summary>
    /// Logik zum Suchen, Berechnen und Positionieren der Fenster.
    /// </summary>
    internal static class WindowTiler
    {
        public static void TileWindows(int columnCount)
        {
            if (columnCount < 2) columnCount = 2;

            // 1. Zielfläche (Monitor des aktiven Fensters oder Primärmonitor)
            IntPtr fgHandle = NativeMethods.GetForegroundWindow();
            Screen targetScreen = (fgHandle != IntPtr.Zero) ? Screen.FromHandle(fgHandle) : Screen.PrimaryScreen;
            if (targetScreen == null || targetScreen.Bounds.Width == 0)
            {
                targetScreen = Screen.PrimaryScreen;
            }

            Rectangle workingArea = targetScreen.WorkingArea;

            // 2. Fenster ermitteln
            var candidateWindows = GetCandidateWindows("Antigravity", columnCount);
            if (candidateWindows.Count < 2)
            {
                return;
            }

            int actualColumns = Math.Min(columnCount, candidateWindows.Count);
            int baseColumnWidth = workingArea.Width / actualColumns;

            // 3. Nebeneinander positionieren
            for (int i = 0; i < actualColumns; i++)
            {
                IntPtr hWnd = candidateWindows[i].Handle;

                // Falls minimiert oder maximiert, auf Normalgröße wiederherstellen
                if (NativeMethods.IsIconic(hWnd) || NativeMethods.IsZoomed(hWnd))
                {
                    NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);
                }

                int colX = workingArea.X + (i * baseColumnWidth);
                int colY = workingArea.Y;
                int colHeight = workingArea.Height;
                int colWidth = (i == actualColumns - 1)
                    ? workingArea.Width - (baseColumnWidth * (actualColumns - 1))
                    : baseColumnWidth;

                SnapWindowWithDwmAdjustment(hWnd, colX, colY, colWidth, colHeight);
            }
        }

        private static List<WindowItem> GetCandidateWindows(string appFilter, int requiredCount)
        {
            var allWindows = new List<WindowItem>();
            uint currentPid = (uint)Process.GetCurrentProcess().Id;

            NativeMethods.EnumWindows((hWnd, lParam) =>
            {
                if (!NativeMethods.IsWindowVisible(hWnd) && !NativeMethods.IsIconic(hWnd)) return true;
                if (IsCloaked(hWnd)) return true;

                long style = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_STYLE).ToInt64();
                long exStyle = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE).ToInt64();

                if ((style & NativeMethods.WS_CHILD) != 0) return true;
                if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0) return true;

                int length = NativeMethods.GetWindowTextLength(hWnd);
                if (length == 0) return true;

                var sb = new StringBuilder(length + 1);
                NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
                string title = sb.ToString();

                uint pid = 0;
                NativeMethods.GetWindowThreadProcessId(hWnd, out pid);
                if (pid == currentPid) return true;

                string procName = string.Empty;
                try
                {
                    using (var proc = Process.GetProcessById((int)pid))
                    {
                        procName = proc.ProcessName;
                    }
                }
                catch { }

                if (procName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                {
                    if (title == "Program Manager" || title == "Taskbar" || string.IsNullOrEmpty(title))
                    {
                        return true;
                    }
                }

                allWindows.Add(new WindowItem
                {
                    Handle = hWnd,
                    Title = title,
                    ProcessName = procName,
                    ProcessId = pid
                });

                return true;
            }, IntPtr.Zero);

            // Bevorzuge Fenster, die den AppFilter (z. B. "Antigravity") enthalten
            var matched = allWindows.FindAll(w =>
                w.ProcessName.IndexOf(appFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                w.Title.IndexOf(appFilter, StringComparison.OrdinalIgnoreCase) >= 0);

            if (matched.Count >= requiredCount)
            {
                return matched.GetRange(0, requiredCount);
            }

            // Fallback auf oberste aktive Anwendungsfenster
            int countToTake = Math.Min(requiredCount, allWindows.Count);
            return allWindows.GetRange(0, countToTake);
        }

        private static bool IsCloaked(IntPtr hWnd)
        {
            int cloaked = 0;
            int hr = NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED, out cloaked, sizeof(int));
            return (hr == 0 && cloaked != 0);
        }

        private static void SnapWindowWithDwmAdjustment(IntPtr hWnd, int targetX, int targetY, int targetWidth, int targetHeight)
        {
            NativeMethods.RECT winRect;
            NativeMethods.RECT frameRect;
            NativeMethods.GetWindowRect(hWnd, out winRect);
            int hr = NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out frameRect, Marshal.SizeOf(typeof(NativeMethods.RECT)));

            int adjustedX = targetX;
            int adjustedY = targetY;
            int adjustedWidth = targetWidth;
            int adjustedHeight = targetHeight;

            if (hr == 0)
            {
                int borderLeft = frameRect.Left - winRect.Left;
                int borderTop = frameRect.Top - winRect.Top;
                int borderRight = winRect.Right - frameRect.Right;
                int borderBottom = winRect.Bottom - frameRect.Bottom;

                adjustedX = targetX - borderLeft;
                adjustedY = targetY - borderTop;
                adjustedWidth = targetWidth + borderLeft + borderRight;
                adjustedHeight = targetHeight + borderTop + borderBottom;
            }

            NativeMethods.SetWindowPos(
                hWnd,
                IntPtr.Zero,
                adjustedX,
                adjustedY,
                adjustedWidth,
                adjustedHeight,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_SHOWWINDOW);
        }

        private class WindowItem
        {
            public IntPtr Handle;
            public string Title;
            public string ProcessName;
            public uint ProcessId;
        }
    }

    internal static class NativeMethods
    {
        public const int SW_RESTORE = 9;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const int DWMWA_CLOAKED = 14;
        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const long WS_CHILD = 0x40000000L;
        public const long WS_EX_TOOLWINDOW = 0x00000080L;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            return (IntPtr.Size == 8) ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);
        }

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool DestroyIcon(IntPtr handle);
    }
}
