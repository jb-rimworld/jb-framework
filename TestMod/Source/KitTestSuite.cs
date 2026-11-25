// SPDX-License-Identifier: MIT
// Lifted from Hearth & Harbor Hearth.Core/Testing/HearthTests.cs (suite base, context, safe cells). Part of JBFramework.Testing.dll
// (local test mod, never shipped).
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace JBFramework.Testing
{
    /// <summary>
    /// One in-game test suite. <see cref="Run"/> is a coroutine: <c>yield return N</c> waits N game ticks (0 = next tick).
    /// Use <c>t.Check(cond, "what")</c> for assertions; an exception fails the suite. The runner finds suites in the test assembly
    /// (the built-in kit.* and fw.* suites) and in the assemblies of the mod under test (JB_TEST_MOD). A mod's own suites are compiled
    /// only in test builds (-p:JBTests=true defines JB_TESTS and references JBFramework.Testing.dll):
    /// <code>
    /// #if JB_TESTS
    /// sealed class RegrowSuite : JBFramework.Testing.KitTestSuite
    /// {
    ///     public override string Name =&gt; "lo.regrow";
    ///     public override IEnumerable&lt;int&gt; Run(JBFramework.Testing.TestContext t) { var k = t.Mod; …; yield return 2500; t.Check(…, "limb regrew"); }
    /// }
    /// #endif
    /// </code>
    /// Name them "&lt;shortid&gt;.&lt;what&gt;" (built-ins are "kit.*" and "fw.*"). Selection: JB_TEST=all, a list "a,b", a prefix "lo.*"
    /// or "section:&lt;id&gt;" (tools/jbmod does this for you).
    /// </summary>
    public abstract class KitTestSuite
    {
        /// <summary>Unique suite name ("lo.regrow").</summary>
        public abstract string Name { get; }
        /// <summary>Hard tick budget; the suite fails if it runs longer.</summary>
        public virtual int MaxTicks => 30000;
        /// <summary>Section the suite belongs to (JB_TEST=section:&lt;id&gt; selects all suites of a section). Default: core.</summary>
        public virtual string Section => KitMod.Core;
        /// <summary>The suite body (a coroutine of tick waits).</summary>
        public abstract IEnumerable<int> Run(TestContext t);
        /// <summary>True for suites that need a special run (a loaded save, extra mods, an old settings file): they run only when named
        /// explicitly or by a prefix that is not "all" (e.g. "fw.*"), never with "all" or a section filter.</summary>
        public virtual bool ExplicitOnly => false;
        /// <summary>True for suites that check a loaded save: before them the harness only stops the storyteller and leaves pawns, jobs and
        /// weather alone (the general clean-up would change the very state the suite wants to compare).</summary>
        public virtual bool KeepsLoadedGame => false;
    }

    /// <summary>
    /// What a suite gets: the mod under test, checks, notes, the map, red-error marks, screenshots, screen sizes and the two phases of a
    /// save round trip. A mod WITHOUT a framework context (build/JBTests.props) gets <see cref="Mod"/> = null and uses <see cref="Content"/>.
    /// </summary>
    public sealed class TestContext
    {
        internal TestContext(string suite, KitMod mod, ModContentPack content) { Suite = suite; Mod = mod; Content = content ?? mod?.Content; }

        /// <summary>
        /// The context of the mod under test (JB_TEST_MOD; the framework's own context when the framework is tested). Null for a mod that
        /// does not use the framework (its test build imports build/JBTests.props): use <see cref="Content"/> there.
        /// </summary>
        public KitMod Mod { get; }
        /// <summary>The content pack of the mod under test (also for a mod without a framework context), or null.</summary>
        public ModContentPack Content { get; }
        /// <summary>The current map.</summary>
        public Map Map => Find.CurrentMap;
        /// <summary>Name of the running suite.</summary>
        public string Suite { get; }
        /// <summary>Checks so far.</summary>
        public int Checks { get; internal set; }
        /// <summary>Failed checks so far (an exception or timeout adds one).</summary>
        public int Failures { get; internal set; }
        internal readonly List<string> Lines = new List<string>();

        /// <summary>One assertion: counted, written to the result file and the game log.</summary>
        public void Check(bool ok, string what)
        {
            Checks++;
            if (!ok) Failures++;
            Log((ok ? "  ok   " : "  FAIL ") + what);
        }
        /// <summary>A note (no assertion).</summary>
        public void Note(string what) => Log("  note " + what);
        /// <summary>A raw line in the result file (also in the game log with the [JBTEST] prefix).</summary>
        public void Log(string line) { Lines.Add(line); Verse.Log.Message("[JBTEST] " + Suite + ": " + line); }

        // red errors --------------------------------------------------------------------------------------------------------
        /// <summary>A mark for <see cref="RedErrorsSince"/>: the number of red errors recorded so far (all mods).</summary>
        public int Mark() => KitTestLog.Mark();
        /// <summary>
        /// Red errors of ours (the framework or the mod under test: namespaces, assemblies, log prefix, packageId) logged since
        /// <paramref name="mark"/>, one line each ("play Error: text"). Errors announced with <see cref="ExpectRedError"/> never count.
        /// </summary>
        public IReadOnlyList<string> RedErrorsSince(int mark) => KitTestLog.Since(mark, true).Select(KitTestLog.Format).ToList();
        /// <summary>One check "no red error of ours since <paramref name="mark"/>"; up to ten of them are noted.</summary>
        public void CheckNoRedErrors(int mark, string what) => KitSuiteUtil.NoRedErrors(this, mark, what);
        /// <summary>
        /// A red error the suite provokes on purpose (a fragment of its text; announce it BEFORE provoking it): it never counts as ours in
        /// this run, neither in the harness (RESULT, <see cref="RedErrorsSince"/>) nor in tools/jbmod's probe report (counted as "expected").
        /// The debug log window it opens is closed by the runner. Fenced framework errors carry the exception message on their first line,
        /// so a fragment of the exception text works too.
        /// </summary>
        public void ExpectRedError(string fragment) => KitTestLog.Expect(fragment);

        // UI ----------------------------------------------------------------------------------------------------------------
        /// <summary>Waits until <paramref name="frames"/> more frames were drawn: <c>foreach (var y in t.Frames(3)) yield return y;</c></summary>
        public IEnumerable<int> Frames(int frames) => KitUiTest.Frames(frames);
        /// <summary>
        /// A screenshot of the whole (virtual) screen as &lt;name&gt;.png in the run's screenshot folder (tools/jbmod with JB_UI_SHOTS=1;
        /// no-op without it). Captured at the end of the frame: wait a frame or two before closing what it shows.
        /// </summary>
        public void Shot(string name) => KitUiTest.Shot(name);
        /// <summary>
        /// Keeps <paramref name="window"/> open until the suite ends: the runner normally closes every message box and pausing dialog at
        /// once (a confirmation can then be shot: <c>var d = …; Find.WindowStack.Add(d); t.KeepOpen(d); foreach (var y in t.Frames(2)) yield return y; t.Shot("x"); d.Close();</c>).
        /// A kept window never pauses the game (suites wait on ticks): its forcePause is switched off.
        /// </summary>
        public void KeepOpen(Window window) { if (window == null) return; window.forcePause = false; KitTestRunner.KeptWindows.Add(window); }
        /// <summary>
        /// Opens the welcome card of the mod under test for the running game now (no delay; also when its "done" flag is set) and returns
        /// it, or null (no per-save profile, or the mod has no card). Close it with <c>Close()</c>; closing marks the card done in this save.
        /// </summary>
        public Window OpenWelcomeCard()
        {
            var w = Mod == null ? null : KitFirstStartCard.OpenNow(Mod);
            if (w != null) KeepOpen(w);
            return w;
        }
        /// <summary>
        /// Lays the game out for a virtual screen <paramref name="height"/> pixels high (1280x720: 720; 2560x1440: 1440) by setting
        /// Prefs.UIScale on the test screen; the width follows the real screen's aspect. Real layout and real screenshots, not a pretended
        /// size. Dispose restores the scale (<c>using (t.ScreenHeight(720)) …</c> around the frames; reopen windows after the change).
        /// </summary>
        public IDisposable ScreenHeight(int height) => KitUiTest.AtScreenHeight(height);

        // saves -------------------------------------------------------------------------------------------------------------
        /// <summary>1 in a new game (phase 1 of jbmod saveload / twophase), 2 in a game loaded from a save (phase 2).</summary>
        public int Phase => SaveLoadKit.LoadedSave == null ? 1 : 2;
        /// <summary>Name of the save this game was loaded from (phase 2), else null.</summary>
        public string LoadedSave => SaveLoadKit.LoadedSave;
        /// <summary>
        /// Saves the running game as <paramref name="name"/> with a fingerprint of the mod's save state (the harness is left out of the
        /// save). jbmod's phase 2 loads the newest save of phase 1; there <see cref="CheckSaveFingerprint"/> compares. One check.
        /// </summary>
        public bool SaveGame(string name) => SaveLoadKit.SaveWithFingerprint(name, this);
        /// <summary>Phase 2: compares the fingerprint written at save time with the state right after the load (one check per difference set).</summary>
        public void CheckSaveFingerprint()
        {
            if (LoadedSave == null) { Check(false, "CheckSaveFingerprint: this game was not loaded from a save (phase 1)"); return; }
            SaveLoadKit.CompareFingerprints(LoadedSave, this);
        }
        /// <summary>
        /// A field path (regex on "game/MyComp.cache[].x", list indices stripped to []) that is rebuilt after a load by design: the save
        /// fingerprint ignores it, with <paramref name="why"/>. Fields can also carry [<see cref="KitTransientAttribute"/>].
        /// </summary>
        public void DeclareTransient(string pathPattern, string why) => SaveFingerprint.Declare(pathPattern, why);

        /// <summary>A walkable cell at (or near) the map centre that the colonists can reach. See <see cref="TestCells.Anchor"/>.</summary>
        public IntVec3 Center => TestCells.Anchor(Map);

        /// <summary>A free, standable, revealed, reachable cell without a building near the anchor (the anchor itself if none).</summary>
        public IntVec3 FreeCellNearCenter(int radius = 20)
        {
            var map = Map; var c = TestCells.Anchor(map);
            foreach (var cell in GenRadial.RadialCellsAround(c, radius, true))
                if (cell.InBounds(map) && cell.Standable(map) && cell.GetFirstBuilding(map) == null && !cell.Fogged(map) && TestCells.Reachable(map, c, cell)) return cell;
            return c;
        }
    }

    /// <summary>
    /// Where suites put their things and pawns. The -quicktest map is random: its centre can lie inside a mountain, in water or in a closed
    /// pocket, where vanilla AI throws and every job that should walk there fails. Suites anchor at <see cref="Anchor"/> instead of map.Center.
    /// </summary>
    public static class TestCells
    {
        static int cachedMap = -1; static IntVec3 cached = IntVec3.Invalid;

        /// <summary>The map centre when it is standable, revealed and reachable from the colonists; else the nearest such cell.</summary>
        public static IntVec3 Anchor(Map map)
        {
            if (map == null) return IntVec3.Invalid;
            if (cachedMap == map.uniqueID && cached.IsValid && Usable(map, cached, Root(map))) return cached;
            var root = Root(map);
            var found = map.Center;
            if (!Usable(map, found, root))
            {
                found = IntVec3.Invalid;
                int n = GenRadial.NumCellsInRadius(Math.Min(GenRadial.MaxRadialPatternRadius - 1f, map.Size.x / 2f));
                for (int i = 0; i < n; i++)
                {
                    var c = map.Center + GenRadial.RadialPattern[i];
                    if (Usable(map, c, root)) { found = c; break; }
                }
                if (!found.IsValid) found = root.IsValid ? root : map.Center;
                Verse.Log.Message("[JBTEST] map centre " + map.Center + " is not walkable or not reachable; suites anchor at " + found);
            }
            cachedMap = map.uniqueID; cached = found;
            return found;
        }

        /// <summary>A free w×h patch near the anchor: every cell (and a ring around it) standable, revealed, without buildings, reachable.</summary>
        public static bool TryFindFreeRect(Map map, int w, int h, out CellRect rect, int startRadius = 3, float maxRadius = 60f)
        {
            rect = default;
            var center = Anchor(map);
            foreach (var c in GenRadial.RadialCellsAround(center, maxRadius, true))
            {
                if (c.DistanceTo(center) < startRadius) continue;
                var r = CellRect.CenteredOn(c, w, h);
                if (!r.ExpandedBy(1).InBounds(map)) continue;
                bool ok = true;
                foreach (var cell in r.ExpandedBy(1)) { if (!cell.Standable(map) || cell.GetFirstBuilding(map) != null || cell.Fogged(map)) { ok = false; break; } }
                if (ok && !Reachable(map, center, r.CenterCell)) ok = false;
                if (ok) { rect = r; return true; }
            }
            return false;
        }

        /// <summary>Can a pawn walk from a to b (doors allowed)?</summary>
        public static bool Reachable(Map map, IntVec3 a, IntVec3 b)
            => a.IsValid && b.IsValid && map.reachability.CanReach(a, b, PathEndMode.OnCell, TraverseMode.PassDoors, Danger.Deadly);

        static IntVec3 Root(Map map)
        {
            foreach (var p in map.mapPawns.FreeColonistsSpawned) if (p.Position.IsValid && p.Position.Standable(map)) return p.Position;
            return IntVec3.Invalid;
        }

        static bool Usable(Map map, IntVec3 c, IntVec3 root)
            => c.InBounds(map) && c.Standable(map) && !c.Fogged(map) && c.GetDistrict(map) != null && (!root.IsValid || Reachable(map, root, c));
    }
}
