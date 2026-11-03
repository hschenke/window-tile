using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
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
        private readonly SynchronizationContext _syncContext;
        private UpdateManager.UpdateInfo _pendingUpdate;

        public TrayApplicationContext()
        {
            _syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            _hotKeyWindow = new HotKeyWindow();
            _customIcon = IconFactory.CreateTileGridIcon();

            // Bereinige eventuelle Reste vorheriger Updates
            UpdateManager.CleanOldExecutable();

            var contextMenu = new ContextMenuStrip();

            var titleItem = new ToolStripMenuItem("Window Tile (Win + Alt + Strg)")
            {
                Enabled = false,
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold)
            };
            contextMenu.Items.Add(titleItem);

            contextMenu.Items.Add("Tastenbelegung / Status prüfen...", null, (s, e) => ShowStatusDialog());
            contextMenu.Items.Add("Nach Updates suchen...", null, (s, e) => CheckForUpdates(isSilent: false));
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
                Text = "Window Tile v" + UpdateManager.CurrentVersion + " (Win + Alt + Strg + 2/3/4)",
                Visible = true
            };

            _notifyIcon.BalloonTipClicked += (s, e) =>
            {
                if (_pendingUpdate != null && _pendingUpdate.Available)
                {
                    PromptAndApplyUpdate(_pendingUpdate);
                }
            };

            // Status beim Start prüfen und Benachrichtigung kompakt anzeigen
            ShowStartupNotification();

            // Automatische Update-Prüfung im Hintergrund nach 3 Sekunden starten
            ThreadPool.QueueUserWorkItem(state =>
            {
                Thread.Sleep(3000);
                CheckForUpdates(isSilent: true);
            });
        }

        private void CheckForUpdates(bool isSilent)
        {
            ThreadPool.QueueUserWorkItem(state =>
            {
                var update = UpdateManager.CheckForUpdate();

                _syncContext.Post(_ =>
                {
                    if (update.Available)
                    {
                        _pendingUpdate = update;
                        if (isSilent)
                        {
                            _notifyIcon.ShowBalloonTip(
                                5000,
                                "Update verfügbar!",
                                "Version " + update.LatestTag + " ist auf GitHub verfügbar.\nHier klicken zum Aktualisieren.",
                                ToolTipIcon.Info);
                        }
                        else
                        {
                            PromptAndApplyUpdate(update);
                        }
                    }
                    else if (!isSilent)
                    {
                        if (!string.IsNullOrEmpty(update.ErrorMessage))
                        {
                            MessageBox.Show(
                                "Fehler beim Prüfen auf Updates:\n" + update.ErrorMessage,
                                "Update-Prüfung fehlgeschlagen",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                        }
                        else
                        {
                            MessageBox.Show(
                                "Window Tile ist mit Version v" + UpdateManager.CurrentVersion + " bereits auf dem neuesten Stand.",
                                "Kein Update erforderlich",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
                        }
                    }
                }, null);
            });
        }

        private void PromptAndApplyUpdate(UpdateManager.UpdateInfo update)
        {
            string msg = string.Format(
                "Eine neuere Version von Window Tile ist auf GitHub verfügbar!\n\n" +
                "Installierte Version: v{0}\n" +
                "Verfügbare Version:   {1}\n\n" +
                "Möchten Sie das Update jetzt automatisch herunterladen und installieren?",
                UpdateManager.CurrentVersion,
                update.LatestTag);

            var choice = MessageBox.Show(msg, "Window Tile - Update verfügbar", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (choice == DialogResult.Yes)
            {
                _notifyIcon.ShowBalloonTip(2000, "Window Tile Update", "Update wird heruntergeladen und installiert...", ToolTipIcon.Info);

                ThreadPool.QueueUserWorkItem(state =>
                {
                    string error;
                    bool success = UpdateManager.ApplyUpdate(update.DownloadUrl, out error);

                    _syncContext.Post(_ =>
                    {
                        if (success)
                        {
                            // Neue Version wurde gestartet, diese Instanz beenden
                            ExitThread();
                        }
                        else
                        {
                            MessageBox.Show(
                                "Das automatische Update ist fehlgeschlagen:\n" + error,
                                "Update-Fehler",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
                        }
                    }, null);
                });
            }
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
            sb.AppendLine("Window Tile v" + UpdateManager.CurrentVersion);
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
            sb.AppendLine("\nTipp: Rechtsklick auf das Taskleisten-Icon erlaubt manuelles Ausführen, Update-Prüfung und Beenden.");

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
    /// Prüft und installiert Updates direkt von GitHub Releases (analog zum Konzept in backup-holli).
    /// </summary>
    internal static class UpdateManager
    {
        public const string CurrentVersion = "0.1.2";
        public const string GitHubRepo = "hschenke/window-tile";
        private const string ReleasesApiUrl = "https://api.github.com/repos/" + GitHubRepo + "/releases/latest";

        public class UpdateInfo
        {
            public bool Available { get; set; }
            public string LatestTag { get; set; }
            public string DownloadUrl { get; set; }
            public string ErrorMessage { get; set; }
        }

        public static void CleanOldExecutable()
        {
            try
            {
                string oldPath = Application.ExecutablePath + ".old";
                if (File.Exists(oldPath))
                {
                    File.Delete(oldPath);
                }
            }
            catch { }
        }

        public static UpdateInfo CheckForUpdate()
        {
            var info = new UpdateInfo();
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // Tls12
                using (var client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "WindowTileHotkeys/" + CurrentVersion);
                    string json = client.DownloadString(ReleasesApiUrl);

                    var tagMatch = Regex.Match(json, @"""tag_name""\s*:\s*""([^""]+)""");
                    var assetMatch = Regex.Match(json, @"""browser_download_url""\s*:\s*""([^""]+WindowTileHotkeys\.exe)""");

                    if (!tagMatch.Success)
                    {
                        info.ErrorMessage = "Versions-Tag auf GitHub nicht gefunden.";
                        return info;
                    }

                    string latestTag = tagMatch.Groups[1].Value.Trim();
                    info.LatestTag = latestTag;
                    info.DownloadUrl = assetMatch.Success ? assetMatch.Groups[1].Value : null;

                    Version localVer = ParseVersion(CurrentVersion);
                    Version remoteVer = ParseVersion(latestTag);

                    if (remoteVer > localVer)
                    {
                        info.Available = true;
                    }
                }
            }
            catch (Exception ex)
            {
                info.ErrorMessage = ex.Message;
            }

            return info;
        }

        public static bool ApplyUpdate(string downloadUrl, out string errorMessage)
        {
            errorMessage = null;
            if (string.IsNullOrEmpty(downloadUrl))
            {
                errorMessage = "Keine Download-URL für die ausführbare Datei gefunden.";
                return false;
            }

            string currentExe = Application.ExecutablePath;
            string appDir = Path.GetDirectoryName(currentExe);
            string tempNewExe = Path.Combine(appDir, "WindowTileHotkeys.exe.new");
            string backupOldExe = currentExe + ".old";

            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                using (var client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "WindowTileHotkeys/" + CurrentVersion);
                    client.DownloadFile(downloadUrl, tempNewExe);
                }

                if (!File.Exists(tempNewExe) || new FileInfo(tempNewExe).Length < 1000)
                {
                    errorMessage = "Heruntergeladene Datei ist unvollständig oder beschädigt.";
                    return false;
                }

                // Falls ein Git-Repository vorliegt, optional Git-Pull ausführen, um auch Quellcode aktuell zu halten
                TryGitPull(appDir);

                // Windows Atomic Replace:
                // Laufende Exe kann nicht überschrieben, aber umbenannt werden!
                if (File.Exists(backupOldExe))
                {
                    File.Delete(backupOldExe);
                }

                File.Move(currentExe, backupOldExe);
                File.Move(tempNewExe, currentExe);

                // Neue Version starten
                Process.Start(currentExe);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                // Rollback falls tempNewExe existiert aber nicht verschoben wurde
                try
                {
                    if (File.Exists(tempNewExe)) File.Delete(tempNewExe);
                    if (!File.Exists(currentExe) && File.Exists(backupOldExe))
                    {
                        File.Move(backupOldExe, currentExe);
                    }
                }
                catch { }
                return false;
            }
        }

        private static void TryGitPull(string appDir)
        {
            try
            {
                string gitDir = Path.Combine(appDir, ".git");
                if (Directory.Exists(gitDir))
                {
                    var psi = new ProcessStartInfo("git", "pull origin main")
                    {
                        WorkingDirectory = appDir,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using (var p = Process.Start(psi))
                    {
                        p.WaitForExit(5000);
                    }
                }
            }
            catch { }
        }

        private static Version ParseVersion(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return new Version(0, 0);
            string cleaned = tag.Trim().TrimStart('v', 'V');
            Version v;
            if (Version.TryParse(cleaned, out v))
            {
                return v;
            }
            string[] parts = cleaned.Split('.');
            if (parts.Length == 2)
            {
                if (Version.TryParse(cleaned + ".0", out v)) return v;
            }
            return new Version(0, 0);
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
    /// Verfolgt die Erstellungsreihenfolge von Fenstern synchron zur Windows-Taskleiste (FIFO).
    /// Dadurch stimmen die Positionen auf dem Monitor 1:1 mit den Taskleisten-Thumbnails überein.
    /// </summary>
    internal static class WindowOrderTracker
    {
        private static readonly object _sync = new object();
        private static readonly Dictionary<IntPtr, long> _sequenceMap = new Dictionary<IntPtr, long>();
        private static long _counter = 0;

        public static void RegisterWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;
            lock (_sync)
            {
                if (!_sequenceMap.ContainsKey(hWnd))
                {
                    _sequenceMap[hWnd] = ++_counter;
                }
            }
        }

        public static void UnregisterWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;
            lock (_sync)
            {
                _sequenceMap.Remove(hWnd);
            }
        }

        public static long GetSequence(IntPtr hWnd)
        {
            lock (_sync)
            {
                long seq;
                if (_sequenceMap.TryGetValue(hWnd, out seq))
                {
                    return seq;
                }
                return long.MaxValue;
            }
        }

        public static void EnsureRegistered(List<IntPtr> zOrderTopToBottom)
        {
            lock (_sync)
            {
                // In EnumWindows (Z-Order von oben nach unten) liegt das älteste Fenster GANZ UNTEN.
                // Das Windows-Taskleisten-FIFO ordnet das älteste Fenster ganz links ein.
                // Daher weisen wir von unten nach oben (ältester zu neuester) fortlaufende Sequenzen zu.
                for (int i = zOrderTopToBottom.Count - 1; i >= 0; i--)
                {
                    IntPtr h = zOrderTopToBottom[i];
                    if (!_sequenceMap.ContainsKey(h))
                    {
                        _sequenceMap[h] = ++_counter;
                    }
                }
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

        private const int HSHELL_WINDOWCREATED = 1;
        private const int HSHELL_WINDOWDESTROYED = 2;

        private readonly List<HotKeyStatus> _statuses = new List<HotKeyStatus>();
        private readonly List<int> _activeIds = new List<int>();
        private readonly uint _shellHookMsg;

        public HotKeyWindow()
        {
            CreateHandle(new CreateParams());

            _shellHookMsg = NativeMethods.RegisterWindowMessage("SHELLHOOK");
            if (_shellHookMsg != 0)
            {
                NativeMethods.RegisterShellHookWindow(Handle);
            }

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
            else if (_shellHookMsg != 0 && (uint)m.Msg == _shellHookMsg)
            {
                int shellEvent = m.WParam.ToInt32();
                IntPtr targetHwnd = m.LParam;
                if (shellEvent == HSHELL_WINDOWCREATED)
                {
                    WindowOrderTracker.RegisterWindow(targetHwnd);
                }
                else if (shellEvent == HSHELL_WINDOWDESTROYED)
                {
                    WindowOrderTracker.UnregisterWindow(targetHwnd);
                }
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (_shellHookMsg != 0)
            {
                NativeMethods.DeregisterShellHookWindow(Handle);
            }

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

            // Sicherstellen, dass alle Fenster erfasst sind (chronologische Zuordnung)
            var handleList = new List<IntPtr>();
            foreach (var win in candidateWindows)
            {
                handleList.Add(win.Handle);
            }
            WindowOrderTracker.EnsureRegistered(handleList);

            // Nach Erstellungsreihenfolge sortieren (Ältestes Fenster = Spalte 0 / ganz links).
            // Entspricht exakt der Taskleisten-Vorschau von links nach rechts.
            candidateWindows.Sort((a, b) => WindowOrderTracker.GetSequence(a.Handle).CompareTo(WindowOrderTracker.GetSequence(b.Handle)));

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

        public class WindowItem
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

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterShellHookWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DeregisterShellHookWindow(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern uint RegisterWindowMessage(string lpString);
    }
}
