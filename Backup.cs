using System;
using System.IO;
using System.Linq;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace ShopBot
{
    // Automatic save backup: once per day at game start (before a save is loaded) the save folder
    // (%USERPROFILE%\AppData\LocalLow\OPNeonGames\Card Shop Simulator) is copied to BepInEx\ShopBot-Backups\<date>.
    // The last N backups are kept. Logs and the Unity cache are left out. Runs on a background thread (no hitch).
    public partial class Plugin
    {
        private ConfigEntry<bool> _backupOn;
        private ConfigEntry<int> _backupKeep;
        internal string _backupStatus = "";

        private void BindBackupConfig()
        {
            _backupOn = Config.Bind("1. General", "Back up the saves daily", true,
                "At game start, once per day, the bot copies your save folder to BepInEx/ShopBot-Backups. To restore: close the game and copy the files of a backup back into the save folder.");
            _backupKeep = Config.Bind("1. General", "Number of save backups to keep", 5, "Older backups are deleted.");
        }

        internal static string BackupRoot => Path.Combine(Paths.BepInExRootPath, "ShopBot-Backups");

        private void StartSaveBackup()
        {
            if (!_backupOn.Value) { _backupStatus = T("off"); return; }
            string src = Application.persistentDataPath;
            int keep = Mathf.Clamp(_backupKeep.Value, 1, 30);
            var log = Logger;
            var t = new Thread(() =>
            {
                try
                {
                    if (!Directory.Exists(src)) { _backupStatus = "no save folder"; return; }
                    Directory.CreateDirectory(BackupRoot);
                    string name = DateTime.Now.ToString("yyyy-MM-dd");
                    string dst = Path.Combine(BackupRoot, name);
                    if (Directory.Exists(dst)) { _backupStatus = name; log.LogInfo($"[Backup] already backed up today ({dst})"); return; }
                    string tmp = dst + ".tmp";
                    if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                    long bytes = CopyDir(src, tmp);
                    Directory.Move(tmp, dst);
                    // keep the newest N
                    foreach (var old in Directory.GetDirectories(BackupRoot).Where(d => !d.EndsWith(".tmp")).OrderByDescending(d => d).Skip(keep))
                        try { Directory.Delete(old, true); } catch { }
                    _backupStatus = name;
                    log.LogInfo($"[Backup] saves backed up: {dst} ({bytes / 1048576.0:0.0} MB, keeping the last {keep})");
                }
                catch (Exception e) { _backupStatus = "failed"; log.LogWarning("[Backup] failed: " + e.Message); }
            }) { IsBackground = true, Name = "ShopBot backup" };
            t.Start();
        }

        private static long CopyDir(string src, string dst)
        {
            long bytes = 0;
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src))
            {
                string n = Path.GetFileName(f);
                if (n.StartsWith("Player", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Copy(f, Path.Combine(dst, n), true); bytes += new FileInfo(f).Length; } catch { }   // a locked file must not stop the rest
            }
            foreach (var d in Directory.GetDirectories(src))
            {
                string n = Path.GetFileName(d);
                if (n.Equals("Unity", StringComparison.OrdinalIgnoreCase)) continue;   // analytics/shader cache
                bytes += CopyDir(d, Path.Combine(dst, n));
            }
            return bytes;
        }
    }
}
