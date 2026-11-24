// SPDX-License-Identifier: MIT
// Lifted from Hearth & Harbor Hearth.Core/Testing/HearthTests.cs (suite base, context, safe cells). Compiled only with -p:JBTests=true.
#if JB_TESTS
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace JBKit.Testing
{
    /// <summary>
    /// One in-game test suite of this mod. <see cref="Run"/> is a coroutine: <c>yield return N</c> waits N game ticks (0 = next tick).
    /// Use <c>t.Check(cond, "what")</c> for assertions; an exception fails the suite. Suites are found in this mod's own assembly only.
    /// Name them "&lt;shortid&gt;.&lt;what&gt;" (kit suites are "kit.*"). Start: JB_TEST=all, a list "a,b", a prefix "lo.*" or "section:&lt;id&gt;"
    /// (tools/jbmod does this for you).
    /// <code>
    /// #if JB_TESTS
    /// sealed class RegrowSuite : JBKit.Testing.KitTestSuite
    /// {
    ///     public override string Name => "lo.regrow";
    ///     public override IEnumerable&lt;int&gt; Run(TestContext t) { …; yield return 2500; t.Check(…, "limb regrew"); }
    /// }
    /// #endif
    /// </code>
    /// </summary>
    internal abstract class KitTestSuite
    {
        public abstract string Name { get; }
        /// <summary>Hard tick budget; the suite fails if it runs longer.</summary>
        public virtual int MaxTicks => 30000;
        /// <summary>Section the suite belongs to (JB_TEST=section:&lt;id&gt; selects all suites of a section). Default: core.</summary>
        public virtual string Section => KitSections.Core;
        public abstract IEnumerable<int> Run(TestContext t);
        /// <summary>True for suites that need a special run (a loaded save, extra mods, an old settings file): they run only when named
        /// explicitly, never with "all", a prefix or a section filter.</summary>
        public virtual bool ExplicitOnly => false;
        /// <summary>True for suites that check a loaded save: before them the harness only stops the storyteller and leaves pawns, jobs and
        /// weather alone (the general clean-up would change the very state the suite wants to compare).</summary>
        public virtual bool KeepsLoadedGame => false;
    }

    /// <summary>What a suite gets: checks, notes and the map.</summary>
    internal sealed class TestContext
    {
        public Map Map => Find.CurrentMap;
        public string Suite;
        public int Checks, Failures;
        public readonly List<string> Lines = new List<string>();

        public void Check(bool ok, string what)
        {
            Checks++;
            if (!ok) Failures++;
            Log((ok ? "  ok   " : "  FAIL ") + what);
        }
        public void Note(string what) => Log("  note " + what);
        public void Log(string line) { Lines.Add(line); Verse.Log.Message("[JBTEST] " + Suite + ": " + line); }

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
    internal static class TestCells
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
#endif
