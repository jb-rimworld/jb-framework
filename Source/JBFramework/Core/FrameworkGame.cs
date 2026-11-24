// SPDX-License-Identifier: MIT
// Reduced from Hearth & Harbor Foundation/HearthGame.cs; one component for every mod that uses the framework.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Verse;

namespace JBFramework
{
    /// <summary>
    /// The framework's one GameComponent. It stores every dependent mod's per-save profile (plain values, keyed by packageId) and the
    /// data of mods that registered <see cref="KitRegistry.SaveData(System.Action)"/>, drives ticks, caretaker checks, deferred change delivery and
    /// welcome cards. A removed mod's profile is kept silently (plain values need none of its types); its own SaveData node is
    /// kept as raw XML (written back unchanged) without touching its types. Created by RimWorld; mods never use it directly.
    /// </summary>
    public sealed class FrameworkGame : GameComponent
    {
        static FrameworkGame instance;
        readonly Game game;
        List<KitSaveEntry> mods = new List<KitSaveEntry>();
        bool exposed;

        /// <summary>Created by RimWorld for every game (new or loaded).</summary>
        public FrameworkGame(Game game)
        {
            this.game = game;
            instance = this;
            Framework.ForEach(k =>
            {
                k.AttachSave(null, null);
                if (k.SaveReset != null && !k.Blocked) k.Guard.Run(KitMod.Core, "SaveData.reset", k.SaveReset);
            }, "FrameworkGame.ctor");
            KitHooks.Each(KitHooks.GameReset, null, "GameReset", h => h());
        }

        /// <summary>The component of the running game, or null (main menu, other game).</summary>
        internal static FrameworkGame Instance
        {
            get
            {
                var i = instance;
                if (i == null) return null;
                try { return Current.Game != null && ReferenceEquals(i.game, Current.Game) ? i : null; } catch { return null; }
            }
        }

        /// <summary>Save key of a mod: packageId without the Workshop "_steam" postfix and without a test-slot suffix (".slotN").</summary>
        internal static string SaveKey(KitMod k) => Regex.Replace(KitCompat.Strip(k.PackageId).ToLowerInvariant(), @"\.slot\d+$", "");

        KitSaveEntry EntryFor(KitMod k, bool create)
        {
            string key = SaveKey(k);
            var e = mods.FirstOrDefault(x => x.id == key);
            if (e == null && create) { e = new KitSaveEntry { id = key }; mods.Add(e); }
            return e;
        }

        /// <summary>Profiles of mods that are not loaded right now (kept for when they come back).</summary>
        internal IEnumerable<string> AbsentMods => mods.Where(m => !Framework.Mods.Any(k => SaveKey(k) == m.id)).Select(m => m.id);

        void Init(bool newGame)
        {
            instance = this;
            foreach (var k in Framework.Mods)
            {
                var kk = k;
                try
                {
                    if (kk.Blocked) continue;
                    var e = EntryFor(kk, false);
                    bool joined = !newGame && (!exposed || e == null);
                    if (e == null) e = EntryFor(kk, true);
                    if (kk.Cfg.PerSave)
                    {
                        if (e.profile == null || newGame)
                        {
                            e.profile = kk.Template.CloneForNewGame();
                            kk.Log.Info("Core", "Per-save settings copied from the template (" + (newGame ? "new game" : joined ? "mod joined an existing save" : "save had no profile") + ")");
                        }
                        kk.AttachSave(game, e.profile);
                    }
                    else kk.AttachSave(game, null);
                    if (joined) kk.Log.Info("Core", "Mod added to an existing save");
                    kk.Window.JoinedExistingSave = joined;
                    if (newGame) kk.Guard.Run(KitMod.Core, "OnGameStarted", () => kk.Mod?.CallOnGameStarted());
                    else kk.Guard.Run(KitMod.Core, "OnGameLoaded", () => kk.Mod?.CallOnGameLoaded(joined));
                    if (!newGame) kk.Caretaker.RunAll();
                    // values of the save may differ from the template the mod saw before: let the mod re-apply everything once
                    kk.MarkChanged("profile:loaded");
                }
                catch (Exception ex) { try { kk.Guard.Fail(KitMod.Core, "GameInit", ex); } catch { } }
            }
            KitFirstStartCard.ResetForGame();
            KitComponentStrip.DropSoon();   // legacy components were read by now; they never tick on and never reach a save
        }

        /// <summary>A new game: every per-save mod gets a copy of its template.</summary>
        public override void StartedNewGame() => Init(true);

        /// <summary>A loaded game: profiles from the save; mods without one get a copy of their template ("joined").</summary>
        public override void LoadedGame() => Init(false);

        /// <summary>Remembers the running game.</summary>
        public override void FinalizeInit() { instance = this; }

        /// <summary>Caretaker checks and each mod's OnTick, every mod in its own fence.</summary>
        public override void GameComponentTick()
        {
            int t;
            try { t = Find.TickManager.TicksGame; } catch { return; }
            foreach (var k in Framework.Mods)
            {
                if (k.Blocked) continue;
                try
                {
                    k.Caretaker.Tick(t);
                    k.Mod?.CallOnTick(t);
                }
                catch (Exception e) { try { k.Guard.Fail(KitMod.Core, "Tick", e); } catch { } }
            }
        }

        /// <summary>Delivers pending setting changes of every mod.</summary>
        public override void GameComponentUpdate()
        {
            try { KitComponentStrip.DropPending(); } catch (Exception e) { Framework.Self?.Guard.Fail(KitMod.Core, "DropComponents", e); }
            foreach (var k in Framework.AllContexts())
            {
                if (!k.HasPendingChanges) continue;
                try { k.FlushChanges(); } catch (Exception e) { try { k.Guard.Fail(KitMod.Core, "Flush", e); } catch { } }
            }
        }

        /// <summary>Welcome cards.</summary>
        public override void GameComponentOnGUI()
        {
            try { KitFirstStartCard.OnGUI(); }
            catch (Exception e) { Framework.Self?.Guard.Fail(KitMod.Core, "WelcomeCard", e); }
        }

        /// <summary>Saves and loads every mod's entry (plain values; each mod's SaveData in its own node).</summary>
        public override void ExposeData()
        {
            exposed = true;
            Scribe_Collections.Look(ref mods, "mods", LookMode.Deep);
            if (KitHooks.GameExpose.Count > 0 && Scribe.EnterNode("framework"))
            {
                try { KitHooks.Each(KitHooks.GameExpose, null, "GameExpose." + Scribe.mode, h => h()); }
                finally { Scribe.ExitNode(); }
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (mods == null) mods = new List<KitSaveEntry>();
                mods.RemoveAll(m => m == null || string.IsNullOrEmpty(m.id));
            }
        }
    }

    /// <summary>
    /// One mod's part of a save: its profile and, inside &lt;data&gt;, what its SaveData callback writes. When the mod is absent or
    /// blocked (framework too old), its &lt;data&gt; node is kept as raw XML and written back unchanged on the next save, so the mod finds
    /// its values again when it returns; none of its types are touched.
    /// </summary>
    internal sealed class KitSaveEntry : IExposable
    {
        public string id;
        public KitProfile profile;
        /// <summary>The &lt;data&gt; node of an absent or blocked mod, as loaded (outer XML), or null.</summary>
        internal string rawData;

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Deep.Look(ref profile, "profile");
            var k = string.IsNullOrEmpty(id) ? null : Framework.Mods.FirstOrDefault(x => FrameworkGame.SaveKey(x) == id);
            if (profile != null && k != null) profile.Owner = k;
            bool live = k != null && !k.Blocked && k.SaveExpose != null;
            if (!live)
            {
                rawData = KitRawXml.Keep(rawData, "save data of absent mod " + id);
                return;
            }
            rawData = null;
            if (Scribe.EnterNode("data"))
            {
                try { k.Guard.Run(KitMod.Core, "SaveData." + Scribe.mode, k.SaveExpose); }
                finally { Scribe.ExitNode(); }
            }
        }


    }
}
