// SPDX-License-Identifier: MIT
// Lifted from Hearth & Harbor Foundation/HLog.cs; one log per mod.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RimWorld;
using Verse;

namespace JBFramework
{
    /// <summary>Severity of a log line.</summary>
    public enum KitLogLevel
    {
        /// <summary>Only with the "Detailed log" preference.</summary>
        Debug = 0,
        /// <summary>Normal events (own file always; game log with dev mode or "Detailed log").</summary>
        Info = 1,
        /// <summary>Something unexpected that the mod handled (yellow in the game log).</summary>
        Warn = 2,
        /// <summary>A real error (red in the game log).</summary>
        Error = 3,
    }

    /// <summary>
    /// A mod's log (<see cref="KitMod.Log"/>). Warn and Error always go to the game log; Info only in dev mode or with the global
    /// "Detailed log" preference; Debug only with "Detailed log". Every line also goes to the mod's own file next to the game config,
    /// &lt;ShortId&gt;.log (rotated at 1 MB, previous kept as .old). Game log lines start with "[&lt;Name&gt;] [&lt;channel&gt;] ".
    /// </summary>
    public sealed class KitLog
    {
        const long RotateBytes = 1_000_000;
        readonly KitMod k;
        readonly object gate = new object();
        readonly HashSet<string> onceKeys = new HashSet<string>();
        readonly LinkedList<string> recent = new LinkedList<string>();
        string filePath;
        bool fileBroken;

        internal KitLog(KitMod owner) { k = owner; }

        /// <summary>Errors written through this log since start.</summary>
        public int ErrorCount { get; private set; }
        /// <summary>Warnings written through this log since start.</summary>
        public int WarnCount { get; private set; }

        /// <summary>Name of the mod's log file (never the full path: reports must not contain personal paths).</summary>
        public string FileName => Path.GetFileName(FilePath);

        internal string FilePath
        {
            get
            {
                if (filePath == null)
                {
                    string name = SafeFileName((k?.Cfg?.ShortId ?? "JBFramework") + ".log");
                    try { filePath = Path.Combine(GenFilePaths.ConfigFolderPath, name); }
                    catch { filePath = Path.Combine(Path.GetTempPath(), name); }
                }
                return filePath;
            }
        }

        /// <summary>Debug line (only with the "Detailed log" preference).</summary>
        public void Debug(string channel, string message) => Write(KitLogLevel.Debug, channel, message, null);
        /// <summary>Info line.</summary>
        public void Info(string channel, string message) => Write(KitLogLevel.Info, channel, message, null);
        /// <summary>Warning (yellow in the game log).</summary>
        public void Warn(string channel, string message) => Write(KitLogLevel.Warn, channel, message, null);
        /// <summary>Error (red in the game log), with the exception's type, message and stack when given.</summary>
        public void Error(string channel, string message) => Write(KitLogLevel.Error, channel, message, null);
        /// <summary>An error with the exception (full stack in the log file).</summary>
        public void Error(string channel, string message, Exception e) => Write(KitLogLevel.Error, channel, message, e);

        /// <summary>Logs only the first time <paramref name="key"/> is seen in this session (fallbacks, missing optional mods …).</summary>
        public void Once(string key, KitLogLevel level, string channel, string message)
        {
            lock (gate) { if (!onceKeys.Add(key ?? "")) return; }
            Write(level, channel, message, null);
        }

        /// <summary>Copy of the last (up to 300) lines, for the diagnosis page and bug reports.</summary>
        internal string[] RecentSnapshot()
        {
            lock (gate) { var a = new string[recent.Count]; recent.CopyTo(a, 0); return a; }
        }

        string Prefix => "[" + (k?.Name ?? "JBFramework") + "] ";

        void Write(KitLogLevel level, string channel, string msg, Exception e)
        {
            bool verbose = Framework.VerboseLog;
            if (level == KitLogLevel.Debug && !verbose) return;
            string tick;
            try { tick = Current.Game != null ? Find.TickManager?.TicksGame.ToString() ?? "-" : "menu"; } catch { tick = "?"; }
            string lv = level == KitLogLevel.Debug ? "DEBUG" : level == KitLogLevel.Info ? "INFO " : level == KitLogLevel.Warn ? "WARN " : "ERROR";
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | tick {tick} | {lv} | {(channel ?? "Core"),-8} | {msg}";
            if (e != null) line += " | " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace;
            lock (gate)
            {
                if (level == KitLogLevel.Error) ErrorCount++; else if (level == KitLogLevel.Warn) WarnCount++;
                recent.AddLast(line); while (recent.Count > 300) recent.RemoveFirst();
                AppendFile(line);
            }
            string game = Prefix + "[" + (channel ?? "Core") + "] " + msg + (e != null ? "\n" + e : "");
            try
            {
                if (level == KitLogLevel.Error) Log.Error(game);
                else if (level == KitLogLevel.Warn) Log.Warning(game);
                else if (verbose || Prefs.DevMode) Log.Message(game);
            }
            catch { }
        }

        void AppendFile(string line)
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
        internal void SessionStart()
        {
            string rw;
            try { rw = VersionControl.CurrentVersionStringWithRev; } catch { rw = "?"; }
            lock (gate) AppendFile($"\n===== {k?.Name} {k?.Version ?? "?"} · {Framework.Name} {Framework.Version} (API {Framework.ApiLevel}) · RimWorld {rw} · {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====");
        }

        static string SafeFileName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }
}
