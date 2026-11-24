// SPDX-License-Identifier: MIT
// Reduced from Hearth & Harbor Foundation/HearthGame.cs (no ledger, no random streams, no scenario).
using System;
using Verse;

namespace JBKit
{
    /// <summary>
    /// Base of the mod's one GameComponent. Public abstract: never instantiated or written to a save itself; the game creates the mod's
    /// concrete subclass by Type and writes its full name (in the mod's namespace) into the save:
    /// <c>public sealed class LOGame : JBKit.KitGameBase { public LOGame(Game game) : base(game) { } }</c> (the Game constructor is required).
    /// Holds the per-save profile when <see cref="KitInfo.PerSave"/>; drives the caretaker and the deferred change delivery in any case.
    /// A new game copies the template; loading a save without our node ("the mod joined an existing save") copies the template too.
    /// </summary>
    public abstract class KitGameBase : GameComponent
    {
        static KitGameBase instance;
        readonly Game game;
        internal KitProfile Profile;
        /// <summary>True after StartedNewGame in this session.</summary>
        internal bool IsNewGame;
        /// <summary>True when the mod was added to a save that did not have it (component created fresh, ExposeData never ran).</summary>
        internal bool JoinedExistingSave;
        bool exposed;

        protected KitGameBase(Game game)
        {
            this.game = game;
            instance = this;
        }

        /// <summary>This mod's component of the running game, or null (main menu, other game, not declared).</summary>
        internal static KitGameBase Instance
        {
            get
            {
                var i = instance;
                if (i == null) return null;
                try { return ReferenceEquals(i.game, Current.Game) && Current.Game != null ? i : null; } catch { return null; }
            }
        }

        /// <summary>The per-save profile of the running game (null when not per-save or no game).</summary>
        internal static KitProfile CurrentProfile => Instance?.Profile;

        /// <summary>Hook for the mod's own per-save fields. Use Scribe_* as usual; do not reuse the node names "kitProfile"/"kitData".</summary>
        internal virtual void ExposeExtra() { }
        /// <summary>Called every tick (guarded under section core). Prefer your own interval checks.</summary>
        internal virtual void Tick(int ticksGame) { }

        void EnsureProfile(string why)
        {
            if (Kit.Info == null || !Kit.Info.PerSave) { Profile = null; return; }
            if (Profile != null) return;
            Profile = Kit.Template.CloneForNewGame();
            KitLog.Info("Core", "Per-save settings copied from the template (" + why + ")");
        }

        public sealed override void StartedNewGame()
        {
            instance = this;
            Profile = null;
            EnsureProfile("new game");
            IsNewGame = true; exposed = true;
            GameInit(true, false);
        }

        public sealed override void LoadedGame()
        {
            instance = this;
            if (!exposed)
            {
                JoinedExistingSave = true;
                exposed = true;
                KitLog.Info("Core", "Mod added to an existing save");
            }
            EnsureProfile(JoinedExistingSave ? "mod joined an existing save" : "save had no profile");
            GameInit(false, JoinedExistingSave);
            KitCaretaker.RunAll();
        }

        void GameInit(bool newGame, bool joined)
        {
            if (Kit.Mod != null)
            {
                if (newGame) KitGuard.Run(KitSections.Core, "OnGameStarted", () => Kit.Mod.OnGameStarted());
                else KitGuard.Run(KitSections.Core, "OnGameLoaded", () => Kit.Mod.OnGameLoaded(joined));
            }
            try { KitHooks.GameInit?.Invoke(newGame, joined); } catch (Exception e) { KitLog.Error("Core", "GameInit hook threw", e); }
            // values of the save may differ from the template the mod saw before: let the mod re-apply everything once
            Kit.MarkChanged("profile:loaded");
        }

        public sealed override void FinalizeInit() { instance = this; }

        public sealed override void GameComponentTick()
        {
            int t;
            try { t = Find.TickManager.TicksGame; } catch { return; }
            KitCaretaker.Tick(t);
            try { Tick(t); }
            catch (Exception e) { KitGuard.Fail(KitSections.Core, GetType().Name + ".Tick", e); }
        }

        public sealed override void GameComponentUpdate()
        {
            if (Kit.HasPendingChanges) Kit.FlushChanges();
        }

        public sealed override void GameComponentOnGUI()
        {
            if (KitHooks.GameOnGUI == null) return;
            try { KitHooks.GameOnGUI(); } catch (Exception e) { KitGuard.Fail(KitSections.Core, "GameOnGUI", e); }
        }

        public sealed override void ExposeData()
        {
            exposed = true;
            if (Kit.Info != null && Kit.Info.PerSave) Scribe_Deep.Look(ref Profile, "kitProfile");
            try { ExposeExtra(); }
            catch (Exception e) { KitLog.Error("Core", "ExposeExtra (game) failed (" + Scribe.mode + ")", e); }
            if (Scribe.mode == LoadSaveMode.PostLoadInit && Profile == null && Kit.Info != null && Kit.Info.PerSave)
                EnsureProfile("save had no profile");
        }
    }
}
