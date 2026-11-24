// SPDX-License-Identifier: MIT
// Lifted from Hearth & Harbor Foundation/HUI.cs (Patch_ModSettings_InitialSize / _Ctor): "is HearthMod" → "mod == Kit.Mod".
using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace JBKit
{
    /// <summary>
    /// This mod's settings window is bigger and resizable (vanilla fixes Dialog_ModSettings at 900x700). Every kit mod applies these
    /// patches with its own Harmony id; each postfix only touches the window of its own mod and returns at once for any other mod.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_ModSettings), nameof(Dialog_ModSettings.InitialSize), MethodType.Getter)]
    internal static class KitPatch_ModSettings_InitialSize
    {
        static readonly FieldInfo modField = AccessTools.Field(typeof(Dialog_ModSettings), "mod");

        /// <summary>True when the dialog shows this mod's settings.</summary>
        internal static bool IsOurs(Dialog_ModSettings d)
        {
            try { return Kit.Mod != null && modField != null && ReferenceEquals(modField.GetValue(d), Kit.Mod); }
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
