// SPDX-License-Identifier: MIT
using System;

namespace JBFramework
{
    /// <summary>
    /// Marks a field of a saved object (component, comp, SaveData class) as a cache that is rebuilt after a load by design: the in-game
    /// save test (kit.saveload's fingerprint) does not compare it. No effect at runtime. <c>[KitTransient("rebuilt from the map on load")] int cachedTick;</c>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public sealed class KitTransientAttribute : Attribute
    {
        /// <summary>Marks the field; <paramref name="why"/> says why it is not saved (shown in the test report).</summary>
        public KitTransientAttribute(string why) { Why = why; }
        /// <summary>Why the field is not part of the saved state.</summary>
        public string Why { get; }
    }
}
