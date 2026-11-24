// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.IO;
using Verse;

namespace JBKit
{
    /// <summary>
    /// Base of the mod's ModSettings class. RimWorld finds the settings file by the Mod class name and the settings object by the
    /// settings class's full name (Class attribute in the file), so an adopting mod KEEPS both names and only changes the base class:
    /// <c>public sealed class LOSettings : JBKit.KitSettingsBase { }</c>.
    /// File layout: one &lt;kit&gt; node (template profile = all values and switches, data version), then whatever ExposeExtra writes.
    /// Legacy migration (one-way): on load, every registration with LegacyScribe (and every section with LegacyEnabled) that the template
    /// does not hold yet is read from the old flat node as a string, parsed, clamped and stored; the first time, the old file is copied once
    /// to *.xml.pre-kit and every migrated key is logged. The kit never writes legacy nodes; KitStartup rewrites the file once after a migration.
    /// </summary>
    public abstract class KitSettingsBase : ModSettings
    {
        internal KitProfile Template = new KitProfile();
        internal int KitDataVersion = 1;
        /// <summary>Keys migrated from legacy fields during this session's load ("key=value").</summary>
        internal readonly List<string> Migrated = new List<string>();
        internal bool MigratedThisSession => Migrated.Count > 0;

        /// <summary>Hook for the mod's remaining own fields (bill templates, lists …). Use Scribe_* as usual; do not reuse the node name "kit".</summary>
        internal virtual void ExposeExtra() { }

        public sealed override void ExposeData()
        {
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
            if (Scribe.mode == LoadSaveMode.LoadingVars) MigrateLegacy();
            try { ExposeExtra(); }
            catch (Exception e) { KitLog.Error("Core", "ExposeExtra failed (" + Scribe.mode + ")", e); }
        }

        void MigrateLegacy()
        {
            foreach (var s in KitSettings.All)
            {
                if (string.IsNullOrEmpty(s.LegacyScribe) || Template.Store.Has(s.Key)) continue;
                string raw = null;
                try { Scribe_Values.Look(ref raw, s.LegacyScribe); }
                catch (Exception e) { KitLog.Warn("Migration", "Could not read old field " + s.LegacyScribe + ": " + e.Message); continue; }
                if (raw == null) continue;
                if (s.TryParseLegacy(raw, out float v)) { Template.Store.Set(s.Key, v); Migrated.Add(s.Key + "=" + s.Format(v) + " (from " + s.LegacyScribe + ")"); }
                else KitLog.Warn("Migration", "Old value of " + s.LegacyScribe + " ('" + raw + "') could not be read; " + s.Key + " keeps its default");
            }
            foreach (var sec in KitSections.All)
            {
                if (string.IsNullOrEmpty(sec.LegacyEnabled) || Template.SectionStored(sec.Id)) continue;
                string raw = null;
                try { Scribe_Values.Look(ref raw, sec.LegacyEnabled); } catch { continue; }
                if (raw == null) continue;
                if (bool.TryParse(raw.Trim(), out bool on)) { Template.sections[sec.Id] = on; Migrated.Add("section:" + sec.Id + "=" + on + " (from " + sec.LegacyEnabled + ")"); }
            }
            if (Migrated.Count == 0) return;
            BackupOnce();
            KitLog.Info("Migration", "Migrated " + Migrated.Count + " old settings: " + string.Join(", ", Migrated));
        }

        /// <summary>Copies the current settings file to *.pre-kit once (never overwrites an existing backup).</summary>
        static void BackupOnce()
        {
            try
            {
                string file = SettingsFilePath;
                if (file == null || !File.Exists(file)) return;
                string bak = file + ".pre-kit";
                if (File.Exists(bak)) return;
                File.Copy(file, bak);
                KitLog.Info("Migration", "Backup of the old settings file: " + Path.GetFileName(bak));
            }
            catch (Exception e) { KitLog.Warn("Migration", "Could not back up the old settings file: " + e.Message); }
        }

        /// <summary>Path of this mod's settings file (vanilla rule: Config/Mod_&lt;folder&gt;_&lt;Mod class name&gt;.xml). Null before the mod exists.</summary>
        internal static string SettingsFilePath
        {
            get
            {
                var mod = Kit.Mod;
                if (mod?.Content == null) return null;
                return Path.Combine(GenFilePaths.ConfigFolderPath, GenText.SanitizeFilename("Mod_" + mod.Content.FolderName + "_" + mod.GetType().Name + ".xml"));
            }
        }
    }
}
