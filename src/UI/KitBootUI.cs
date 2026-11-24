// SPDX-License-Identifier: MIT
namespace JBKit
{
    /// <summary>src/UI plugs into the core here (called from the KitModBase constructor after Register and the settings load).</summary>
    internal static partial class KitBoot
    {
        static partial void InstallUI()
        {
            KitSettingsWindow.Install();
            KitFirstStartCard.Install();
        }
    }
}
