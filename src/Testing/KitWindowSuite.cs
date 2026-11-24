// SPDX-License-Identifier: MIT
// Stub of kit.window (spec 1.5.11): opens the settings window at three screen sizes and draws every page. The real suite comes with the
// integration of src/UI (it needs KitSettingsWindow and KitUI.TestScreen). Compiled only with -p:JBTests=true and tier lite/full.
#if JB_TESTS && JBKIT_UI
using System.Collections.Generic;

namespace JBKit.Testing
{
    /// <summary>Placeholder: runs only when named explicitly and checks nothing yet.</summary>
    internal sealed class KitWindowSuite : KitTestSuite
    {
        public override string Name => "kit.window";
        public override int MaxTicks => 600;
        public override bool ExplicitOnly => true;

        public override IEnumerable<int> Run(TestContext t)
        {
            t.Note("kit.window is a stub: the window test arrives with the src/UI integration. Nothing was checked.");
            yield return 1;
        }
    }
}
#endif
