// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.IO;
using Verse;

namespace JBFramework
{
    /// <summary>
    /// Base of a mod's ModSettings class. RimWorld finds the settings file by the Mod class name and the settings object by this class's
    /// full name, so an existing mod KEEPS both names and only changes the base class: <c>public sealed class LOSettings : KitSettingsBase { }</c>.
    /// Settings files stay per mod. File layout: one &lt;kit&gt; node (the template profile: values and section switches), then whatever
    /// <see cref="ExposeExtra"/> writes. Legacy migration (one-way): on load, every registration with a legacy field name that the
    /// template does not hold yet is read from the old flat node as a string, parsed, clamped and stored; the first time, the old file
    /// is copied once to *.xml.pre-kit and every migrated key is logged. Legacy nodes are never written again.
    /// </summary>
    public abstract class KitSettingsBase : ModSettings
    {
        internal KitMod Owner;
        internal KitProfile Template = new KitProfile();
        internal int KitDataVersion = 1;
        /// <summary>Keys migrated from legacy fields during this session's load ("key=value (from old)").</summary>
        internal readonly List<string> Migrated = new List<string>();
        internal bool MigratedThisSession => Migrated.Count > 0;

        /// <summary>The mod's context (null only before the mod was created).</summary>
        public KitMod Kit => Owner ?? (Owner = Framework.ForAssembly(GetType().Assembly));

        /// <summary>Hook for the mod's remaining own fields (bill templates, lists …). Use Scribe_* as usual; do not use the node name "kit".</summary>
        protected virtual void ExposeExtra() { }

        /// <summary>Reads and writes the &lt;kit&gt; node, migrates legacy fields once, then calls <see cref="ExposeExtra"/>. Fenced.</summary>
        public sealed override void ExposeData()
        {
            var k = Kit;
            if (k != null && k.Blocked) return;
            if (Scribe.EnterNode("kit"))
            {
                try
                {
                    Scribe_Values.Look(ref KitDataVersion, "dataVersion", 1);
                    Scribe_Deep.Look(ref Template, "template");
                }
                finally { Scribe.ExitNode(); }
            }
            if (Template == null) Template = new KitProfile();
            Template.Owner = k;
            if (Scribe.mode == LoadSaveMode.LoadingVars && k != null) MigrateLegacy(k);
            try { ExposeExtra(); }
            catch (Exception e)
            {
                if (k != null) k.Log.Error("Core", "ExposeExtra failed (" + Scribe.mode + ")", e);
                else Log.Error("[" + Framework.Name + "] ExposeExtra of " + GetType().FullName + " failed: " + e);
            }
        }

        void MigrateLegacy(KitMod k)
        {
            foreach (var s in k.SettingsReg.All)
            {
                if (string.IsNullOrEmpty(s.LegacyScribe) || Template.Store.Has(s.Key)) continue;
                string raw = null;
                try { Scribe_Values.Look(ref raw, s.LegacyScribe); }
                catch (Exception e) { k.Log.Warn("Migration", "Could not read old field " + s.LegacyScribe + ": " + e.Message); continue; }
                if (raw == null) continue;
                if (s.TryParseLegacy(raw, out float v, s.LegacyScribeConvert == null))
                {
                    if (s.LegacyScribeConvert != null)
                    {
                        try { v = s.Clamp(s.LegacyScribeConvert(v)); }
                        catch (Exception e) { k.Log.Warn("Migration", "Converting the old value of " + s.LegacyScribe + " threw (" + e.Message + "); " + s.Key + " keeps its default"); continue; }
                    }
                    Template.Store.Set(s.Key, v); Migrated.Add(s.Key + "=" + s.Format(v) + " (from " + s.LegacyScribe + ")");
                }
                else k.Log.Warn("Migration", "Old value of " + s.LegacyScribe + " ('" + raw + "') could not be read; " + s.Key + " keeps its default");
            }
            foreach (var sec in k.Sections.All)
            {
                if (string.IsNullOrEmpty(sec.LegacyEnabledField) || Template.SectionStored(sec.Id)) continue;
                string raw = null;
                try { Scribe_Values.Look(ref raw, sec.LegacyEnabledField); } catch { continue; }
                if (raw == null) continue;
                if (bool.TryParse(raw.Trim(), out bool on)) { on ^= sec.LegacyEnabledInverted; Template.sections[sec.Id] = on; Migrated.Add("section:" + sec.Id + "=" + on + " (from " + (sec.LegacyEnabledInverted ? "not " : "") + sec.LegacyEnabledField + ")"); }
            }
            if (Migrated.Count == 0) return;
            BackupOnce(k);
            k.Log.Info("Migration", "Migrated " + Migrated.Count + " old settings: " + string.Join(", ", Migrated));
        }

        /// <summary>Copies the current settings file to *.pre-kit once (never overwrites an existing backup).</summary>
        static void BackupOnce(KitMod k)
        {
            try
            {
                string file = SettingsFilePath(k);
                if (file == null || !File.Exists(file)) return;
                string bak = file + ".pre-kit";
                if (File.Exists(bak)) return;
                File.Copy(file, bak);
                k.Log.Info("Migration", "Backup of the old settings file: " + Path.GetFileName(bak));
            }
            catch (Exception e) { k.Log.Warn("Migration", "Could not back up the old settings file: " + e.Message); }
        }

        /// <summary>Path of a mod's settings file (vanilla rule: Config/Mod_&lt;folder&gt;_&lt;Mod class name&gt;.xml). Never print it.</summary>
        internal static string SettingsFilePath(KitMod k)
        {
            var mod = k?.Mod;
            if (mod?.Content == null) return null;
            return Path.Combine(GenFilePaths.ConfigFolderPath, GenText.SanitizeFilename("Mod_" + mod.Content.FolderName + "_" + mod.GetType().Name + ".xml"));
        }
    }
}
