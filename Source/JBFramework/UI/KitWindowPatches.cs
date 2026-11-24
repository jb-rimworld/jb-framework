// SPDX-License-Identifier: MIT
// Lifted from Hearth & Harbor Foundation/HUI.cs (Patch_ModSettings_InitialSize / _Ctor): "is HearthMod" → "is KitModBase".
using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace JBFramework
{
    /// <summary>
    /// The settings window of every mod built on the framework is bigger and resizable (vanilla fixes Dialog_ModSettings at 900x700).
    /// Applied once, by the framework itself; other mods' windows stay untouched.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_ModSettings), nameof(Dialog_ModSettings.InitialSize), MethodType.Getter)]
    internal static class KitPatch_ModSettings_InitialSize
    {
        static readonly FieldInfo modField = AccessTools.Field(typeof(Dialog_ModSettings), "mod");

        /// <summary>True when the dialog shows the settings of a mod built on the framework (or the framework's own).</summary>
        internal static bool IsOurs(Dialog_ModSettings d)
        {
            try { return modField != null && modField.GetValue(d) is KitModBase; }
            catch (Exception) { return false; }
        }

        [HarmonyPostfix]
        static void Postfix(Dialog_ModSettings __instance, ref Vector2 __result)
        {
            if (IsOurs(__instance)) __result = KitUI.SettingsSize();
        }
    }

    [HarmonyPatch(typeof(Dialog_ModSettings), MethodType.Constructor, new[] { typeof(Mod) })]
    internal static class KitPatch_ModSettings_Ctor
    {
        [HarmonyPostfix]
        static void Postfix(Dialog_ModSettings __instance)
        {
            if (KitPatch_ModSettings_InitialSize.IsOurs(__instance)) { __instance.resizeable = true; __instance.draggable = true; }
        }
    }
}
