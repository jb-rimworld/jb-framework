// SPDX-License-Identifier: MIT
// Per-save named random streams, lifted from Hearth & Harbor Foundation/HearthGame.cs (JRand, JRandState) and made multi-tenant:
// every stream name is namespaced by the owning mod, the state lives in the framework's one GameComponent (FrameworkGame, node
// <framework><random>), and nothing here references a mod's types (removing a mod leaves only a few plain numbers in the save).
using System;
using System.Collections.Generic;
using Verse;

namespace JBFramework
{
    /// <summary>
    /// Deterministic, save-stable random numbers for mods. <see cref="Stream(KitMod, string)"/> returns a named stream of the running save: the same
    /// name gives the same sequence after a reload (no "save scumming" a roll) and on every machine (multiplayer-safe), and streams are
    /// independent of each other (drawing from "casino.slots" never shifts "events.weather"). The seed comes from the world seed, so
    /// two colonies on the same seed see the same rolls; the position of every stream is stored in the save.
    /// <para>Without a running game (main menu, tests) <see cref="Stream(KitMod, string)"/> returns a non-persistent stream seeded from the clock.</para>
    /// <para><see cref="Vanilla()"/> is the opposite: a throw-away stream fed from Verse.Rand, which re-rolls on reload exactly like
    /// vanilla randomness. Use it for things that should feel like vanilla (and must follow Verse.Rand's push/pop seeding).</para>
    /// </summary>
    public static class KitRandom
    {
        /// <summary>
        /// The named per-save stream of mod <paramref name="owner"/> (any short name, e.g. "lottery.draw"). Valid everywhere a game exists:
        /// world and map generation, OnGameStarted/OnGameLoaded, ticks, UI. Fetch it where you draw (it is a dictionary lookup) instead of
        /// caching it in a field: after a load or a new game the framework hands out new instances, and an old one would no longer be saved.
        /// </summary>
        public static KitRandomStream Stream(KitMod owner, string name) => StreamFor(Key(owner, name), KitRandomAlgorithm.SplitMix64);

        /// <summary>Like <see cref="Stream(KitMod, string)"/> with another generator (the stream's numbers depend on it).</summary>
        public static KitRandomStream Stream(KitMod owner, string name, KitRandomAlgorithm algorithm) => StreamFor(Key(owner, name), algorithm);

        /// <summary>
        /// A per-save stream of the framework itself, for code that has no <see cref="KitMod"/> (another developer's mod that does not use
        /// the settings part). Prefix the name with your packageId ("my.mod/loot") so it never collides with another mod's stream.
        /// </summary>
        public static KitRandomStream Stream(string globalName) => StreamFor("global/" + (globalName ?? ""), KitRandomAlgorithm.SplitMix64);

        /// <summary>Like <see cref="Stream(string)"/> with another generator.</summary>
        public static KitRandomStream Stream(string globalName, KitRandomAlgorithm algorithm) => StreamFor("global/" + (globalName ?? ""), algorithm);

        /// <summary>
        /// A stream fed from Verse.Rand (re-rolls on reload like vanilla). One shared instance, re-seeded on every call without allocation:
        /// do not keep the reference across calls.
        /// </summary>
        public static KitRandomStream Vanilla() => Vanilla(KitRandomAlgorithm.SplitMix64);

        /// <summary>Like <see cref="Vanilla()"/> with another generator.</summary>
        public static KitRandomStream Vanilla(KitRandomAlgorithm algorithm)
        {
            var v = vanilla ?? (vanilla = new KitRandomStream(1, 7));
            v.Reseed((ulong)(uint)Rand.Int ^ ((ulong)(uint)Rand.Int << 32), 7, 0, algorithm);
            return v;
        }

        /// <summary>The seed of the running save's streams (0 without a game).</summary>
        public static ulong SaveSeed => KitRandomState.Live?.EnsureSeed() ?? 0;

        /// <summary>True while a game runs, i.e. <see cref="Stream(KitMod, string)"/> returns persistent streams.</summary>
        public static bool Persistent => KitRandomState.Live != null;

        // internal -------------------------------------------------------------------------------------------------------
        static KitRandomStream vanilla;
        static readonly Dictionary<string, KitRandomStream> fallback = new Dictionary<string, KitRandomStream>();
        static ulong fallbackSeed;

        internal static string Key(KitMod owner, string name)
            => (owner == null ? "global" : FrameworkGame.SaveKey(owner)) + "/" + (name ?? "");

        static KitRandomStream StreamFor(string key, KitRandomAlgorithm algo)
        {
            var st = KitRandomState.Live;
            if (st != null) return st.Get(key, algo);
            if (fallbackSeed == 0) fallbackSeed = KitRandomStream.Mix((ulong)DateTime.Now.Ticks) | 1UL;
            if (!fallback.TryGetValue(key, out var s)) { s = new KitRandomStream(fallbackSeed, key); fallback[key] = s; }
            s.Algorithm = algo;
            return s;
        }
    }

    /// <summary>The per-save state: seed and the position of every stream that was used (hooked into FrameworkGame).</summary>
    internal sealed class KitRandomState
    {
        static KitRandomState current;
        Game game;
        public ulong Seed;
        Dictionary<string, ulong> counters = new Dictionary<string, ulong>();
        readonly Dictionary<string, KitRandomStream> live = new Dictionary<string, KitRandomStream>();
        List<string> tk; List<ulong> tv;

        static KitRandomState()
        {
            KitHooks.GameReset.Add(() => { current = null; });   // Current.Game is not the new game yet: Live creates it on demand
            KitHooks.GameExpose.Add(() => Ensure()?.Expose());
        }

        /// <summary>Forces the static constructor (hook registration) once at startup.</summary>
        internal static void Install() { }

        /// <summary>The state of the running game, or null (no game).</summary>
        internal static KitRandomState Live
        {
            get
            {
                try
                {
                    var g = Verse.Current.Game;
                    if (g == null) return null;
                    if (current == null || !ReferenceEquals(current.game, g)) current = new KitRandomState { game = g };
                    return current;
                }
                catch { return null; }
            }
        }

        static KitRandomState Ensure() => Live ?? current;

        internal IEnumerable<KeyValuePair<string, ulong>> Positions()
        {
            var all = new Dictionary<string, ulong>(counters);
            foreach (var kv in live) all[kv.Key] = kv.Value.Counter;
            return all;
        }

        /// <summary>The seed, set from the world on first use.</summary>
        internal ulong EnsureSeed() { if (Seed == 0) Seed = SeedFromWorld(); return Seed; }

        internal KitRandomStream Get(string key, KitRandomAlgorithm algo)
        {
            // map generation runs before the component is set up: seed lazily from the world seed instead of caching seed 0
            if (Seed == 0) Seed = SeedFromWorld();
            if (!live.TryGetValue(key, out var s))
            {
                ulong c = counters.TryGetValue(key, out var v) ? v : 0;
                s = new KitRandomStream(Seed, KitRandomStream.HashString(key), c, algo);
                live[key] = s;
            }
            s.Algorithm = algo;
            return s;
        }

        /// <summary>
        /// The save's seed: from the world seed (also while the world is being generated), so two colonies on the same world seed see
        /// the same rolls. Without any world seed yet, a random seed from the clock (stored in the save, so stable from then on): never a
        /// constant, which would give every colony the same rolls.
        /// </summary>
        static ulong SeedFromWorld()
        {
            string seed = null;
            try { seed = Find.World?.info?.seedString; } catch { }
            if (!Usable(seed)) { try { seed = Verse.Current.CreatingWorld?.info?.seedString; } catch { } }
            ulong h = Usable(seed) ? KitRandomStream.HashString(seed) : KitRandomStream.Mix((ulong)DateTime.UtcNow.Ticks ^ (ulong)Environment.TickCount);
            return KitRandomStream.Mix(h ^ 0x4A42_4652_414E_4400UL) | 1UL;   // never 0 (0 means "not seeded yet")
        }

        static bool Usable(string seed) => !string.IsNullOrEmpty(seed) && seed != "SeedError";

        void Expose()
        {
            if (Scribe.EnterNode("random"))
            {
                try
                {
                    if (Scribe.mode == LoadSaveMode.Saving) foreach (var kv in live) counters[kv.Key] = kv.Value.Counter;
                    Scribe_Values.Look(ref Seed, "seed");
                    Scribe_Collections.Look(ref counters, "streams", LookMode.Value, LookMode.Value, ref tk, ref tv);
                    if (Scribe.mode == LoadSaveMode.LoadingVars) live.Clear();
                    if (Scribe.mode == LoadSaveMode.PostLoadInit && counters == null) counters = new Dictionary<string, ulong>();
                }
                finally { Scribe.ExitNode(); }
            }
            else if (Scribe.mode == LoadSaveMode.PostLoadInit && counters == null) counters = new Dictionary<string, ulong>();
        }
    }

    /// <summary>
    /// A fair "deck" of <see cref="Count"/> indices: every index comes up exactly once per round, in a shuffled order, then the deck is
    /// reshuffled (no long droughts, no streaks). Save it inside your own data (<c>Scribe_Deep.Look(ref bag, "bag")</c>); give it the
    /// stream to draw with, usually <see cref="KitRandom.Stream(KitMod, string)"/>.
    /// </summary>
    public sealed class KitShuffleBag : IExposable
    {
        int count;
        List<int> left = new List<int>();

        /// <summary>For the save loader.</summary>
        public KitShuffleBag() { }
        /// <summary>A bag of the indices 0 … <paramref name="count"/>-1.</summary>
        public KitShuffleBag(int count) { this.count = Math.Max(0, count); }

        /// <summary>Number of indices in a full round.</summary>
        public int Count => count;
        /// <summary>Indices still to come in this round.</summary>
        public int Remaining => left?.Count ?? 0;

        /// <summary>
        /// The next index (0 … Count-1), or -1 when Count is 0. Changing the size with <see cref="Resize"/> starts a new round.
        /// </summary>
        public int Next(KitRandomStream rng)
        {
            if (count <= 0) return -1;
            if (left == null) left = new List<int>();
            if (left.Count == 0)
            {
                for (int i = 0; i < count; i++) left.Add(i);
                if (rng != null) rng.Shuffle(left);
            }
            int last = left.Count - 1, v = left[last];
            left.RemoveAt(last);
            return v;
        }

        /// <summary>Changes the number of indices and starts a new round.</summary>
        public void Resize(int newCount)
        {
            count = Math.Max(0, newCount);
            left?.Clear();
        }

        /// <summary>Saves and loads the round.</summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref count, "count");
            Scribe_Collections.Look(ref left, "left", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (left == null) left = new List<int>();
                left.RemoveAll(i => i < 0 || i >= count);
            }
        }
    }
}
