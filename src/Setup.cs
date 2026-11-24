// Installeur de CatLock (CatLockSetup.exe).
// CatLock.exe est embarque comme ressource. Installation par utilisateur, sans droits admin,
// dans %LOCALAPPDATA%\Programs\CatLock. Mode silencieux : CatLockSetup.exe /S
//
// Compile par build.ps1 avec InstallInfo.cs.

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Installation de CatLock")]
[assembly: AssemblyProduct("CatLock")]
[assembly: AssemblyDescription("Installation de CatLock")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace CatLock
{
    static class SetupProgram
    {
        [STAThread]
        static int Main(string[] args)
        {
            bool silent = Array.Exists(args, delegate(string a) { return a == "/S" || a == "/s" || a == "--silent"; });
            if (silent)
            {
                try { Installer.Install(true, false); return 0; }
                catch { return 1; }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
            return 0;
        }
    }

    static class Installer
    {
        public static bool IsInstalled { get { return File.Exists(InstallInfo.ExePath); } }

        public static void Install(bool autostart, bool desktopShortcut)
        {
            string dir = InstallInfo.InstallDir;
            string exe = InstallInfo.ExePath;

            // CatLock en cours (ancienne version ou version de developpement) : on l'arrete.
            foreach (Process p in Process.GetProcessesByName("CatLock"))
            {
                try { p.Kill(); p.WaitForExit(5000); } catch { }
            }

            Directory.CreateDirectory(dir);
            CopyWithRetry(exe);

            CreateShortcut(InstallInfo.StartMenuShortcut, exe);
            if (desktopShortcut) CreateShortcut(InstallInfo.DesktopShortcut, exe);
            else if (File.Exists(InstallInfo.DesktopShortcut)) File.Delete(InstallInfo.DesktopShortcut);

            RegisterUninstall(exe);

            // CatLock gere lui-meme sa tache planifiee de demarrage.
            ProcessStartInfo psi = new ProcessStartInfo(exe, "--autostart " + (autostart ? "on" : "off"));
            psi.UseShellExecute = false;
            using (Process p = Process.Start(psi))
            {
                p.WaitForExit(30000);
                if (p.ExitCode != 0)
                    throw new InvalidOperationException("Impossible de configurer le lancement au démarrage.");
            }

            Process.Start(exe);
        }

        static void CopyWithRetry(string target)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("CatLock.exe"))
                    using (FileStream file = File.Create(target))
                        source.CopyTo(file);
                    return;
                }
                catch (IOException)
                {
                    // Le processus qu'on vient d'arreter peut garder le fichier quelques instants.
                    if (attempt >= 10) throw;
                    System.Threading.Thread.Sleep(300);
                }
            }
        }

        static void CreateShortcut(string path, string target)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            try
            {
                object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                Type linkType = link.GetType();
                linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { target });
                linkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { Path.GetDirectoryName(target) });
                linkType.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { "Verrouille le clavier et la souris (anti-chat)" });
                linkType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { target + ",0" });
                linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                Marshal.FinalReleaseComObject(link);
            }
            finally { Marshal.FinalReleaseComObject(shell); }
        }

        static void RegisterUninstall(string exe)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(InstallInfo.UninstallKey))
            {
                key.SetValue("DisplayName", "CatLock");
                key.SetValue("DisplayVersion", InstallInfo.Version);
                key.SetValue("Publisher", InstallInfo.Publisher);
                key.SetValue("URLInfoAbout", InstallInfo.Website);
                key.SetValue("DisplayIcon", exe + ",0");
                key.SetValue("InstallLocation", InstallInfo.InstallDir);
                key.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
                key.SetValue("QuietUninstallString", "\"" + exe + "\" --uninstall --silent");
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                key.SetValue("EstimatedSize", (int)Math.Max(1, new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }
    }

    // ------------------------------------------------------------------------------
    // Fenetre d'installation.
    // ------------------------------------------------------------------------------
    sealed class SetupForm : Form
    {
        readonly float scale;
        readonly Panel body;
        readonly CheckBox autostartBox;
        readonly CheckBox desktopBox;
        readonly Button installButton;
        readonly Button cancelButton;
        bool finished;

        public SetupForm()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
            int width = S(440);
            bool update = Installer.IsInstalled;

            Text = "Installation de CatLock";
            Font = new Font("Segoe UI", 9.75f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = SystemColors.Window;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.ColumnCount = 1;
            layout.AutoSize = true;
            layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            layout.Padding = new Padding(S(20), S(18), S(20), S(16));

            // En-tete : grande icone + titre
            TableLayoutPanel header = new TableLayoutPanel();
            header.ColumnCount = 2;
            header.AutoSize = true;
            header.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            header.Margin = new Padding(0, 0, 0, S(14));
            PictureBox picture = new PictureBox();
            picture.Size = new Size(S(56), S(56));
            picture.SizeMode = PictureBoxSizeMode.Zoom;
            picture.Image = LoadLogo(S(56));
            picture.Margin = new Padding(0, 0, S(14), 0);
            Label title = new Label();
            title.AutoSize = true;
            title.Font = new Font("Segoe UI Semibold", 18f);
            title.Text = "CatLock";
            title.Margin = new Padding(0, S(2), 0, 0);
            Label tagline = new Label();
            tagline.AutoSize = true;
            tagline.MaximumSize = new Size(width - S(70), 0);
            tagline.ForeColor = SystemColors.GrayText;
            tagline.Text = "Verrouille le clavier et la souris pour que le chat ne fasse pas de bêtises.";
            FlowLayoutPanel titles = new FlowLayoutPanel();
            titles.FlowDirection = FlowDirection.TopDown;
            titles.AutoSize = true;
            titles.Margin = new Padding(0);
            titles.Controls.Add(title);
            titles.Controls.Add(tagline);
            header.Controls.Add(picture, 0, 0);
            header.Controls.Add(titles, 1, 0);

            body = new Panel();
            body.AutoSize = true;
            body.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            body.Margin = new Padding(0);

            FlowLayoutPanel options = new FlowLayoutPanel();
            options.FlowDirection = FlowDirection.TopDown;
            options.AutoSize = true;
            options.Margin = new Padding(0);

            Label info = new Label();
            info.AutoSize = true;
            info.MaximumSize = new Size(width, 0);
            info.Text = "Raccourci par défaut : Alt+Shift+W (modifiable ensuite par clic droit sur l'icône du chat, dans les icônes cachées de la barre des tâches).";
            info.Margin = new Padding(0, 0, 0, S(10));

            Label folder = new Label();
            folder.AutoSize = true;
            folder.MaximumSize = new Size(width, 0);
            folder.ForeColor = SystemColors.GrayText;
            folder.Text = "Dossier : " + InstallInfo.InstallDir;
            folder.Margin = new Padding(0, 0, 0, S(10));

            autostartBox = new CheckBox();
            autostartBox.AutoSize = true;
            autostartBox.Checked = true;
            autostartBox.Text = "Lancer CatLock au démarrage de Windows";
            desktopBox = new CheckBox();
            desktopBox.AutoSize = true;
            desktopBox.Checked = File.Exists(InstallInfo.DesktopShortcut);
            desktopBox.Text = "Créer un raccourci sur le Bureau";

            options.Controls.Add(info);
            options.Controls.Add(folder);
            options.Controls.Add(autostartBox);
            options.Controls.Add(desktopBox);
            body.Controls.Add(options);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.AutoSize = true;
            buttons.Anchor = AnchorStyles.Right;
            buttons.Margin = new Padding(0, S(16), 0, 0);
            installButton = MakeButton(update ? "Mettre à jour" : "Installer");
            installButton.Click += delegate { if (finished) Close(); else DoInstall(); };
            cancelButton = MakeButton("Annuler");
            cancelButton.Click += delegate { Close(); };
            buttons.Controls.Add(installButton);
            buttons.Controls.Add(cancelButton);

            layout.Controls.Add(header);
            layout.Controls.Add(body);
            layout.Controls.Add(buttons);
            Controls.Add(layout);

            AcceptButton = installButton;
            CancelButton = cancelButton;
        }

        int S(float v) { return (int)Math.Round(v * scale); }

        Button MakeButton(string text)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.MinimumSize = new Size(S(96), S(30));
            b.Padding = new Padding(S(8), 0, S(8), 0);
            b.Margin = new Padding(S(8), 0, 0, 0);
            b.UseVisualStyleBackColor = true;
            return b;
        }

        // L'icone contient des images PNG (generees par build.ps1) : on prend la plus petite
        // qui fait au moins la taille voulue et on la decode directement.
        static Image LoadLogo(int size)
        {
            try
            {
                byte[] ico;
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("CatLock.ico"))
                using (MemoryStream ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    ico = ms.ToArray();
                }
                int count = BitConverter.ToInt16(ico, 4);
                int bestOffset = -1, bestLength = 0, bestSize = int.MaxValue, largestOffset = -1, largestLength = 0, largestSize = 0;
                for (int i = 0; i < count; i++)
                {
                    int entry = 6 + 16 * i;
                    int w = ico[entry] == 0 ? 256 : ico[entry];
                    int length = BitConverter.ToInt32(ico, entry + 8);
                    int offset = BitConverter.ToInt32(ico, entry + 12);
                    if (w >= size && w < bestSize) { bestSize = w; bestOffset = offset; bestLength = length; }
                    if (w > largestSize) { largestSize = w; largestOffset = offset; largestLength = length; }
                }
                if (bestOffset < 0) { bestOffset = largestOffset; bestLength = largestLength; }
                return Image.FromStream(new MemoryStream(ico, bestOffset, bestLength));
            }
            catch { return null; }
        }

        void DoInstall()
        {
            installButton.Enabled = false;
            cancelButton.Enabled = false;
            UseWaitCursor = true;
            Application.DoEvents();
            try
            {
                Installer.Install(autostartBox.Checked, desktopBox.Checked);
            }
            catch (Exception ex)
            {
                UseWaitCursor = false;
                installButton.Enabled = true;
                cancelButton.Enabled = true;
                MessageBox.Show(this, "L'installation a échoué :\n" + ex.Message, "CatLock",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            UseWaitCursor = false;
            ShowDone();
        }

        void ShowDone()
        {
            body.Controls.Clear();
            Label done = new Label();
            done.AutoSize = true;
            done.MaximumSize = new Size(S(440), 0);
            done.Text = "CatLock est installé et lancé.\n\n" +
                        "Il se trouve dans les icônes cachées de la barre des tâches (flèche ^ à côté de l'horloge). " +
                        "Clic droit sur le chat pour changer le raccourci ou quitter.\n\n" +
                        "Pour le désinstaller : Paramètres > Applications > CatLock.";
            body.Controls.Add(done);

            cancelButton.Visible = false;
            installButton.Text = "Terminer";
            installButton.Enabled = true;
            finished = true;
        }
    }
}
