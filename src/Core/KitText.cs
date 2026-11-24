// SPDX-License-Identifier: MIT
using Verse;

namespace JBKit
{
    /// <summary>
    /// Translation helper. Every player-facing kit string has an English fallback in code, so a missing language file
    /// never shows a raw key. Kit keys always carry the mod prefix: <c>&lt;Prefix&gt;.Kit.&lt;Key&gt;</c>.
    /// </summary>
    internal static class KitText
    {
        /// <summary>Translate if the key exists, otherwise return the English fallback.</summary>
        public static string TranslateOr(this string key, string fallback)
        {
            try { if (!string.IsNullOrEmpty(key) && key.CanTranslate()) return key.Translate().ToString(); }
            catch { /* translator not ready (very early startup): fall back */ }
            return fallback;
        }

        /// <summary>The mod's key prefix ("LO"); "JBKit" before the mod constructor ran.</summary>
        public static string Prefix => Kit.Info?.KeyPrefix ?? "JBKit";

        /// <summary>Full key for a mod key: Key("Setting.x") → "LO.Setting.x".</summary>
        public static string Key(string suffix) => Prefix + "." + suffix;

        /// <summary>Kit string: key &lt;Prefix&gt;.Kit.&lt;sub&gt;, English fallback.</summary>
        public static string K(string sub, string fallbackEn) => (Prefix + ".Kit." + sub).TranslateOr(fallbackEn);

        /// <summary>Kit string with {0}, {1} … placeholders filled (plain string.Format semantics on the translated text, never throws).</summary>
        public static string K(string sub, string fallbackEn, params object[] args)
        {
            string s = K(sub, fallbackEn);
            try { return string.Format(s, args); }
            catch { try { return string.Format(fallbackEn, args); } catch { return fallbackEn; } }
        }
    }
}
