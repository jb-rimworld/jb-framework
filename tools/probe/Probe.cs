// SPDX-License-Identifier: MIT
// JB probe: a tiny test-only mod for tools/jbmod, generalised from Hearth & Harbor tools/probe (HH_PROBE_* → JB_PROBE_*).
// It never ships and references no mod, so it also runs in the phases WITHOUT the mod under test (removal, vanilla save).
//   - always (JB_PROBE_ERR set): every red error the game logs is appended to $JB_PROBE_ERR, one line each:
//       phase<TAB>type<TAB>first line of the message<TAB>where (up to 6 stack frames, "a < b < c")
//     phase = load|play. tools/jbmod splits these lines into "ours" and "others".
//   - JB_PROBE_DAYS=<n>: once the game runs (new game or loaded save), play n days at top speed (dialogs closed, storyteller silenced),
//     write a census to $JB_PROBE_OUT, optionally save as $JB_PROBE_SAVE, and quit. 0 = save/quit at the first tick.
//     Without JB_PROBE_DAYS the probe only records errors (the mod's own test runner drives the game).
// Loading a save at start is done by vanilla: with dev mode on, a save named "autostart" is loaded from the main menu.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace JBProbe
{
    public sealed class ProbeMod : Mod
    {
        static readonly object fileLock = new object();
        static string errPath;
        public static int Errors;

        public ProbeMod(ModContentPack content) : base(content)
        {
            errPath = Env("JB_PROBE_ERR");
            if (string.IsNullOrEmpty(errPath)) return;
            try { File.WriteAllText(errPath, ""); } catch { }
            Application.logMessageReceivedThreaded += OnLog;
            Log.Message("[JBPROBE] recording red errors");
        }

        static void OnLog(string msg, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            Errors++;
            string first = Clean((msg ?? "").Split('\n')[0]);
            if (first.Length > 400) first = first.Substring(0, 400) + "…";
            first += DefOwner(first);
            string phase;
            try { phase = Current.ProgramState == ProgramState.Playing ? "play" : "load"; } catch { phase = "?"; }
            try { lock (fileLock) File.AppendAllText(errPath, phase + "\t" + type + "\t" + first + "\t" + Where(msg, stack) + "\n"); } catch { }
        }

        /// <summary>Up to 6 stack frames (method names without arguments), from the stack or from an exception text inside the message.</summary>
        static string Where(string msg, string stack)
        {
            var frames = new List<string>();
            foreach (var raw in ((stack ?? "") + "\n" + (msg ?? "")).Split('\n'))
            {
                string l = raw.Trim();
                if (l.StartsWith("at ")) l = l.Substring(3);
                int p = l.IndexOf('(');
                if (p <= 0) continue;
                string m = l.Substring(0, p).Trim();
                if (m.Length == 0 || m.Contains(" ") || !m.Contains(".") || m.StartsWith("Verse.Log.") || m.StartsWith("UnityEngine.") || m.StartsWith("System.Environment")) continue;
                if (!frames.Contains(m)) frames.Add(m);
                if (frames.Count >= 6) break;
            }
            return Clean(string.Join(" < ", frames));
        }

        /// <summary>
        /// "Config error in X: …" (and "Exception in ConfigErrors of X", "XML error … X") names only a defName: the probe appends
        /// " [def of &lt;packageId&gt;]" so tools/jbmod counts it for the mod whose def it is.
        /// </summary>
        static string DefOwner(string first)
        {
            try
            {
                var m = System.Text.RegularExpressions.Regex.Match(first, @"(?:Config error in|ConfigErrors of|XML error.*?defName[= ]?)\s*""?([A-Za-z0-9_\-]+)");
                if (!m.Success) return "";
                string name = m.Groups[1].Value;
                foreach (var t in GenDefDatabase.AllDefTypesWithDatabases())
                {
                    var db = typeof(DefDatabase<>).MakeGenericType(t);
                    var get = db.GetMethod("GetNamedSilentFail", new[] { typeof(string) });
                    if (get?.Invoke(null, new object[] { name }) is Def d && d.modContentPack != null) return " [def of " + d.modContentPack.PackageId + "]";
                }
            }
            catch { }
            return "";
        }

        static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Trim();

        internal static string Env(string k) => Environment.GetEnvironmentVariable(k);
    }

    public sealed class ProbeRunner : GameComponent
    {
        int days = -1, startTick = -1, endTick = -1;
        bool done;
        readonly StringBuilder report = new StringBuilder();

        public ProbeRunner(Game g)
        {
            string d = ProbeMod.Env("JB_PROBE_DAYS");
            if (string.IsNullOrEmpty(d) || !int.TryParse(d, out days)) days = -1;
        }

        bool Driving => days >= 0;
        readonly Dictionary<Pawn, string> colonists = new Dictionary<Pawn, string>();
        readonly HashSet<Pawn> reported = new HashSet<Pawn>();

        /// <summary>Who left the colony while the probe played (died, captured, gone): name, tick, where, and the health list.</summary>
        void WatchColonists(bool final)
        {
            try
            {
                foreach (var kv in colonists)
                {
                    var p = kv.Key;
                    if (p == null || reported.Contains(p)) continue;
                    bool gone = p.Dead || p.Destroyed || !p.IsColonist || (final && !p.Spawned && p.GetCaravan() == null);
                    if (!gone) continue;
                    reported.Add(p);
                    string why = p.Dead ? "died" : p.IsPrisoner ? "captured" : !p.IsColonist ? "left the colony" : "not on a map";
                    string cause = "";
                    try { if (p.Dead && p.Corpse?.InnerPawn != null) cause = p.health?.hediffSet?.hediffs == null ? "" : string.Join(", ", p.health.hediffSet.hediffs.Select(h => h.LabelCap + (h.Part != null ? " (" + h.Part.Label + ")" : ""))); } catch { }
                    if (cause.Length == 0) try { cause = string.Join(", ", p.health.hediffSet.hediffs.Select(h => h.LabelCap.ToString())); } catch { }
                    string line = "colonist " + kv.Value + " " + why + " at tick " + Find.TickManager.TicksGame + (cause.Length > 0 ? ": " + cause : "");
                    report.AppendLine(line);
                    Log.Message("[JBPROBE] " + line);
                }
            }
            catch (Exception e) { report.AppendLine("colonist watch failed: " + e.Message); }
        }

        public override void GameComponentUpdate()
        {
            if (!Driving) return;
            var ws = Find.WindowStack;
            if (ws != null)
                foreach (var w in ws.Windows.ToList())
                    if (w is Dialog_MessageBox || w is Dialog_NodeTree || (w.forcePause && !(w is Page)))
                    { Log.Message("[JBPROBE] closing window " + w.GetType().Name); ws.TryRemove(w, false); }
            var tm = Find.TickManager;
            if (tm != null && !done && (tm.Paused || tm.CurTimeSpeed != TimeSpeed.Ultrafast)) tm.CurTimeSpeed = TimeSpeed.Ultrafast;
        }

        public override void GameComponentTick()
        {
            if (!Driving || done) return;
            int now = Find.TickManager.TicksGame;
            if (startTick < 0)
            {
                startTick = now; endTick = now + Math.Max(1, days * 60000);
                try { Find.Storyteller?.storytellerComps?.Clear(); } catch { }
                Census("at start");
                try { foreach (var p in PawnsFinder.AllMaps_FreeColonists) colonists[p] = p.LabelShortCap; } catch { }
                Log.Message($"[JBPROBE] running {days} day(s) from tick {now}");
                if (days > 0) return;
            }
            if (now % 250 == 0) WatchColonists(false);
            if (now < endTick && days > 0) return;
            done = true;
            WatchColonists(true);
            Census("after " + days + " day(s)");
            string save = ProbeMod.Env("JB_PROBE_SAVE");
            if (!string.IsNullOrEmpty(save))
            {
                try { GameDataSaveLoader.SaveGame(save); report.AppendLine("saved as " + save); }
                catch (Exception e) { report.AppendLine("SAVE FAILED " + e.Message); }
            }
            report.AppendLine("PROBE DONE errors=" + ProbeMod.Errors);
            try { File.WriteAllText(ProbeMod.Env("JB_PROBE_OUT") ?? Path.Combine(GenFilePaths.ConfigFolderPath, "jb_probe.txt"), report.ToString()); }
            catch (Exception e) { Log.Warning("[JBPROBE] " + e.Message); }
            Log.Message("[JBPROBE] done:\n" + report);
            LongEventHandler.ExecuteWhenFinished(() => Root.Shutdown());
        }

        /// <summary>Generic facts that must survive adding or removing a mod.</summary>
        void Census(string when)
        {
            try
            {
                var map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
                report.AppendLine("== " + when + " (tick " + Find.TickManager.TicksGame + ")");
                report.AppendLine("maps=" + Find.Maps.Count + " gameComponents=" + Current.Game.components.Count);
                if (map == null) { report.AppendLine("no map"); return; }
                report.AppendLine("colonists=" + map.mapPawns.FreeColonistsSpawnedCount + " pawnsSpawned=" + map.mapPawns.AllPawnsSpawned.Count() + " things=" + map.listerThings.AllThings.Count);
            }
            catch (Exception e) { report.AppendLine("census failed: " + e.Message); }
        }
    }
}
