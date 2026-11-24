// SPDX-License-Identifier: MIT
// Lifted from Hearth & Harbor Hearth.Core/Pure/JRandCore.cs (RngStream). Pure code: no game types, safe to use from any thread
// as long as one stream is not shared between threads. Value n of a stream is a pure function of (seed, stream id, n, algorithm),
// so results do not depend on the order in which systems draw numbers, and they are identical on every machine.
using System;
using System.Collections.Generic;

namespace JBFramework
{
    /// <summary>The generator a <see cref="KitRandomStream"/> uses. The numeric values are stored in saves: never renumber.</summary>
    public enum KitRandomAlgorithm
    {
        /// <summary>SplitMix64 finaliser over (seed, stream, counter). Fast, the default.</summary>
        SplitMix64 = 0,
        /// <summary>Two PCG32 outputs per value.</summary>
        Pcg32 = 1,
        /// <summary>Xoshiro-style mix of four SplitMix64 words.</summary>
        Xoshiro256 = 2,
        /// <summary>ChaCha with 8 rounds over (seed, stream, counter): slowest, strongest mixing.</summary>
        ChaCha8 = 3,
    }

    /// <summary>
    /// A counter-based deterministic random stream: value number n is a pure function of (<see cref="Seed"/>, <see cref="StreamId"/>, n).
    /// Two streams with the same seed, id and counter produce the same numbers on every machine and after every reload, which makes
    /// results reproducible and multiplayer-safe. Get per-save named streams from <see cref="KitRandom.Stream(KitMod, string, KitRandomAlgorithm)"/>; create your own with
    /// the constructor (tests, previews, world generation).
    /// </summary>
    public sealed class KitRandomStream
    {
        /// <summary>A stream. <paramref name="counter"/> is the number of values already drawn (0 = from the start).</summary>
        public KitRandomStream(ulong seed, ulong streamId, ulong counter = 0, KitRandomAlgorithm algorithm = KitRandomAlgorithm.SplitMix64)
        {
            Seed = seed; StreamId = streamId; Counter = counter; Algorithm = algorithm;
        }

        /// <summary>A stream whose id is the stable hash of <paramref name="name"/> (<see cref="HashString"/>).</summary>
        public KitRandomStream(ulong seed, string name, ulong counter = 0, KitRandomAlgorithm algorithm = KitRandomAlgorithm.SplitMix64)
            : this(seed, HashString(name), counter, algorithm) { }

        /// <summary>The seed (per save for <see cref="KitRandom"/> streams).</summary>
        public ulong Seed { get; private set; }
        /// <summary>The stream id (hash of the stream name for named streams).</summary>
        public ulong StreamId { get; private set; }
        /// <summary>How many values were drawn so far; the next value is number <see cref="Counter"/>. Settable to rewind or skip.</summary>
        public ulong Counter { get; set; }
        /// <summary>The generator.</summary>
        public KitRandomAlgorithm Algorithm { get; set; }

        /// <summary>Re-points this instance at another stream (avoids allocations for throw-away streams).</summary>
        public void Reseed(ulong seed, ulong streamId, ulong counter, KitRandomAlgorithm algorithm)
        {
            Seed = seed; StreamId = streamId; Counter = counter; Algorithm = algorithm;
        }

        /// <summary>A copy at the same position (drawing from the copy does not move this stream).</summary>
        public KitRandomStream Clone() => new KitRandomStream(Seed, StreamId, Counter, Algorithm);

        // ---------------------------------------------------------------------------------------------------- stable hashing
        /// <summary>SplitMix64 finaliser: a strong 64-bit mix of <paramref name="z"/>.</summary>
        public static ulong Mix(ulong z)
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>FNV-1a 64 of the UTF-16 code units: stable across platforms, runtimes and runs (string.GetHashCode is not).</summary>
        public static ulong HashString(string s)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in s ?? "") { h ^= c; h *= 1099511628211UL; }
            return h;
        }

        // ---------------------------------------------------------------------------------------------------- raw values
        /// <summary>The next 64 random bits.</summary>
        public ulong NextULong()
        {
            ulong n = Counter++;
            switch (Algorithm)
            {
                case KitRandomAlgorithm.Pcg32:
                {
                    ulong state = Mix(Seed ^ StreamId) + n * 6364136223846793005UL;
                    ulong a = Pcg(state), b = Pcg(state ^ 0xDA3E39CB94B95BDBUL);
                    return (a << 32) | b;
                }
                case KitRandomAlgorithm.Xoshiro256:
                {
                    ulong s0 = Mix(Seed + n), s1 = Mix(StreamId + n * 0x9E37UL), s2 = Mix(s0 ^ s1), s3 = Mix(s2 + n);
                    ulong r = Rotl(s1 * 5, 7) * 9;
                    return r ^ s3 ^ s0;
                }
                case KitRandomAlgorithm.ChaCha8:
                    return ChaChaLite(Seed, StreamId, n);
                default:
                    return Mix(Seed ^ Mix(StreamId ^ Mix(n)));
            }
        }

        /// <summary>Uniform in [0, 1) with 53 random bits.</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);
        /// <summary>Uniform in [0, 1).</summary>
        public float NextFloat()
        {
            float f = (float)NextDouble();
            return f < 1f ? f : 0.99999994f;   // rounding a double just below 1 to float can give 1
        }
        /// <summary>True with probability <paramref name="p"/> (≤ 0 never, ≥ 1 always; still draws one value for 0 &lt; p &lt; 1 only).</summary>
        public bool Chance(double p) => p >= 1 || (p > 0 && NextDouble() < p);
        /// <summary>Uniform whole number in [<paramref name="min"/>, <paramref name="maxExclusive"/>); <paramref name="min"/> when the range is empty.</summary>
        public int Range(int min, int maxExclusive)
        {
            if (maxExclusive <= min) return min;
            ulong span = (ulong)((long)maxExclusive - min);   // long maths: the span may exceed int.MaxValue
            return (int)(min + (long)(NextULong() % span));
        }
        /// <summary>Uniform whole number in [<paramref name="min"/>, <paramref name="max"/>] (both ends included).</summary>
        public int RangeInclusive(int min, int max) => max <= min ? min : (int)(min + (long)(NextULong() % (ulong)((long)max - min + 1)));
        /// <summary>Uniform in [<paramref name="min"/>, <paramref name="max"/>).</summary>
        public double Range(double min, double max) => min + (max - min) * NextDouble();
        /// <summary>Normal distribution (Box-Muller; draws two values).</summary>
        public double Gaussian(double mean = 0, double sd = 1)
        {
            double u1 = Math.Max(1e-12, NextDouble()), u2 = NextDouble();
            return mean + sd * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }
        /// <summary>Poisson distribution with mean <paramref name="lambda"/> (normal approximation above 30).</summary>
        public int Poisson(double lambda)
        {
            if (lambda <= 0) return 0;
            if (lambda > 30) return Math.Max(0, (int)Math.Round(Gaussian(lambda, Math.Sqrt(lambda))));
            double l = Math.Exp(-lambda), p = 1; int k = 0;
            do { k++; p *= NextDouble(); } while (p > l);
            return k - 1;
        }

        // ---------------------------------------------------------------------------------------------------- collections
        /// <summary>Fair in-place shuffle (Fisher-Yates): every order is equally likely. Draws Count-1 values.</summary>
        public void Shuffle<T>(IList<T> list)
        {
            if (list == null) return;
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Range(0, i + 1);
                if (j != i) { T t = list[i]; list[i] = list[j]; list[j] = t; }
            }
        }

        /// <summary>One element, uniformly; default(T) for an empty or null list (then nothing is drawn).</summary>
        public T Pick<T>(IReadOnlyList<T> list) => list == null || list.Count == 0 ? default : list[Range(0, list.Count)];

        /// <summary>
        /// Index picked with probability weight/sum (negative, NaN and infinite weights count as 0). -1 when no weight is positive
        /// (then nothing is drawn). Draws exactly one value otherwise.
        /// </summary>
        public int WeightedIndex(IReadOnlyList<double> weights)
        {
            if (weights == null) return -1;
            double sum = 0;
            for (int i = 0; i < weights.Count; i++) sum += Clean(weights[i]);
            if (!(sum > 0)) return -1;
            double r = NextDouble() * sum;
            int last = -1;
            for (int i = 0; i < weights.Count; i++)
            {
                double w = Clean(weights[i]);
                if (w <= 0) continue;
                last = i;
                if (r < w) return i;
                r -= w;
            }
            return last;   // floating point remainder: the last positive weight
        }

        /// <summary>Element picked with probability weight/sum; default(T) when no weight is positive. Draws exactly one value otherwise.</summary>
        public T PickWeighted<T>(IReadOnlyList<T> items, Func<T, double> weight)
        {
            if (items == null || weight == null || items.Count == 0) return default;
            var w = new double[items.Count];
            for (int i = 0; i < w.Length; i++) w[i] = weight(items[i]);
            int idx = WeightedIndex(w);
            return idx < 0 ? default : items[idx];
        }

        static double Clean(double w) => double.IsNaN(w) || double.IsInfinity(w) || w < 0 ? 0 : w;

        // ---------------------------------------------------------------------------------------------------- generators
        static ulong Pcg(ulong state)
        {
            uint xorshifted = (uint)(((state >> 18) ^ state) >> 27);
            int rot = (int)(state >> 59);
            return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
        }
        static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

        static uint R(uint x, int n) => (x << n) | (x >> (32 - n));
        static void QR(ref uint a, ref uint b, ref uint c, ref uint d)
        { a += b; d ^= a; d = R(d, 16); c += d; b ^= c; b = R(b, 12); a += b; d ^= a; d = R(d, 8); c += d; b ^= c; b = R(b, 7); }
        [ThreadStatic] static uint[] chaS, chaX;
        static ulong ChaChaLite(ulong seed, ulong stream, ulong n)
        {
            var s = chaS ?? (chaS = new uint[16]);
            var x = chaX ?? (chaX = new uint[16]);
            ulong ms = Mix(seed), mt = Mix(stream);
            s[0] = 0x61707865; s[1] = 0x3320646e; s[2] = 0x79622d32; s[3] = 0x6b206574;
            s[4] = (uint)seed; s[5] = (uint)(seed >> 32); s[6] = (uint)stream; s[7] = (uint)(stream >> 32);
            s[8] = (uint)ms; s[9] = (uint)(ms >> 32); s[10] = (uint)mt; s[11] = (uint)(mt >> 32);
            s[12] = (uint)n; s[13] = (uint)(n >> 32); s[14] = 0x4845_4152; s[15] = 0x5448_0001;
            Array.Copy(s, x, 16);
            for (int i = 0; i < 4; i++)
            {
                QR(ref x[0], ref x[4], ref x[8], ref x[12]); QR(ref x[1], ref x[5], ref x[9], ref x[13]);
                QR(ref x[2], ref x[6], ref x[10], ref x[14]); QR(ref x[3], ref x[7], ref x[11], ref x[15]);
                QR(ref x[0], ref x[5], ref x[10], ref x[15]); QR(ref x[1], ref x[6], ref x[11], ref x[12]);
                QR(ref x[2], ref x[7], ref x[8], ref x[13]); QR(ref x[3], ref x[4], ref x[9], ref x[14]);
            }
            return ((ulong)(x[1] + s[1]) << 32) | (x[0] + s[0]);
        }
    }
}
