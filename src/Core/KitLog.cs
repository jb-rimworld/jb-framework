// SPDX-License-Identifier: MIT
// Lifted from Hearth & Harbor Foundation/HLog.cs.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RimWorld;
using Verse;

namespace JBKit
{
    internal enum KitLogLevel { Debug = 0, Info = 1, Warn = 2, Error = 3 }

    /// <summary>
    /// Transparent logging. Writes to the game log (Warn/Error always, Info only in dev mode or with verbose logging)
    /// and to the mod's own file next to the game config: &lt;ShortId&gt;.log (rotated at 1 MB, previous kept as .old).
    /// Line format: 2026-11-24 21:04:11 | tick 123456 | WARN  | Healing  | message. Game log lines start with "[&lt;Name&gt;] ".
    /// </summary>
    internal static class KitLog
    {
        public const long RotateBytes = 1_000_000;
        static readonly object Lock = new object();
        static readonly HashSet<string> OnceKeys = new HashSet<string>();
        static string filePath;
        static bool fileBroken;
        /// <summary>Debug lines and Info lines in the game log. Follows the global setting kit.verboseLog.</summary>
        public static bool Verbose;
        static readonly LinkedList<string> Recent = new LinkedList<string>();
        /// <summary>Errors and warnings written through the kit since start.</summary>
        public static int ErrorCount, WarnCount;

        /// <summary>Game log prefix, e.g. "[Luciferium Overhaul] ".</summary>
        public static string Prefix => "[" + (Kit.Info?.Name ?? "JBKit") + "] ";

        /// <summary>Copy of the last (up to 300) lines, for the diagnosis page and bug reports.</summary>
        public static string[] RecentSnapshot()
        {
            lock (Lock) { var a = new string[Recent.Count]; Recent.CopyTo(a, 0); return a; }
        }

        /// <summary>Absolute path of the log file (never shown in reports; reports print only the file name).</summary>
        public static string FilePath
        {
            get
            {
                if (filePath == null)
                {
                    string name = SafeFileName((Kit.Info?.ShortId ?? "JBKit") + ".log");
                    try { filePath = Path.Combine(GenFilePaths.ConfigFolderPath, name); }
                    catch { filePath = Path.Combine(Path.GetTempPath(), name); }
                }
                return filePath;
            }
        }
        public static string FileName => Path.GetFileName(FilePath);

        public static void Debug(string channel, string msg) => Write(KitLogLevel.Debug, channel, msg, null);
        public static void Info(string channel, string msg) => Write(KitLogLevel.Info, channel, msg, null);
        public static void Warn(string channel, string msg) => Write(KitLogLevel.Warn, channel, msg, null);
        public static void Error(string channel, string msg, Exception e = null) => Write(KitLogLevel.Error, channel, msg, e);

        /// <summary>Logs only the first time this key is seen in this session (fallbacks, missing optional mods …).</summary>
        public static void Once(string key, KitLogLevel level, string channel, string msg)
        {
            lock (Lock) { if (!OnceKeys.Add(key)) return; }
            Write(level, channel, msg, null);
        }

        static void Write(KitLogLevel level, string channel, string msg, Exception e)
        {
            if (level == KitLogLevel.Debug && !Verbose) return;
            string tick;
            try { tick = Current.Game != null ? Find.TickManager?.TicksGame.ToString() ?? "-" : "menu"; } catch { tick = "?"; }
            string lv = level == KitLogLevel.Debug ? "DEBUG" : level == KitLogLevel.Info ? "INFO " : level == KitLogLevel.Warn ? "WARN " : "ERROR";
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | tick {tick} | {lv} | {(channel ?? "Core"),-8} | {msg}";
            if (e != null) line += " | " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace;
            lock (Lock)
            {
                if (level == KitLogLevel.Error) ErrorCount++; else if (level == KitLogLevel.Warn) WarnCount++;
                Recent.AddLast(line); while (Recent.Count > 300) Recent.RemoveFirst();
                AppendFile(line);
            }
            string game = Prefix + "[" + (channel ?? "Core") + "] " + msg + (e != null ? "\n" + e : "");
            try
            {
                if (level == KitLogLevel.Error) Log.Error(game);
                else if (level == KitLogLevel.Warn) Log.Warning(game);
                else if (Verbose || Prefs.DevMode) Log.Message(game);
            }
            catch { }
        }

        static void AppendFile(string line)
        {
            if (fileBroken) return;
            try
            {
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > RotateBytes)
                {
                    string old = FilePath + ".old";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(FilePath, old);
                }
                File.AppendAllText(FilePath, line + "\n", Encoding.UTF8);
            }
            catch { fileBroken = true; }
        }

        /// <summary>Session header so each start is easy to find in the file.</summary>
        public static void SessionStart()
        {
            string rw;
            try { rw = VersionControl.CurrentVersionStringWithRev; } catch { rw = "?"; }
            lock (Lock) AppendFile($"\n===== {Kit.Info?.Name ?? "JBKit"} {Kit.Info?.Version ?? "?"} · JB.Kit {KitInfo.KitVersion} ({KitInfo.KitCommit}) · RimWorld {rw} · {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====");
        }

        static string SafeFileName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }
}
