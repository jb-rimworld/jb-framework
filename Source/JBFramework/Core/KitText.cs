// SPDX-License-Identifier: MIT
using Verse;

namespace JBFramework
{
    /// <summary>
    /// Translation helper. The framework's own texts exist once (Common/Languages/&lt;Lang&gt;/Keyed/JBFramework.xml, keys
    /// <c>JBF.&lt;Area&gt;.&lt;Key&gt;</c>), with the English fallback in code so a missing language file never shows a raw key.
    /// A mod's own registration labels use the mod's prefix (<see cref="KitMod.Key"/>).
    /// </summary>
    internal static class KitText
    {
        /// <summary>Prefix of the framework's own keys.</summary>
        public const string Prefix = "JBF";

        /// <summary>Translate if the key exists, otherwise return the English fallback.</summary>
        public static string TranslateOr(this string key, string fallback)
        {
            try { if (!string.IsNullOrEmpty(key) && key.CanTranslate()) return key.Translate().ToString(); }
            catch { /* translator not ready (very early startup): fall back */ }
            return fallback;
        }

        /// <summary>Framework string: key JBF.&lt;sub&gt;, English fallback.</summary>
        public static string K(string sub, string fallbackEn) => (Prefix + "." + sub).TranslateOr(fallbackEn);

        /// <summary>Framework string with {0}, {1} … placeholders filled (string.Format on the translated text, never throws).</summary>
        public static string K(string sub, string fallbackEn, params object[] args)
        {
            string s = K(sub, fallbackEn);
            try { return string.Format(s, args); }
            catch { try { return string.Format(fallbackEn, args); } catch { return fallbackEn; } }
        }
    }
}
