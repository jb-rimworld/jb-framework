// SPDX-License-Identifier: MIT
// Red-error recorder of the test build (new; H&H counted only its own HLog errors). Compiled only with -p:JBTests=true.
#if JB_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace JBKit.Testing
{
    /// <summary>
    /// Records every red error the game logs from the moment the mod constructor ran (installed by KitBoot.InstallTesting) and tells
    /// "ours" (message or stack names this mod's namespace, assembly, "[Name]" log prefix or packageId) from everybody else's.
    /// kit.smoke and the runner fail on any of ours.
    /// </summary>
    internal static class KitTestLog
    {
        internal sealed class Entry { public string Phase, Type, Text; public bool Ours; }
        static readonly object Lock = new object();
        static readonly List<Entry> entries = new List<Entry>();
        static bool installed;
        static string[] markers;

        internal static void Install()
        {
            if (installed) return;
            installed = true;
            var m = new List<string>();
            try
            {
                var asm = typeof(KitTestLog).Assembly.GetName().Name;
                if (!string.IsNullOrEmpty(asm)) m.Add(asm + ".");
                var ns = Kit.Mod?.GetType().Namespace;
                if (!string.IsNullOrEmpty(ns)) m.Add(ns + ".");
                m.Add(KitLog.Prefix.Trim());
                var pid = Kit.Mod?.Content?.PackageId;
                if (!string.IsNullOrEmpty(pid)) m.Add(pid);
            }
            catch { }
            markers = m.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToArray();
            Application.logMessageReceivedThreaded += OnLog;
        }

        static void OnLog(string msg, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            try
            {
                string all = (msg ?? "") + "\n" + (stack ?? "");
                bool ours = markers != null && markers.Any(k => all.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
                string first = (msg ?? "").Split('\n')[0].Trim();
                if (first.Length > 300) first = first.Substring(0, 300) + "…";
                string phase;
                try { phase = Current.ProgramState == ProgramState.Playing ? "play" : "load"; } catch { phase = "?"; }
                lock (Lock) entries.Add(new Entry { Phase = phase, Type = type.ToString(), Text = first, Ours = ours });
            }
            catch { }
        }

        /// <summary>Number of red errors recorded so far (all, or only ours).</summary>
        public static int Count(bool oursOnly) { lock (Lock) return oursOnly ? entries.Count(e => e.Ours) : entries.Count; }

        /// <summary>Entries from index <paramref name="from"/> on (use a Count() taken earlier to get "since then").</summary>
        public static List<Entry> Since(int from, bool oursOnly)
        {
            lock (Lock) return entries.Skip(Math.Max(0, from)).Where(e => !oursOnly || e.Ours).ToList();
        }

        /// <summary>Index for <see cref="Since"/>: the number of entries right now.</summary>
        public static int Mark() { lock (Lock) return entries.Count; }

        public static string Format(Entry e) => e.Phase + " " + e.Type + ": " + e.Text;
    }
}
#endif
