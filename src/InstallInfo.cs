// Emplacements partages entre CatLock.exe (desinstallation) et CatLockSetup.exe (installation).

using System;
using System.IO;

namespace CatLock
{
    static class InstallInfo
    {
        public const string Version = "1.0.0";
        public const string Publisher = "BenjaminTci";
        public const string Website = "https://github.com/BenjaminTci/Cat-lock";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CatLock";

        // Installation par utilisateur : pas besoin des droits administrateur.
        public static string InstallDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\CatLock"); }
        }

        public static string ExePath
        {
            get { return Path.Combine(InstallDir, "CatLock.exe"); }
        }

        public static string StartMenuShortcut
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "CatLock.lnk"); }
        }

        public static string DesktopShortcut
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "CatLock.lnk"); }
        }
    }
}
