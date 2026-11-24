// SPDX-License-Identifier: MIT
// Suites for the knowledge base, the adapters, the shared functions and the random streams (FRAMEWORK.md section 3: new files of
// Source/JBFramework/{Compat,Shared,Random}). Self-contained: move this file wherever the harness wants its suites.
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace JBFramework.Testing
{
    /// <summary>fw.known: the knowledge base is loaded, consistent and queryable; detection helpers accept any packageId spelling.</summary>
    internal sealed class FwKnownSuite : KitTestSuite
    {
        public override string Name => "fw.known";

        public override IEnumerable<int> Run(TestContext t)
        {
            var all = KitKnown.All;
            t.Check(all.Count >= 40, "knowledge base has at least 40 notes (" + all.Count + ")");
            t.Check(all.All(n => n.packageIds.Count > 0 && !string.IsNullOrEmpty(n.label) && !string.IsNullOrEmpty(n.description)), "every note has packageIds, a label and a description");
            t.Check(all.All(n => n.tags.All(x => x == x.Trim().ToLowerInvariant())), "tags are lower case");
            var dup = all.SelectMany(n => n.packageIds.Select(p => (p: KitDetect.Normalize(p), n))).GroupBy(x => x.p).Where(g => g.Select(x => x.n).Distinct().Count() > 1).Select(g => g.Key).ToList();
            t.Check(dup.Count == 0, "no packageId in two notes" + (dup.Count > 0 ? ": " + string.Join(", ", dup) : ""));
            var harmony = KitKnown.Note("brrainz.harmony");
            t.Check(harmony != null && KitKnown.Note("BRRAINZ.HARMONY_steam") == harmony, "Note() ignores case and the _steam postfix");
            t.Check(harmony != null && harmony.Active, "Harmony's note is active (Harmony is loaded)");
            t.Check(KitDetect.IsActive("brrainz.harmony_steam") && KitDetect.IsActive("Brrainz.Harmony"), "IsActive ignores case and _steam");
            t.Check(!KitDetect.IsActive("jbframework.surely.not.installed"), "unknown packageId is not active");
            t.Check(KitDetect.Same("A.b_steam", "a.B") && KitDetect.Normalize(" X.Y_steam ") == "x.y", "Same/Normalize");
            t.Check(KitDetect.ActivePackageIds.Contains("ludeon.rimworld"), "ActivePackageIds lists the core");
            t.Check(KitKnown.WithTag(KitTags.ChangesBillsUi).Any(n => n.packageIds.Contains("Andromeda.NiceBillTab")), "WithTag finds Nice Bill Tab under changes-bills-ui");
            t.Check(KitKnown.ActiveWithTag(KitTags.Library).Contains(harmony), "ActiveWithTag(library) contains Harmony");
            var k = t.Mod;
            KitKnown.Declare(k, "library", " LIBRARY ");
            t.Check(KitKnown.DeclaredTags(k).Count >= 1 && KitKnown.DeclaredTags(k).Contains("library"), "Declare normalises tags");
            t.Check(KitKnown.RelevantFor(k).Contains(harmony), "RelevantFor lists Harmony for a mod that declared 'library'");
            t.Check(KitKnown.OwnerName("brrainz.harmony") == harmony.LabelCap.ToString(), "OwnerName maps a packageId to the note's name");
            t.Check(KitKnown.OwnerName("some.unknown.harmony.id") == "some.unknown.harmony.id", "OwnerName falls back to the id");
            var report = KitReport.Lines(k);
            t.Check(report.Any(l => l.StartsWith("Known mods for tags")), "the mod's report lists the known mods for its tags");
            var fwReport = Framework.Self == null ? new List<string>() : KitReport.Lines(Framework.Self);
            t.Check(fwReport.Any(l => l.StartsWith("Knowledge base:")), "the framework's report has the knowledge base line");
            if (LanguageDatabase.activeLanguage?.folderName == "German (Deutsch)")
                t.Check(harmony.description.StartsWith("Die "), "German description injected");
            yield break;
        }
    }

    /// <summary>fw.adapters: every adapter answers safely without its mod (and with it, when present).</summary>
    internal sealed class FwAdaptersSuite : KitTestSuite
    {
        public override string Name => "fw.adapters";

        public override IEnumerable<int> Run(TestContext t)
        {
            var gun = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_AssaultRifle");
            if (!KitCE.Active)
            {
                t.Check(KitCE.AmmoSetOf(gun) == null && KitCE.DefaultAmmoFor(gun) == null && KitCE.AmmoTypesFor(gun).Count == 0, "CE inactive: no ammo");
                t.Check(!KitCE.IsAmmo(ThingDefOf.Steel), "CE inactive: steel is no ammo");
            }
            else
            {
                var ammo = KitCE.DefaultAmmoFor(gun);
                t.Check(ammo != null && KitCE.IsAmmo(ammo), "CE active: assault rifle has a default ammo (" + ammo?.defName + ")");
            }
            t.Check(!KitHAR.IsAlienRace(ThingDefOf.Human) && !KitHAR.IsCustomBodied(ThingDefOf.Human), "humans are neither alien nor custom-bodied");
            t.Check(!KitHAR.IsCustomBodied(ThingDefOf.Thrumbo), "animals are not 'custom-bodied humanlikes'");
            t.Check(KitHAR.HumanlikeRaces().FirstOrDefault() == ThingDefOf.Human, "humans come first in HumanlikeRaces");
            KitNiceBillTab.RequestRefresh();
            t.Check(true, "Nice Bill Tab refresh does not throw (active: " + KitNiceBillTab.Active + ")");
            t.Check(KitHearth.HearthHarbor == KitDetect.IsActive(KitHearth.HearthHarborId), "Hearth & Harbor presence");
            if (!KitVEPipes.Active)
                t.Check(!KitVEPipes.IsPipeDef(ThingDefOf.Wall) && !KitVEPipes.IsOnPipeNet(null), "VEF inactive: nothing is on a pipe net");
            else
                t.Check(DefDatabase<ThingDef>.AllDefsListForReading.Any(KitVEPipes.IsPipeDef), "VEF active: some def connects to a pipe net");
            yield break;
        }
    }

    /// <summary>fw.random: per-save streams are persistent, independent, namespaced per mod; the shuffle bag deals fairly.</summary>
    internal sealed class FwRandomSuite : KitTestSuite
    {
        public override string Name => "fw.random";

        public override IEnumerable<int> Run(TestContext t)
        {
            var k = t.Mod;
            t.Check(KitRandom.Persistent && KitRandom.SaveSeed != 0, "a running game has persistent streams and a seed");
            var a = KitRandom.Stream(k, "fw.test.a");
            t.Check(ReferenceEquals(a, KitRandom.Stream(k, "fw.test.a")), "same name, same stream object within a game");
            ulong startA = a.Counter;
            var b = KitRandom.Stream(k, "fw.test.b");
            ulong startB = b.Counter;
            for (int i = 0; i < 10; i++) a.NextULong();
            t.Check(b.Counter == startB && a.Counter == startA + 10, "streams are independent");
            var fake = new KitRandomStream(KitRandom.SaveSeed, KitRandom.Key(k, "fw.test.a"), startA);
            var probe = a.Clone(); probe.Counter = startA;
            t.Check(fake.NextULong() == probe.NextULong(), "stream = (save seed, hash of '<mod>/<name>', counter)");
            t.Check(KitRandom.Key(k, "x") != KitRandom.Key(Framework.Self, "x") && KitRandom.Key(null, "x") == "global/x", "names are namespaced per mod");
            var g = KitRandom.Stream("fw.test/global");
            t.Check(g != null && !ReferenceEquals(g, a), "global streams exist");
            var v1 = KitRandom.Vanilla().NextULong();
            t.Check(KitRandom.Vanilla() != null, "vanilla-fed stream (" + v1 + ")");
            var positions = KitRandomState.Live.Positions().ToDictionary(x => x.Key, x => x.Value);
            t.Check(positions.TryGetValue(KitRandom.Key(k, "fw.test.a"), out var pa) && pa == a.Counter, "positions are tracked for the save");

            var bag = new KitShuffleBag(5);
            var seen = new List<int>();
            for (int i = 0; i < 5; i++) seen.Add(bag.Next(a));
            t.Check(seen.OrderBy(x => x).SequenceEqual(new[] { 0, 1, 2, 3, 4 }), "shuffle bag deals every index once per round: " + string.Join(",", seen));
            t.Check(bag.Remaining == 0 && bag.Next(a) >= 0 && bag.Remaining == 4, "a new round starts automatically");
            bag.Resize(0);
            t.Check(bag.Next(a) == -1, "empty bag returns -1");
            yield break;
        }
    }

    /// <summary>
    /// fw.shared: def edits restore exactly; tier families get the gizmo comp and swap in place; the architect regroups a family into one
    /// dropdown; the dropdown material rule applies to registered stuffed defs only; removal steps report. Changes defs of the test
    /// session, so it runs only when named.
    /// </summary>
    internal sealed class FwSharedSuite : KitTestSuite
    {
        public override string Name => "fw.shared";
        public override bool ExplicitOnly => true;

        public override IEnumerable<int> Run(TestContext t)
        {
            var k = t.Mod;
            // ---- def edits
            var edits = KitDefEdits.For(k);
            var wall = ThingDefOf.Wall;
            int oldStuff = wall.costStuffCount;
            var beauty = StatDefOf.Beauty;
            bool hadBeauty = wall.statBases?.Any(s => s.stat == beauty) ?? false;
            float oldBeauty = hadBeauty ? wall.GetStatValueAbstract(beauty, ThingDefOf.WoodLog) : 0f;
            t.Check(edits.Set(wall, "costStuffCount", 1, "fwtest"), "Set a field");
            t.Check(edits.Set(wall, "costStuffCount", 2, "fwtest") && wall.costStuffCount == 2, "Set again overwrites the live value");
            t.Check(!edits.Set(wall, "noSuchField", 1, "fwtest"), "unknown field is refused");
            edits.SetStat(wall, beauty, 7f, "fwtest");
            var bed = DefDatabase<ThingDef>.GetNamedSilentFail("Bed");
            bool? medical = bed?.building?.bed_defaultMedical;
            if (bed != null) t.Check(edits.Set(bed, "building.bed_defaultMedical", !(medical ?? false), "fwtest"), "Set a nested field");
            var costList = wall.costList;
            int restored = edits.Restore("fwtest");
            t.Check(wall.costStuffCount == oldStuff, "field restored (" + wall.costStuffCount + ")");
            t.Check((wall.statBases?.Any(s => s.stat == beauty) ?? false) == hadBeauty, "stat presence restored");
            if (bed != null) t.Check(bed.building.bed_defaultMedical == medical, "nested field restored");
            t.Check(restored >= 3 && edits.Count == 0, "Restore counted " + restored + " values and cleared the book");

            // ---- tiers: two vanilla buildable buildings of one size
            var pair = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => d.category == ThingCategory.Building && d.BuildableByPlayer && d.modContentPack != null && d.modContentPack.IsCoreMod && !d.MadeFromStuff && d.size == IntVec2.One && d.passability != Traversability.Impassable)
                .Take(2).ToList();
            t.Check(pair.Count == 2, "found two 1x1 core buildings: " + string.Join(", ", pair.Select(d => d.defName)));
            if (pair.Count == 2)
            {
                KitTiers.Family(k, "fwtest", pair[0].defName, pair[1].defName);
                t.Check(KitTiers.TierOf(pair[0]) == 1 && KitTiers.TierOf(pair[1]) == 2 && KitTiers.SiblingAt(pair[0], 2) == pair[1], "family registered after startup is built at once");
                t.Check(pair[0].comps.Any(c => c is CompProperties_KitTierSwap), "members get the tier gizmo comp");
                var cell = t.FreeCellNearCenter();
                var thing = GenSpawn.Spawn(ThingMaker.MakeThing(pair[0]), cell, t.Map);
                thing.SetFaction(Faction.OfPlayer);
                yield return 1;
                t.Check(thing.TryGetComp<CompKitTierSwap>() != null, "spawned member has the comp");
                var neu = KitTiers.Swap(thing, pair[1]);
                t.Check(neu != null && neu.def == pair[1] && neu.Position == cell && thing.Destroyed && neu.Faction == Faction.OfPlayer, "Swap replaced it in place, faction kept");
                yield return 1;
                neu?.Destroy();

                // ---- architect: the pair becomes one dropdown in the first one's tab (only when both live in the same tab)
                if (pair[0].designationCategory != null && pair[0].designationCategory == pair[1].designationCategory)
                {
                    var set = new HashSet<ThingDef>(pair);
                    KitArchitect.OneEntryPerFamily(k, d => d is ThingDef td && set.Contains(td) ? "fwtest" : null);
                    var list = pair[0].designationCategory.AllResolvedDesignators;
                    var drops = list.OfType<Designator_Dropdown>().Where(dd => dd.Elements.Any(e => (e as Designator_Place)?.PlacingDef == pair[0])).ToList();
                    t.Check(drops.Count == 1 && drops[0].Elements.Any(e => (e as Designator_Place)?.PlacingDef == pair[1]), "both members are in one dropdown");
                }
                else t.Note("the two buildings live in different tabs: architect regroup not checked");
            }

            // ---- material menu rule
            KitMaterialMenu.AfterDropdownPick(k, d => d == ThingDefOf.Wall);
            var wallDes = new Designator_Build(ThingDefOf.Wall);
            t.Check(KitMaterialMenu.Wrap(null, wallDes, () => { }) != null, "registered stuffed def gets the wrapped select action");
            var other = DefDatabase<ThingDef>.AllDefsListForReading.FirstOrDefault(d => d.MadeFromStuff && d.BuildableByPlayer && d != ThingDefOf.Wall);
            if (other != null) t.Check(KitMaterialMenu.Wrap(null, new Designator_Build(other), () => { }) == null, "other stuffed defs keep vanilla's action");
            t.Check(KitMaterialMenu.IsStuffed(wallDes), "IsStuffed(wall)");

            // ---- removal step (a weapon def a quick-test colony does not have)
            var lance = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_ChargeLance");
            if (lance != null)
            {
                var spawned = GenSpawn.Spawn(ThingMaker.MakeThing(lance, GenStuff.DefaultStuffFor(lance)), t.FreeCellNearCenter(), t.Map);
                var report = new List<string>();
                KitRemoval.RemoveThings(d => d == lance, report, refund: false);
                t.Check(spawned.Destroyed && report.Count == 1, "RemoveThings removed the spawned weapon: " + report.FirstOrDefault());
                var none = new List<string>();
                KitRemoval.RemoveHediffs(h => false, none);
                t.Check(none.Count == 1, "a step that matches nothing still reports: " + none.FirstOrDefault());
            }
            yield break;
        }
    }
}
