// SPDX-License-Identifier: MIT
using System;
using Verse;

namespace JBFramework
{
    /// <summary>
    /// Startup of the knowledge base, the shared functions and the random streams (after all defs exist). Its order relative to the
    /// core's own startup class is not defined, so every registration made later than this pass is applied at once instead.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class KitPartsStartup
    {
        static KitPartsStartup()
        {
            Step("Known", KitKnownPage.Install);
            Step("Random", KitRandomState.Install);
            Step("Architect", KitArchitect.AfterStartup);
            Step("Tiers", KitTiers.AfterStartup);
            Step("Known.Check", CheckNotes);
        }

        static void Step(string site, Action a)
        {
            try { a(); }
            catch (Exception e) { try { Framework.Self?.Guard.Fail(KitMod.Core, "Startup." + site, e); } catch { } }
        }

        static void CheckNotes()
        {
            var self = Framework.Self;
            if (self == null) return;
            int notes = KitKnown.All.Count, active = 0;
            foreach (var n in KitKnown.All) if (n.Active) active++;
            self.Log.Info("Compat", $"Knowledge base: {notes} mod notes, {active} of these mods active");
        }
    }
}
