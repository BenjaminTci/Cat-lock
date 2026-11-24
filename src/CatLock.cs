// CatLock - empeche le chat de faire n'importe quoi sur le clavier et la souris.
// Un raccourci (Alt+Shift+W par defaut) verrouille / deverrouille clavier et souris. L'application vit dans la zone
// de notification et se lance au demarrage de Windows.
//
// Compile avec le csc.exe du .NET Framework 4 (C# 5) : voir build.ps1.

using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("CatLock")]
[assembly: System.Reflection.AssemblyProduct("CatLock")]
[assembly: System.Reflection.AssemblyDescription("Verrouille clavier et souris avec un raccourci")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

namespace CatLock
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static int Main(string[] args)
        {
            // Mode utilitaire pour le script de build : exporte l'icone de l'exe.
            if (args.Length == 2 && args[0] == "--export-icon")
            {
                Art.ExportIco(args[1]);
                return 0;
            }

            bool fromStartup = Array.IndexOf(args, "--startup") >= 0;

            try { SetProcessDPIAware(); } catch { }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, "Local\\CatLock_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    if (!fromStartup)
                    {
                        MessageBox.Show(
                            "CatLock est déjà lancé.\n\nIl se trouve dans les icônes cachées de la barre des tâches.\n" +
                            Shortcut.Load() + " verrouille / déverrouille le clavier et la souris.",
                            "CatLock", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp(fromStartup));
                GC.KeepAlive(mutex);
            }
            return 0;
        }
    }

    // ------------------------------------------------------------------------------
    // Raccourci configurable : une touche + des modificateurs (Ctrl, Alt, Shift, Win).
    // Enregistre dans HKCU\Software\CatLock.
    // ------------------------------------------------------------------------------
    sealed class Shortcut
    {
        public const int Ctrl = 1, Alt = 2, Shift = 4, Win = 8;
        const string KeyPath = @"Software\CatLock";

        public static readonly Shortcut Default = new Shortcut(0x57, Alt | Shift); // Alt+Shift+W

        public readonly int Vk;
        public readonly int Mods;

        public Shortcut(int vk, int mods)
        {
            Vk = vk;
            Mods = mods & (Ctrl | Alt | Shift | Win);
        }

        [DllImport("user32.dll")]
        static extern uint MapVirtualKey(uint uCode, uint uMapType);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetKeyNameText(int lParam, StringBuilder lpString, int cchSize);

        public static Shortcut Load()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyPath))
                {
                    if (key != null)
                    {
                        object vk = key.GetValue("ShortcutVk");
                        object mods = key.GetValue("ShortcutMods");
                        if (vk is int && mods is int)
                        {
                            Shortcut s = new Shortcut((int)vk, (int)mods);
                            if (s.ValidationError() == null) return s;
                        }
                    }
                }
            }
            catch { }
            return Default;
        }

        public void Save()
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath))
            {
                key.SetValue("ShortcutVk", Vk, RegistryValueKind.DWord);
                key.SetValue("ShortcutMods", Mods, RegistryValueKind.DWord);
            }
        }

        // null si le raccourci est utilisable, sinon la raison (affichee a l'utilisateur).
        public string ValidationError()
        {
            if (Vk <= 0 || Vk > 0xFE || IsModifierKey(Vk))
                return "Ajoutez une touche en plus de Ctrl, Alt, Shift ou Win.";
            if (Mods == 0)
                return "Utilisez au moins Ctrl, Alt, Shift ou Win, pour que le chat ne puisse pas le déclencher en marchant sur une seule touche.";
            if (Vk == 0x2E && (Mods & (Ctrl | Alt)) == (Ctrl | Alt))
                return "Ctrl+Alt+Suppr est réservé à Windows.";
            if (Vk == 0x4C && (Mods & Win) != 0)
                return "Win+L est réservé à Windows (verrouillage de la session).";
            return null;
        }

        public static bool IsModifierKey(int vk)
        {
            switch (vk)
            {
                case 0x10: case 0x11: case 0x12:                       // Shift, Ctrl, Alt
                case 0xA0: case 0xA1: case 0xA2: case 0xA3: case 0xA4: case 0xA5:
                case 0x5B: case 0x5C:                                  // Win gauche / droite
                    return true;
                default:
                    return false;
            }
        }

        public bool SameAs(Shortcut other)
        {
            return other != null && other.Vk == Vk && other.Mods == Mods;
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            if ((Mods & Ctrl) != 0) sb.Append("Ctrl+");
            if ((Mods & Alt) != 0) sb.Append("Alt+");
            if ((Mods & Shift) != 0) sb.Append("Shift+");
            if ((Mods & Win) != 0) sb.Append("Win+");
            sb.Append(KeyName(Vk));
            return sb.ToString();
        }

        static string KeyName(int vk)
        {
            if ((vk >= 0x41 && vk <= 0x5A) || (vk >= 0x30 && vk <= 0x39)) return ((char)vk).ToString();
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);

            // Nom localise donne par Windows (tient compte de la disposition AZERTY).
            uint scan = MapVirtualKey((uint)vk, 0);
            if (scan != 0)
            {
                bool extended = (vk >= 0x21 && vk <= 0x28) || vk == 0x2D || vk == 0x2E || vk == 0x6F || vk == 0x90;
                int lParam = (int)(scan << 16) | (extended ? 1 << 24 : 0);
                StringBuilder name = new StringBuilder(64);
                if (GetKeyNameText(lParam, name, name.Capacity) > 0)
                {
                    string s = name.ToString();
                    return s.Length > 1 ? s.Substring(0, 1).ToUpper() + s.Substring(1).ToLower() : s.ToUpper();
                }
            }
            return ((Keys)vk).ToString();
        }
    }

    // ------------------------------------------------------------------------------
    // Hooks bas niveau clavier + souris.
    // Quand c'est verrouille, tout est avale sauf le raccourci (qui deverrouille).
    // ------------------------------------------------------------------------------
    sealed class InputBlocker : IDisposable
    {
        const int WH_KEYBOARD_LL = 13;
        const int WH_MOUSE_LL = 14;

        const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
        const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;
        const int WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;
        const int WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208;
        const int WM_XBUTTONDOWN = 0x020B, WM_XBUTTONUP = 0x020C;

        const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
        const int VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3;
        const int VK_LMENU = 0xA4, VK_RMENU = 0xA5, VK_LWIN = 0x5B, VK_RWIN = 0x5C;

        delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")]
        static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vKey);

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extraInfo; }
        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public InputUnion u; }
        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] inputs, int cbSize);

        const int LLKHF_INJECTED = 0x10;
        // Touche virtuelle non attribuee, utilisee comme "masque" (meme technique qu'AutoHotkey).
        const int VK_MASK = 0xE8;

        static readonly IntPtr Swallow = new IntPtr(1);

        // Garder des references sur les delegues pour que le GC ne les ramasse pas.
        readonly LowLevelProc keyboardProc;
        readonly LowLevelProc mouseProc;
        IntPtr keyboardHook = IntPtr.Zero;
        IntPtr mouseHook = IntPtr.Zero;

        readonly SynchronizationContext ui;

        // Etat des touches tel que vu par le hook (independant de ce que le systeme recoit).
        readonly bool[] keyDown = new bool[256];
        // Touches dont l'appui a ete transmis au systeme : on laissera passer leur relachement
        // meme une fois verrouille, pour ne pas laisser de touche "coincee" (ex : Ctrl).
        readonly bool[] keyPassed = new bool[256];
        // Touches dont l'appui a ete avale pendant le verrouillage : leur relachement est
        // avale aussi, meme apres deverrouillage (ex : relacher Alt ne doit rien declencher).
        readonly bool[] keySwallowed = new bool[256];
        readonly bool[] buttonPassed = new bool[5];
        // Touche du raccourci actuellement enfoncee (pour avaler sa repetition et son relachement).
        int heldVk = -1;
        bool locked;

        volatile int shortcutVk = Shortcut.Default.Vk;
        volatile int shortcutMods = Shortcut.Default.Mods;
        volatile bool capturing;

        public event EventHandler LockedChanged;
        // Mode capture (fenetre "Changer le raccourci") : combinaison pressee (touche, modificateurs).
        public event Action<int, int> ShortcutCaptured;

        public void SetShortcut(Shortcut s)
        {
            shortcutVk = s.Vk;
            shortcutMods = s.Mods;
        }

        // Pendant la capture, la prochaine combinaison avec modificateur est interceptee et
        // signalee au lieu de (de)verrouiller. Les touches seules passent (Entree, Echap...).
        public bool Capturing
        {
            get { return capturing; }
            set { capturing = value; }
        }

        public InputBlocker(SynchronizationContext ui)
        {
            this.ui = ui;
            keyboardProc = KeyboardProc;
            mouseProc = MouseProc;
            Install();
        }

        public bool Locked
        {
            get { return locked; }
            set
            {
                if (locked == value) return;
                locked = value;
                RaiseLockedChanged();
            }
        }

        // (Re)installe les hooks. Windows peut retirer silencieusement un hook bas niveau
        // s'il a mis trop de temps a repondre ; le reinstaller regulierement garantit que
        // le raccourci continue de fonctionner.
        public void Install()
        {
            IntPtr hMod = GetModuleHandle(null);
            IntPtr newKeyboard = SetWindowsHookEx(WH_KEYBOARD_LL, keyboardProc, hMod, 0);
            if (newKeyboard == IntPtr.Zero)
            {
                if (keyboardHook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                return;
            }
            IntPtr newMouse = SetWindowsHookEx(WH_MOUSE_LL, mouseProc, hMod, 0);
            if (newMouse == IntPtr.Zero)
            {
                UnhookWindowsHookEx(newKeyboard);
                if (mouseHook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                return;
            }
            if (keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(keyboardHook);
            if (mouseHook != IntPtr.Zero) UnhookWindowsHookEx(mouseHook);
            keyboardHook = newKeyboard;
            mouseHook = newMouse;
        }

        // Apres un verrouillage de session / mise en veille, des relachements de touches ont
        // pu nous echapper : on repart d'un etat propre.
        public void ResetKeyState()
        {
            Array.Clear(keyDown, 0, keyDown.Length);
            Array.Clear(keyPassed, 0, keyPassed.Length);
            Array.Clear(keySwallowed, 0, keySwallowed.Length);
            Array.Clear(buttonPassed, 0, buttonPassed.Length);
            heldVk = -1;
        }

        void RaiseLockedChanged()
        {
            EventHandler handler = LockedChanged;
            if (handler != null) ui.Post(delegate { handler(this, EventArgs.Empty); }, null);
        }

        static bool AsyncDown(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }

        // Modificateurs enfonces en ce moment (combinaison de Shortcut.Ctrl/Alt/Shift/Win).
        // Correspondance exacte : avec un raccourci Alt+Shift+W, Ctrl+Alt+Shift+W ou
        // AltGr+Shift+W (AltGr = Ctrl + Alt) ne declenchent rien.
        int CurrentMods()
        {
            int mods = 0;
            if (locked)
            {
                // Le systeme ne voit plus rien : on se fie a notre propre suivi.
                if (keyDown[VK_LCONTROL] || keyDown[VK_RCONTROL]) mods |= Shortcut.Ctrl;
                if (keyDown[VK_LMENU] || keyDown[VK_RMENU]) mods |= Shortcut.Alt;
                if (keyDown[VK_LSHIFT] || keyDown[VK_RSHIFT]) mods |= Shortcut.Shift;
                if (keyDown[VK_LWIN] || keyDown[VK_RWIN]) mods |= Shortcut.Win;
            }
            else
            {
                // Etat reel du systeme.
                if (AsyncDown(VK_CONTROL)) mods |= Shortcut.Ctrl;
                if (AsyncDown(VK_MENU)) mods |= Shortcut.Alt;
                if (AsyncDown(VK_SHIFT)) mods |= Shortcut.Shift;
                if (AsyncDown(VK_LWIN) || AsyncDown(VK_RWIN)) mods |= Shortcut.Win;
            }
            return mods;
        }

        // Si Windows a vu un modificateur enfonce, le relacher juste apres sans autre touche
        // peut declencher quelque chose : Alt ouvre la barre de menus, Win ouvre le menu
        // Demarrer, Alt+Shift change la langue du clavier. On envoie une touche "neutre".
        void SendMaskKeyIfModifierPassed()
        {
            if (!keyPassed[VK_LMENU] && !keyPassed[VK_RMENU] && !keyPassed[VK_LSHIFT] && !keyPassed[VK_RSHIFT] &&
                !keyPassed[VK_LCONTROL] && !keyPassed[VK_RCONTROL] && !keyPassed[VK_LWIN] && !keyPassed[VK_RWIN]) return;
            INPUT[] inputs = new INPUT[2];
            inputs[0].type = 1; inputs[0].u.ki.wVk = VK_MASK;
            inputs[1].type = 1; inputs[1].u.ki.wVk = VK_MASK; inputs[1].u.ki.dwFlags = 2; // KEYEVENTF_KEYUP
            SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0) return CallNextHookEx(keyboardHook, nCode, wParam, lParam);

            int msg = wParam.ToInt32();
            int vk = Marshal.ReadInt32(lParam) & 0xFF;
            bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            bool isUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

            // Notre propre touche masque : toujours transmise.
            if (vk == VK_MASK && (Marshal.ReadInt32(lParam, 8) & LLKHF_INJECTED) != 0)
                return CallNextHookEx(keyboardHook, nCode, wParam, lParam);

            // Repetition automatique et relachement de la touche du raccourci : avales.
            if (vk == heldVk)
            {
                if (isUp) heldVk = -1;
                if (isDown || isUp) return Swallow;
            }

            // Raccourci : toujours ecoute, jamais transmis aux autres applications.
            if (isDown && !Shortcut.IsModifierKey(vk))
            {
                int mods = CurrentMods();
                if (capturing && !locked)
                {
                    if (mods != 0)
                    {
                        heldVk = vk;
                        SendMaskKeyIfModifierPassed();
                        Action<int, int> captured = ShortcutCaptured;
                        if (captured != null) ui.Post(delegate { captured(vk, mods); }, null);
                        return Swallow;
                    }
                }
                else if (vk == shortcutVk && mods == shortcutMods)
                {
                    heldVk = vk;
                    locked = !locked;
                    SendMaskKeyIfModifierPassed();
                    RaiseLockedChanged();
                    return Swallow;
                }
            }

            if (isDown) keyDown[vk] = true;
            else if (isUp) keyDown[vk] = false;

            if (!locked)
            {
                if (isUp && keySwallowed[vk])
                {
                    keySwallowed[vk] = false;
                    return Swallow;
                }
                if (isDown) { keyPassed[vk] = true; keySwallowed[vk] = false; }
                else if (isUp) keyPassed[vk] = false;
                return CallNextHookEx(keyboardHook, nCode, wParam, lParam);
            }

            if (isUp && keyPassed[vk])
            {
                keyPassed[vk] = false;
                return CallNextHookEx(keyboardHook, nCode, wParam, lParam);
            }
            if (isDown && !keyPassed[vk]) keySwallowed[vk] = true;
            else if (isUp) keySwallowed[vk] = false;
            return Swallow;
        }

        static int ButtonIndex(int msg, IntPtr lParam)
        {
            switch (msg)
            {
                case WM_LBUTTONDOWN: case WM_LBUTTONUP: return 0;
                case WM_RBUTTONDOWN: case WM_RBUTTONUP: return 1;
                case WM_MBUTTONDOWN: case WM_MBUTTONUP: return 2;
                case WM_XBUTTONDOWN: case WM_XBUTTONUP:
                    // MSLLHOOKSTRUCT : POINT (8 octets) puis mouseData ; mot haut = bouton X.
                    int which = (Marshal.ReadInt32(lParam, 8) >> 16) & 0xFFFF;
                    return which == 2 ? 4 : 3;
                default: return -1;
            }
        }

        IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0) return CallNextHookEx(mouseHook, nCode, wParam, lParam);

            int msg = wParam.ToInt32();
            bool isDown = msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN || msg == WM_XBUTTONDOWN;
            bool isUp = msg == WM_LBUTTONUP || msg == WM_RBUTTONUP || msg == WM_MBUTTONUP || msg == WM_XBUTTONUP;
            int button = (isDown || isUp) ? ButtonIndex(msg, lParam) : -1;

            if (!locked)
            {
                if (button >= 0) buttonPassed[button] = isDown;
                return CallNextHookEx(mouseHook, nCode, wParam, lParam);
            }

            if (isUp && button >= 0 && buttonPassed[button])
            {
                buttonPassed[button] = false;
                return CallNextHookEx(mouseHook, nCode, wParam, lParam);
            }
            // Mouvements, clics, molette : tout est bloque.
            return Swallow;
        }

        public void Dispose()
        {
            if (keyboardHook != IntPtr.Zero) { UnhookWindowsHookEx(keyboardHook); keyboardHook = IntPtr.Zero; }
            if (mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
        }
    }

    // ------------------------------------------------------------------------------
    // Application de la zone de notification.
    // ------------------------------------------------------------------------------
    sealed class TrayApp : ApplicationContext
    {
        const string SettingsKeyPath = @"Software\CatLock";

        bool autostartEnabled;
        Shortcut shortcut;
        ShortcutForm shortcutForm;
        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu;
        readonly ToolStripMenuItem lockItem;
        readonly ToolStripMenuItem startupItem;
        readonly Icon iconUnlocked;
        readonly Icon iconLocked;
        readonly InputBlocker blocker;
        readonly OverlayForm overlay;
        readonly System.Windows.Forms.Timer rehookTimer;
        readonly TaskbarWatcher taskbarWatcher;

        public TrayApp(bool fromStartup)
        {
            menu = new ContextMenuStrip();
            if (SynchronizationContext.Current == null)
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

            int iconSize = Math.Max(16, SystemInformation.SmallIconSize.Width);
            iconUnlocked = Art.MakeIcon(iconSize, false);
            iconLocked = Art.MakeIcon(iconSize, true);

            shortcut = Shortcut.Load();

            lockItem = new ToolStripMenuItem("Verrouiller clavier et souris", null, delegate { blocker.Locked = true; });
            lockItem.Font = new Font(lockItem.Font, FontStyle.Bold);
            startupItem = new ToolStripMenuItem("Lancer au démarrage de Windows", null, delegate { ToggleAutostart(); });
            menu.Items.Add(lockItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Changer le raccourci...", null, delegate { OpenShortcutDialog(); }));
            menu.Items.Add(startupItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Quitter CatLock", null, delegate { ExitThread(); }));
            menu.Opening += delegate { startupItem.Checked = autostartEnabled; };

            tray = new NotifyIcon();
            tray.ContextMenuStrip = menu;
            tray.Icon = iconUnlocked;
            tray.Visible = true;

            overlay = new OverlayForm();

            blocker = new InputBlocker(SynchronizationContext.Current);
            blocker.SetShortcut(shortcut);
            blocker.LockedChanged += delegate { UpdateState(); };
            UpdateTexts();

            rehookTimer = new System.Windows.Forms.Timer();
            rehookTimer.Interval = 30000;
            rehookTimer.Tick += delegate { blocker.Install(); };
            rehookTimer.Start();

            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;

            // Si l'Explorateur redemarre (ou n'etait pas pret au demarrage), on remet l'icone.
            taskbarWatcher = new TaskbarWatcher(delegate
            {
                tray.Visible = false;
                tray.Visible = true;
            });

            bool firstRun = FirstRunSetup();
            try
            {
                // Premier lancement, migration de l'ancienne cle "Run", ou mise a jour du chemin
                // de l'exe s'il a ete deplace : on (re)cree la tache planifiee.
                autostartEnabled = Autostart.IsEnabled();
                if (firstRun || Autostart.LegacyRunKeyPresent() || autostartEnabled)
                {
                    Autostart.Enable();
                    autostartEnabled = true;
                }
            }
            catch (Exception ex)
            {
                tray.ShowBalloonTip(8000, "CatLock : démarrage automatique impossible", ex.Message, ToolTipIcon.Warning);
                return;
            }

            if (!fromStartup || firstRun)
            {
                tray.ShowBalloonTip(5000, "CatLock est actif",
                    shortcut + " verrouille le clavier et la souris.\nRefaites " + shortcut + " pour les réactiver.",
                    ToolTipIcon.Info);
            }
        }

        void UpdateState()
        {
            bool locked = blocker.Locked;
            tray.Icon = locked ? iconLocked : iconUnlocked;
            lockItem.Enabled = !locked;
            UpdateTexts();
            overlay.ShowState(locked, shortcut.ToString());
        }

        void UpdateTexts()
        {
            string text = blocker.Locked
                ? "CatLock - VERROUILLÉ (" + shortcut + ")"
                : "CatLock - " + shortcut + " pour verrouiller";
            tray.Text = text.Length > 63 ? text.Substring(0, 63) : text; // limite Windows
            lockItem.ShortcutKeyDisplayString = shortcut.ToString();
        }

        // ---- Changement du raccourci ----

        void OpenShortcutDialog()
        {
            if (shortcutForm != null && !shortcutForm.IsDisposed)
            {
                shortcutForm.Activate();
                return;
            }
            ShortcutForm form = new ShortcutForm(shortcut, blocker, iconUnlocked);
            form.FormClosed += delegate
            {
                blocker.Capturing = false;
                shortcutForm = null;
                if (form.Saved && form.Result != null) ApplyShortcut(form.Result);
            };
            shortcutForm = form;
            form.Show();
            form.Activate();
        }

        void ApplyShortcut(Shortcut s)
        {
            try { s.Save(); }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible d'enregistrer le raccourci :\n" + ex.Message,
                    "CatLock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            shortcut = s;
            blocker.SetShortcut(s);
            UpdateTexts();
            tray.ShowBalloonTip(4000, "Raccourci modifié",
                s + " verrouille et déverrouille maintenant le clavier et la souris.", ToolTipIcon.Info);
        }

        void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            blocker.ResetKeyState();
        }

        void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                blocker.ResetKeyState();
                blocker.Install();
            }
        }

        // ---- Demarrage automatique ----

        static bool FirstRunSetup()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath))
                {
                    if (key.GetValue("Initialized") != null) return false;
                    key.SetValue("Initialized", 1, RegistryValueKind.DWord);
                }
                return true;
            }
            catch { return false; }
        }

        void ToggleAutostart()
        {
            try
            {
                if (autostartEnabled) Autostart.Disable(); else Autostart.Enable();
                autostartEnabled = Autostart.IsEnabled();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible de modifier le démarrage automatique :\n" + ex.Message,
                    "CatLock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        protected override void ExitThreadCore()
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            rehookTimer.Stop();
            if (shortcutForm != null && !shortcutForm.IsDisposed) shortcutForm.Close();
            blocker.Dispose();
            taskbarWatcher.DestroyHandle();
            overlay.Close();
            tray.Visible = false;
            tray.Dispose();
            menu.Dispose();
            base.ExitThreadCore();
        }
    }

    // ------------------------------------------------------------------------------
    // Demarrage automatique via une tache planifiee "a l'ouverture de session".
    // (La cle Run a ete ignoree par l'Explorateur sur ce PC ; la tache planifiee est plus
    // fiable, ne necessite pas les droits admin et demarre sans le delai de l'Explorateur.)
    // ------------------------------------------------------------------------------
    static class Autostart
    {
        const string TaskName = "CatLock";
        const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool IsEnabled()
        {
            string output;
            return RunSchtasks("/Query /TN \"" + TaskName + "\"", out output) == 0;
        }

        public static void Enable()
        {
            string user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
            string exe = SecurityElement.Escape(Application.ExecutablePath);
            string dir = SecurityElement.Escape(Path.GetDirectoryName(Application.ExecutablePath));
            string xml =
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                "  <RegistrationInfo><Description>Lance CatLock (verrouillage du clavier et de la souris).</Description></RegistrationInfo>\r\n" +
                "  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + user + "</UserId></LogonTrigger></Triggers>\r\n" +
                "  <Principals><Principal id=\"Author\"><UserId>" + user + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>\r\n" +
                "  <Settings>\r\n" +
                "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
                "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
                "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
                "    <AllowHardTerminate>true</AllowHardTerminate>\r\n" +
                "    <AllowStartOnDemand>true</AllowStartOnDemand>\r\n" +
                "    <Enabled>true</Enabled>\r\n" +
                "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
                // Priorite normale : par defaut une tache planifiee tourne en priorite basse,
                // ce qui pourrait ralentir le traitement du clavier et de la souris.
                "    <Priority>4</Priority>\r\n" +
                "  </Settings>\r\n" +
                "  <Actions Context=\"Author\"><Exec><Command>" + exe + "</Command><Arguments>--startup</Arguments><WorkingDirectory>" + dir + "</WorkingDirectory></Exec></Actions>\r\n" +
                "</Task>\r\n";

            string tmp = Path.Combine(Path.GetTempPath(), "CatLock_task.xml");
            File.WriteAllText(tmp, xml, Encoding.Unicode);
            try
            {
                string output;
                if (RunSchtasks("/Create /TN \"" + TaskName + "\" /XML \"" + tmp + "\" /F", out output) != 0)
                    throw new InvalidOperationException(output.Trim());
            }
            finally { try { File.Delete(tmp); } catch { } }

            RemoveLegacyRunKey();
        }

        public static void Disable()
        {
            string output;
            RunSchtasks("/Delete /TN \"" + TaskName + "\" /F", out output);
            RemoveLegacyRunKey();
        }

        public static bool LegacyRunKeyPresent()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                return key != null && key.GetValue("CatLock") != null;
        }

        static void RemoveLegacyRunKey()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                if (key != null) key.DeleteValue("CatLock", false);
        }

        static int RunSchtasks(string arguments, out string output)
        {
            ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), arguments);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            Encoding oem = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            psi.StandardOutputEncoding = oem;
            psi.StandardErrorEncoding = oem;
            using (Process p = Process.Start(psi))
            {
                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(15000);
                output = stderr.Length > 0 ? stderr : stdout;
                return p.HasExited ? p.ExitCode : -1;
            }
        }
    }

    // ------------------------------------------------------------------------------
    // Fenetre "Changer le raccourci" : l'utilisateur appuie sur la nouvelle combinaison.
    // La capture passe par le hook clavier, ce qui permet aussi les combinaisons avec Win.
    // ------------------------------------------------------------------------------
    sealed class ShortcutForm : Form
    {
        readonly InputBlocker blocker;
        readonly Shortcut current;
        readonly Label comboLabel;
        readonly Label errorLabel;
        readonly Button saveButton;
        readonly Action<int, int> onCaptured;

        public Shortcut Result { get; private set; }
        public bool Saved { get; private set; }

        public ShortcutForm(Shortcut current, InputBlocker blocker, Icon icon)
        {
            this.current = current;
            this.blocker = blocker;

            float scale;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
            int width = (int)(400 * scale);

            Text = "CatLock - Changer le raccourci";
            Icon = icon;
            Font = new Font("Segoe UI", 9.75f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding((int)(14 * scale));

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.ColumnCount = 1;
            layout.AutoSize = true;
            layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            layout.Location = new Point(Padding.Left, Padding.Top);

            Label currentLabel = new Label();
            currentLabel.AutoSize = true;
            currentLabel.Text = "Raccourci actuel : " + current;
            currentLabel.Font = new Font(Font, FontStyle.Bold);
            currentLabel.Margin = new Padding(0, 0, 0, (int)(8 * scale));

            Label help = new Label();
            help.AutoSize = true;
            help.MaximumSize = new Size(width, 0);
            help.Text = "Appuyez sur la nouvelle combinaison : au moins une touche Ctrl, Alt, Shift ou Win, plus une autre touche.";
            help.Margin = new Padding(0, 0, 0, (int)(8 * scale));

            comboLabel = new Label();
            comboLabel.AutoSize = false;
            comboLabel.Size = new Size(width, (int)(56 * scale));
            comboLabel.BorderStyle = BorderStyle.FixedSingle;
            comboLabel.BackColor = SystemColors.Window;
            comboLabel.TextAlign = ContentAlignment.MiddleCenter;
            comboLabel.Font = new Font("Segoe UI Semibold", 16f);
            comboLabel.ForeColor = SystemColors.GrayText;
            comboLabel.Text = "En attente...";
            comboLabel.Margin = new Padding(0);

            errorLabel = new Label();
            errorLabel.AutoSize = true;
            errorLabel.MaximumSize = new Size(width, 0);
            errorLabel.ForeColor = Color.FromArgb(196, 43, 28);
            errorLabel.Margin = new Padding(0, (int)(6 * scale), 0, (int)(6 * scale));

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.AutoSize = true;
            buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttons.Anchor = AnchorStyles.Right;
            buttons.Margin = new Padding(0, (int)(6 * scale), 0, 0);

            saveButton = MakeButton("Enregistrer", scale);
            saveButton.Enabled = false;
            saveButton.Click += delegate { Saved = true; Close(); };
            Button cancelButton = MakeButton("Annuler", scale);
            cancelButton.Click += delegate { Close(); };
            Button defaultButton = MakeButton("Par défaut (" + Shortcut.Default + ")", scale);
            defaultButton.Click += delegate { SetCandidate(Shortcut.Default); };
            buttons.Controls.Add(saveButton);
            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(defaultButton);

            layout.Controls.Add(currentLabel);
            layout.Controls.Add(help);
            layout.Controls.Add(comboLabel);
            layout.Controls.Add(errorLabel);
            layout.Controls.Add(buttons);
            Controls.Add(layout);

            AcceptButton = saveButton;
            CancelButton = cancelButton;

            // Capture uniquement quand cette fenetre est au premier plan.
            Activated += delegate { blocker.Capturing = true; };
            Deactivate += delegate { blocker.Capturing = false; };
            onCaptured = delegate(int vk, int mods) { if (!IsDisposed) SetCandidate(new Shortcut(vk, mods)); };
            blocker.ShortcutCaptured += onCaptured;
        }

        static Button MakeButton(string text, float scale)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.MinimumSize = new Size((int)(88 * scale), (int)(30 * scale));
            b.Padding = new Padding((int)(6 * scale), 0, (int)(6 * scale), 0);
            b.Margin = new Padding((int)(6 * scale), 0, 0, 0);
            return b;
        }

        void SetCandidate(Shortcut s)
        {
            comboLabel.Text = s.ToString();
            string error = s.ValidationError();
            if (error == null && s.SameAs(current)) error = "C'est déjà le raccourci actuel.";
            errorLabel.Text = error ?? "";
            comboLabel.ForeColor = error == null ? SystemColors.ControlText : Color.FromArgb(196, 43, 28);
            Result = error == null ? s : null;
            saveButton.Enabled = Result != null;
            if (saveButton.Enabled) saveButton.Focus();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            blocker.Capturing = false;
            blocker.ShortcutCaptured -= onCaptured;
            base.OnFormClosed(e);
        }
    }

    // Fenetre invisible qui recoit le message "TaskbarCreated".
    sealed class TaskbarWatcher : NativeWindow
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int RegisterWindowMessage(string lpString);

        readonly int taskbarCreatedMsg;
        readonly Action onTaskbarCreated;

        public TaskbarWatcher(Action onTaskbarCreated)
        {
            this.onTaskbarCreated = onTaskbarCreated;
            taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (taskbarCreatedMsg != 0 && m.Msg == taskbarCreatedMsg) onTaskbarCreated();
            base.WndProc(ref m);
        }
    }

    // ------------------------------------------------------------------------------
    // Petit bandeau en bas a droite de l'ecran (ne prend jamais le focus, laisse passer les clics).
    // ------------------------------------------------------------------------------
    sealed class OverlayForm : Form
    {
        const int WS_EX_TOPMOST = 0x00000008, WS_EX_TRANSPARENT = 0x00000020;
        const int WS_EX_TOOLWINDOW = 0x00000080, WS_EX_NOACTIVATE = 0x08000000;
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        readonly float scale;
        readonly Font titleFont;
        readonly Font subtitleFont;
        readonly System.Windows.Forms.Timer hideTimer;
        bool locked;
        string title = "";
        string subtitle = "";

        public OverlayForm()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(30, 30, 34);
            Opacity = 0.94;
            Size = new Size(S(330), S(68));
            titleFont = new Font("Segoe UI Semibold", 15f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            subtitleFont = new Font("Segoe UI", 12.5f * scale, FontStyle.Regular, GraphicsUnit.Pixel);

            hideTimer = new System.Windows.Forms.Timer();
            hideTimer.Interval = 1800;
            hideTimer.Tick += delegate { hideTimer.Stop(); Hide(); };
        }

        int S(float v) { return (int)Math.Round(v * scale); }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int round = 2; // DWMWCP_ROUND (Windows 11)
                DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
            }
            catch { }
        }

        public void ShowState(bool isLocked, string shortcutText)
        {
            locked = isLocked;
            title = locked ? "Clavier et souris verrouillés" : "Clavier et souris réactivés";
            subtitle = shortcutText + (locked ? " pour déverrouiller" : " pour verrouiller à nouveau");
            hideTimer.Stop();

            // Le bandeau s'elargit si le raccourci a un nom long.
            int textWidth = Math.Max(TextRenderer.MeasureText(title, titleFont).Width,
                                     TextRenderer.MeasureText(subtitle, subtitleFont).Width);
            Width = Math.Max(S(330), S(70) + textWidth + S(20));

            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - S(16), area.Bottom - Height - S(16));
            Invalidate();

            if (!Visible) Show();
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

            if (!locked) hideTimer.Start(); // le message "reactive" disparait tout seul
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Color accent = locked ? Art.LockedColor : Art.UnlockedColor;
            using (SolidBrush b = new SolidBrush(accent))
                g.FillRectangle(b, 0, 0, S(5), Height);

            int iconSize = S(40);
            using (Bitmap icon = Art.Draw(iconSize, locked))
                g.DrawImage(icon, S(18), (Height - iconSize) / 2);

            float x = S(70);
            using (SolidBrush white = new SolidBrush(Color.White))
            using (SolidBrush grey = new SolidBrush(Color.FromArgb(190, 190, 198)))
            {
                g.DrawString(title, titleFont, white, x, S(13));
                g.DrawString(subtitle, subtitleFont, grey, x, S(37));
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                hideTimer.Dispose();
                titleFont.Dispose();
                subtitleFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // ------------------------------------------------------------------------------
    // Dessin de l'icone : une tete de chat (orange = libre, rouge + cadenas = verrouille).
    // ------------------------------------------------------------------------------
    static class Art
    {
        public static readonly Color UnlockedColor = Color.FromArgb(255, 152, 0);
        public static readonly Color LockedColor = Color.FromArgb(229, 57, 53);

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr hIcon);

        public static Bitmap Draw(int size, bool locked)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                g.ScaleTransform(size / 32f, size / 32f);

                Color fur = locked ? LockedColor : UnlockedColor;
                Color dark = Color.FromArgb(40, 30, 30);

                using (SolidBrush furBrush = new SolidBrush(fur))
                using (GraphicsPath head = new GraphicsPath())
                {
                    head.AddPolygon(new[] { new PointF(3.5f, 15f), new PointF(5.5f, 1.5f), new PointF(14.5f, 8f) });
                    head.AddPolygon(new[] { new PointF(17.5f, 8f), new PointF(26.5f, 1.5f), new PointF(28.5f, 15f) });
                    head.AddEllipse(2.5f, 6f, 27f, 23f);
                    head.FillMode = FillMode.Winding;
                    g.FillPath(furBrush, head);
                }

                using (SolidBrush darkBrush = new SolidBrush(dark))
                using (Pen darkPen = new Pen(dark, 2f))
                {
                    darkPen.StartCap = LineCap.Round;
                    darkPen.EndCap = LineCap.Round;
                    if (locked)
                    {
                        // Yeux fermes (le chat est sage...)
                        g.DrawArc(darkPen, 8f, 13f, 6f, 5f, 20, 140);
                        g.DrawArc(darkPen, 18f, 13f, 6f, 5f, 20, 140);
                    }
                    else
                    {
                        g.FillEllipse(darkBrush, 9f, 13.5f, 4.5f, 5.5f);
                        g.FillEllipse(darkBrush, 18.5f, 13.5f, 4.5f, 5.5f);
                    }
                    g.FillPolygon(darkBrush, new[] { new PointF(14.3f, 21f), new PointF(17.7f, 21f), new PointF(16f, 23f) });
                }

                if (locked)
                {
                    // Cadenas en bas a droite
                    using (Pen outline = new Pen(Color.White, 4f))
                    using (Pen shackle = new Pen(Color.FromArgb(40, 30, 30), 1.8f))
                    using (SolidBrush white = new SolidBrush(Color.White))
                    using (SolidBrush body = new SolidBrush(Color.FromArgb(40, 30, 30)))
                    {
                        g.DrawArc(outline, 21f, 18f, 6.5f, 7f, 180, 180);
                        g.FillRectangle(white, 18f, 21.5f, 12.5f, 10f);
                        g.DrawArc(shackle, 21f, 18f, 6.5f, 7f, 180, 180);
                        g.DrawLine(shackle, 21f, 21.5f, 21f, 23f);
                        g.DrawLine(shackle, 27.5f, 21.5f, 27.5f, 23f);
                        g.FillRectangle(body, 19.3f, 22.8f, 9.9f, 7.7f);
                    }
                }
            }
            return bmp;
        }

        public static Icon MakeIcon(int size, bool locked)
        {
            using (Bitmap bmp = Draw(size, locked))
            {
                IntPtr h = bmp.GetHicon();
                try
                {
                    using (Icon tmp = Icon.FromHandle(h))
                        return (Icon)tmp.Clone();
                }
                finally { DestroyIcon(h); }
            }
        }

        // Ecrit un .ico multi-tailles (images PNG, format accepte depuis Windows Vista).
        public static void ExportIco(string path)
        {
            int[] sizes = { 16, 24, 32, 48, 64, 256 };
            byte[][] images = new byte[sizes.Length][];
            for (int i = 0; i < sizes.Length; i++)
            {
                using (Bitmap bmp = Draw(sizes[i], false))
                using (MemoryStream ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    images[i] = ms.ToArray();
                }
            }

            using (BinaryWriter w = new BinaryWriter(File.Create(path)))
            {
                w.Write((short)0);
                w.Write((short)1);
                w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0);
                    w.Write((byte)0);
                    w.Write((short)1);
                    w.Write((short)32);
                    w.Write(images[i].Length);
                    w.Write(offset);
                    offset += images[i].Length;
                }
                for (int i = 0; i < sizes.Length; i++) w.Write(images[i]);
            }
        }
    }
}
