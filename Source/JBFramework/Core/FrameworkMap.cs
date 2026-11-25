// SPDX-License-Identifier: MIT
// Per-map data of every dependent mod (KitRegistry.MapData), the MapComponent counterpart of FrameworkGame, and the component stripping
// behind KitRegistry.LegacyComponent / KitRemoval.RemoveComponents (generalised from Set my Defaults' SmdGameData patches).
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace JBFramework
{
    /// <summary>
    /// The framework's one MapComponent. Every map keeps the per-map data of the mods that registered
    /// <see cref="KitRegistry.MapData(Action{Map})"/>, each in a node under the mod's packageId; a removed or blocked mod's node is kept as raw
    /// XML and written back unchanged, so the mod finds its data again when it returns and a save never names one of its classes.
    /// Created by RimWorld for every map; mods never use it directly. Its name is in saves: never renamed.
    /// </summary>
    public sealed class FrameworkMap : MapComponent
    {
        List<KitMapEntry> mods = new List<KitMapEntry>();

        /// <summary>Created by RimWorld for a new map or before a map is loaded: every mod's reset runs for this map.</summary>
        public FrameworkMap(Map map) : base(map) { ResetOnce(map); }

        /// <summary>
        /// Every mod's per-map reset, once per Map object: on a load it runs before the map's saved components are read (prefix on
        /// Map.ExposeComponents), so a legacy MapComponent that merges into the mod's MapData state while loading keeps its merge.
        /// </summary>
        internal static void ResetOnce(Map map)
        {
            if (map == null || !KitResetLedger.First(map)) return;
            foreach (var k in Framework.Mods)
            {
                if (k.Blocked || k.MapReset == null) continue;
                var kk = k; var m = map;
                kk.Guard.Run(KitMod.Core, "MapData.reset", () => kk.MapReset(m));
            }
        }

        /// <summary>Drops the instances of legacy and removed components from this map (after the load finished).</summary>
        public override void FinalizeInit() => KitComponentStrip.DropSoon();

        /// <summary>The map is gone: every mod's reset runs for it, so per-map state keyed by this map is forgotten.</summary>
        public override void MapRemoved()
        {
            foreach (var k in Framework.Mods)
            {
                if (k.Blocked || k.MapReset == null) continue;
                var kk = k; var m = map;
                kk.Guard.Run(KitMod.Core, "MapData.removed", () => kk.MapReset(m));
            }
        }

        /// <summary>Saves and loads every mod's per-map node.</summary>
        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // an entry for every live mod with per-map data (entries of absent mods stay as loaded)
                foreach (var k in Framework.Mods)
                {
                    if (k.Blocked || k.MapExpose == null) continue;
                    string key = FrameworkGame.SaveKey(k);
                    if (!mods.Any(e => e.id == key)) mods.Add(new KitMapEntry(map) { id = key });
                }
                foreach (var e in mods) e.map = map;
            }
            Scribe_Collections.Look(ref mods, "mods", LookMode.Deep, map);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (mods == null) mods = new List<KitMapEntry>();
                mods.RemoveAll(m => m == null || string.IsNullOrEmpty(m.id));
                foreach (var e in mods) e.map = map;
            }
        }

        internal IEnumerable<string> AbsentMods => mods.Where(m => !Framework.Mods.Any(k => FrameworkGame.SaveKey(k) == m.id)).Select(m => m.id);
    }

    /// <summary>One mod's node in a map: what its MapData callback writes, or the raw XML of an absent or blocked mod.</summary>
    internal sealed class KitMapEntry : IExposable
    {
        public string id;
        public Map map;
        string rawData;

        public KitMapEntry(Map map) { this.map = map; }

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            var k = string.IsNullOrEmpty(id) ? null : Framework.Mods.FirstOrDefault(x => FrameworkGame.SaveKey(x) == id);
            if (k == null || k.Blocked || k.MapExpose == null)
            {
                rawData = KitRawXml.Keep(rawData, "map data of absent mod " + id);
                return;
            }
            rawData = null;
            if (Scribe.EnterNode("data"))
            {
                try { var m = map; k.Guard.Run(KitMod.Core, "MapData." + Scribe.mode, () => k.MapExpose(m)); }
                finally { Scribe.ExitNode(); }
            }
        }
    }

    /// <summary>Keeps the &lt;data&gt; node of an absent mod: remembered on load, written back verbatim on save. Never throws.</summary>
    internal static class KitRawXml
    {
        /// <summary>Call in the entry's ExposeData instead of the mod's callback. Returns the raw node to keep.</summary>
        internal static string Keep(string raw, string what)
        {
            try
            {
                if (Scribe.mode == LoadSaveMode.LoadingVars) return (Scribe.loader?.curXmlParent?["data"])?.OuterXml;
                if (Scribe.mode == LoadSaveMode.Saving && !string.IsNullOrEmpty(raw)) Write(raw);
            }
            catch (Exception e) { Framework.Self?.Log.Warn("Core", "Could not keep the " + what + ": " + e.Message); }
            return raw;
        }

        static System.Reflection.FieldInfo writerField;

        static void Write(string xml)
        {
            if (writerField == null) writerField = AccessTools.Field(typeof(ScribeSaver), "writer");
            if (writerField?.GetValue(Scribe.saver) is System.Xml.XmlWriter w) { w.WriteRaw(xml); return; }
            // fallback without the private writer: rebuild the node with Scribe's public calls (elements, attributes, text leaves)
            var doc = new System.Xml.XmlDocument(); doc.LoadXml(xml);
            WriteNode(doc.DocumentElement);
        }

        static void WriteNode(System.Xml.XmlElement e)
        {
            bool hasChildElements = false;
            foreach (System.Xml.XmlNode c in e.ChildNodes) if (c is System.Xml.XmlElement) { hasChildElements = true; break; }
            if (!hasChildElements && e.Attributes.Count == 0) { Scribe.saver.WriteElement(e.Name, e.InnerText); return; }
            Scribe.saver.EnterNode(e.Name);
            try
            {
                foreach (System.Xml.XmlAttribute a in e.Attributes) Scribe.saver.WriteAttribute(a.Name, a.Value);
                foreach (System.Xml.XmlNode c in e.ChildNodes) if (c is System.Xml.XmlElement ce) WriteNode(ce);
            }
            finally { Scribe.saver.ExitNode(); }
        }
    }

    /// <summary>
    /// Component classes that are never written into a save: the legacy components of mods (<see cref="KitRegistry.LegacyComponent"/>,
    /// read from old saves, then dropped) and those removed by <see cref="KitRemoval.RemoveComponents"/> (for the rest of the session).
    /// While the game, the world or a map writes its components, instances of these types are taken out; after a load finished they are
    /// dropped from the running game, so they neither tick nor come back.
    /// </summary>
    internal static class KitComponentStrip
    {
        static readonly HashSet<Type> types = new HashSet<Type>();
        internal static bool Any { get { lock (types) return types.Count > 0; } }
        internal static void Add(Type t) { if (t != null) lock (types) types.Add(t); }
        internal static bool Has(Type t) { if (t == null) return false; lock (types) return types.Contains(t); }
        internal static bool Stripped(object c) => c != null && Has(c.GetType());

        /// <summary>Removes the stripped instances from the running game, world and maps. Returns the type names removed.</summary>
        internal static List<string> DropNow()
        {
            var names = new List<string>();
            if (!Any) return names;
            try
            {
                var g = Current.Game;
                if (g == null) return names;
                Drop(g.components, names);
                if (Find.World != null) Drop(Find.World.components, names);
                foreach (var m in g.Maps) Drop(m.components, names);
            }
            catch (Exception e) { Framework.Self?.Log.Warn("Core", "Dropping legacy components failed: " + e.Message); }
            return names;
        }

        static void Drop<T>(List<T> list, List<string> names)
        {
            if (list == null) return;
            foreach (var c in list.Where(x => Stripped(x)).ToList()) { names.Add(c.GetType().FullName); list.Remove(c); }
        }

        static bool pending;
        /// <summary>
        /// Drops the instances on the next frame (FrameworkGame.GameComponentUpdate): never inside vanilla's FinalizeInit/LoadedGame loops,
        /// where removing a component would make the loop skip the next one.
        /// </summary>
        internal static void DropSoon() { if (Any) pending = true; }

        /// <summary>Called every frame by the framework's game component: runs a pending drop.</summary>
        internal static void DropPending()
        {
            if (!pending) return;
            pending = false;
            var n = DropNow();
            if (n.Count > 0) Framework.Self?.Log.Info("Core", "Legacy/removed components dropped from the running game: " + string.Join(", ", n.Distinct()));
        }

        internal static void Strip<T>(List<T> list)
        {
            if (list == null || Scribe.mode != LoadSaveMode.Saving || !Any) return;
            list.RemoveAll(c => Stripped(c));
        }
    }

    [HarmonyPatch(typeof(Game), "ExposeSmallComponents")]
    static class Patch_KitStrip_Game
    {
        static void Prefix(Game __instance) => KitComponentStrip.Strip(__instance?.components);
        // FillComponents may make a fresh copy while the list is written: drop it again
        static void Finalizer(Game __instance) => KitComponentStrip.Strip(__instance?.components);
    }

    [HarmonyPatch(typeof(World), nameof(World.ExposeComponents))]
    static class Patch_KitStrip_World
    {
        static void Prefix(World __instance) => KitComponentStrip.Strip(__instance?.components);
        static void Finalizer(World __instance) => KitComponentStrip.Strip(__instance?.components);
    }

    [HarmonyPatch(typeof(Map), "ExposeComponents")]
    static class Patch_KitStrip_Map
    {
        static void Prefix(Map __instance)
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars) FrameworkMap.ResetOnce(__instance);   // before legacy components are read
            KitComponentStrip.Strip(__instance?.components);
        }
        static void Finalizer(Map __instance) => KitComponentStrip.Strip(__instance?.components);
    }
}
