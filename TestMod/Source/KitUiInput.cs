// SPDX-License-Identifier: MIT
// Real mouse and keyboard input for the UI suites: the RimWorld 1.7 engine (Waylith) has an input injector, so a suite can click a button
// the way a player does (pointer move, press, release on separate frames) and the event runs through WindowStack, Window and Widgets
// exactly as a real click. Reached by reflection, so the harness does not compile against the engine. Part of JBFramework.Testing.dll.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace JBFramework.Testing
{
    /// <summary>Injected pointer and key input on the game's (virtual) screen, in UI units (the coordinates windows draw in).</summary>
    internal static class KitUiInput
    {
        static bool looked, ok;
        static object window, injector;
        static Type tPos, tButton, tKind, tKey;
        static MethodInfo move, press, release, keyDown, keyUp;
        static PropertyInfo scaleProp;
        /// <summary>Why the injector is not available (null when it is).</summary>
        public static string Problem;
        /// <summary>Correction factor found by <see cref="Calibrate"/> (logical units per UI unit).</summary>
        public static float Factor = -1f;

        public static bool Available => Init();

        static bool Init()
        {
            if (looked) return ok;
            looked = true;
            try
            {
                var tApp = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Waylith.EngineApplication", false)).FirstOrDefault(t => t != null);
                if (tApp == null) { Problem = "Waylith.EngineApplication not found (not RimWorld 1.7?)"; return false; }
                var app = tApp.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
                if (app == null) { Problem = "no EngineApplication instance"; return false; }
                var platform = tApp.GetProperty("WindowSystem")?.GetValue(app);
                window = tApp.GetProperty("Window")?.GetValue(app);
                if (platform == null || window == null) { Problem = "no window system or window"; return false; }
                var tPlatform = platform.GetType().GetInterfaces().FirstOrDefault(i => i.Name == "IPlatform") ?? platform.GetType();
                injector = tPlatform.GetProperty("InputInjector")?.GetValue(platform);
                if (injector == null) { Problem = "the platform has no input injector"; return false; }
                var tInj = injector.GetType().GetInterfaces().FirstOrDefault(i => i.Name == "IInputInjector");
                var tWin = window.GetType().GetInterfaces().FirstOrDefault(i => i.Name == "IWindow");
                if (tInj == null || tWin == null) { Problem = "IInputInjector/IWindow not found"; return false; }
                var asm = tInj.Assembly;
                tPos = asm.GetType("Waylith.Windowing.LogicalPosition"); tButton = asm.GetType("Waylith.Windowing.PointerButton");
                tKind = asm.GetType("Waylith.Windowing.PointerKind"); tKey = asm.GetType("Waylith.Windowing.PhysicalKey");
                MethodInfo M(string name, int n) => tInj.GetMethods().FirstOrDefault(m => m.Name == name && m.GetParameters().Length == n);
                move = M("MovePointer", 6); press = M("PressButton", 6); release = M("ReleaseButton", 5);
                keyDown = tInj.GetMethods().FirstOrDefault(m => m.Name == "KeyDown" && m.GetParameters().Length == 2);
                keyUp = M("KeyUp", 2);
                scaleProp = tWin.GetProperty("ScaleFactor");
                if (tPos == null || tButton == null || tKind == null || tKey == null || move == null || press == null || release == null || keyDown == null || keyUp == null)
                { Problem = "an injector member is missing"; return false; }
                ok = true;
            }
            catch (Exception e) { Problem = e.GetType().Name + ": " + e.Message; ok = false; }
            return ok;
        }

        static double WindowScale { get { try { return Convert.ToDouble(scaleProp?.GetValue(window) ?? 1.0); } catch { return 1.0; } } }

        static object Pos(Vector2 ui)
        {
            float f = Factor > 0f ? Factor : (float)(Prefs.UIScale / WindowScale);
            return Activator.CreateInstance(tPos, (double)(ui.x * f), (double)(ui.y * f));
        }
        static object Mouse => Enum.Parse(tKind, "Mouse");
        static object Left => Enum.Parse(tButton, "Left");

        public static void MoveTo(Vector2 ui) => move.Invoke(injector, new[] { window, Pos(ui), Mouse, null, null, null });
        public static void Down() => press.Invoke(injector, new[] { window, Left, Mouse, null, null, null });
        public static void Up() => release.Invoke(injector, new[] { window, Left, Mouse, null, null });
        public static void KeyDown(string key) => keyDown.Invoke(injector, new[] { Enum.Parse(tKey, key), null });
        public static void KeyUp(string key) => keyUp.Invoke(injector, new[] { Enum.Parse(tKey, key), null });

        /// <summary>The mouse position the game sees, in UI units (y down).</summary>
        public static Vector2 GameMouse => UI.MousePositionOnUIInverted;

        /// <summary>
        /// Moves the pointer to two points and compares with what the game reads, so later clicks land exactly (a scale mismatch between
        /// UI units and the window's logical units is measured, not assumed). <paramref name="result"/> gets a line for the log.
        /// </summary>
        public static IEnumerable<int> Calibrate(List<string> result)
        {
            if (!Available) { result.Add("input injector unavailable: " + Problem); yield break; }
            Factor = -1f;
            var a = new Vector2(300f, 200f);
            MoveTo(a);
            foreach (var y in KitUiTest.Frames(3)) yield return y;
            var seen = GameMouse;
            if (seen.x > 1f && seen.y > 1f) Factor = (float)(Prefs.UIScale / WindowScale) * (a.x / seen.x);
            var b = new Vector2(700f, 500f);
            MoveTo(b);
            foreach (var y in KitUiTest.Frames(3)) yield return y;
            var seen2 = GameMouse;
            result.Add($"pointer calibration: UI scale {Prefs.UIScale:0.##}, window scale {WindowScale:0.##}, factor {Factor:0.###}; aimed {a} saw {seen}; aimed {b} saw {seen2}");
        }

        /// <summary>A left click at <paramref name="ui"/>: move, press and release on separate frames (as a player's click arrives).</summary>
        public static IEnumerable<int> Click(Vector2 ui)
        {
            MoveTo(ui);
            foreach (var y in KitUiTest.Frames(2)) yield return y;
            Down();
            foreach (var y in KitUiTest.Frames(2)) yield return y;
            Up();
            foreach (var y in KitUiTest.Frames(3)) yield return y;
        }

        /// <summary>A key press (PhysicalKey name, e.g. "Escape"): down and up on separate frames.</summary>
        public static IEnumerable<int> Press(string key)
        {
            KeyDown(key);
            foreach (var y in KitUiTest.Frames(2)) yield return y;
            KeyUp(key);
            foreach (var y in KitUiTest.Frames(3)) yield return y;
        }
    }
}
