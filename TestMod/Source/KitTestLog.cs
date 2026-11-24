// SPDX-License-Identifier: MIT
// Red-error recorder of the test runs (new; H&H counted only its own HLog errors). Part of JBFramework.Testing.dll.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace JBFramework.Testing
{
    /// <summary>
    /// Records every red error the game logs from the moment the test mod was created (before the mods under test, after the framework;
    /// the probe mod records the earlier ones) and tells "ours" from everybody else's. "Ours" = the framework (namespace, assembly,
    /// "[JB Framework]" log prefix, packageId) or the mod under test (its namespaces, assemblies, "[Name]" log prefix, packageId).
    /// The markers are worked out lazily (the mod under test does not exist yet when recording starts).
    /// </summary>
    internal static class KitTestLog
    {
        internal sealed class Entry { public string Phase, Type, Text, All; }
        static readonly object Lock = new object();
        static readonly List<Entry> entries = new List<Entry>();
        static bool installed;

        internal static void Install()
        {
            if (installed) return;
            installed = true;
            Application.logMessageReceivedThreaded += OnLog;
        }

        static void OnLog(string msg, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            try
            {
                string all = (msg ?? "") + "\n" + (stack ?? "");
                string first = (msg ?? "").Split('\n')[0].Trim();
                if (first.Length > 300) first = first.Substring(0, 300) + "…";
                string phase;
                try { phase = Current.ProgramState == ProgramState.Playing ? "play" : "load"; } catch { phase = "?"; }
                lock (Lock) entries.Add(new Entry { Phase = phase, Type = type.ToString(), Text = first, All = all });
            }
            catch { }
        }

        /// <summary>Markers of the framework itself (its own red errors count as ours in every run).</summary>
        internal static string[] FrameworkMarkers()
        {
            var m = new List<string> { typeof(Framework).Namespace + ".", typeof(Framework).Assembly.GetName().Name + ".dll", "[" + Framework.Name + "]", Framework.PackageId };
            try { var self = Framework.Self; if (self != null) m.Add(self.PackageId); } catch { }
            return m.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToArray();
        }

        /// <summary>Markers of a dependent mod: namespaces of its types, assembly names, log prefix, packageId (loaded and published).</summary>
        internal static string[] ModMarkers(KitMod k)
        {
            var m = new List<string>();
            if (k == null) return m.ToArray();
            try
            {
                m.Add("[" + k.Name + "]");
                m.Add(k.PackageId);
                m.Add(FrameworkGame.SaveKey(k));
                var asms = k.Content?.assemblies?.loadedAssemblies?.ToList() ?? new List<System.Reflection.Assembly>();
                if (k.Mod != null && !asms.Contains(k.Mod.GetType().Assembly)) asms.Add(k.Mod.GetType().Assembly);
                foreach (var a in asms)
                {
                    if (a == null || a == typeof(Framework).Assembly || a == typeof(KitTestLog).Assembly) continue;
                    m.Add(a.GetName().Name + ".");
                    foreach (var t in KitPatches.SafeTypes(a)) if (!string.IsNullOrEmpty(t.Namespace)) m.Add(t.Namespace + ".");
                }
            }
            catch { }
            return m.Where(x => !string.IsNullOrEmpty(x) && x.Length > 2).Distinct().ToArray();
        }

        static string[] defaultMarkers;
        /// <summary>Framework + mod under test (+ the harness assembly).</summary>
        static string[] Default
        {
            get
            {
                if (defaultMarkers != null) return defaultMarkers;
                var k = KitTestEnv.Target;
                if (k == null) return FrameworkMarkers();
                var m = FrameworkMarkers().Concat(ModMarkers(k)).Distinct().ToArray();
                if (Current.ProgramState == ProgramState.Playing) defaultMarkers = m;
                return m;
            }
        }

        static readonly List<string> expected = new List<string>();
        /// <summary>A red error a suite provokes on purpose (text fragment): it never counts as ours.</summary>
        public static void Expect(string fragment) { lock (Lock) if (!expected.Contains(fragment)) expected.Add(fragment); }
        static bool IsExpected(Entry e) => expected.Any(x => e.All.IndexOf(x, StringComparison.Ordinal) >= 0);
        /// <summary>Recorded red errors that a suite provoked on purpose.</summary>
        public static int ExpectedCount() { lock (Lock) return entries.Count(IsExpected); }

        static bool Matches(Entry e, string[] markers) => !IsExpected(e) && markers.Any(k => e.All.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>Number of red errors recorded so far (all, or only ours).</summary>
        public static int Count(bool oursOnly) { lock (Lock) { var m = Default; return oursOnly ? entries.Count(e => Matches(e, m)) : entries.Count(e => !IsExpected(e)); } }

        /// <summary>Entries from index <paramref name="from"/> on (use <see cref="Mark"/> taken earlier to get "since then").</summary>
        public static List<Entry> Since(int from, bool oursOnly) => Since(from, oursOnly ? Default : null);

        /// <summary>Entries from index <paramref name="from"/> on that name one of <paramref name="markers"/> (null = all).</summary>
        public static List<Entry> Since(int from, string[] markers)
        {
            lock (Lock) return entries.Skip(Math.Max(0, from)).Where(e => markers == null ? !IsExpected(e) : Matches(e, markers)).ToList();
        }

        /// <summary>Index for <see cref="Since(int, bool)"/>: the number of entries right now.</summary>
        public static int Mark() { lock (Lock) return entries.Count; }

        public static string Format(Entry e) => e.Phase + " " + e.Type + ": " + e.Text;
    }
}
