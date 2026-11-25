// SPDX-License-Identifier: MIT
// JB Framework guard: shared SOURCE that every dependent mod compiles into its own tiny <Mod>.Guard.dll (build/JBGuard.props).
// The mod's LoadFolders.xml loads it ONLY when JB Framework is missing or switched off (folder Guard, IfModNotActive); every content
// folder of the mod loads only with the framework (IfModActive). Then the guard is all that loads of the mod: no red error, no lost
// def, and ONE window at the main menu (all guards of all mods share it through AppDomain data, without a shared type) tells the
// player in plain words and offers to install the framework.
//
// Rules for this file: it references nothing but the game (Assembly-CSharp, Unity): never the framework (it is what is missing), never
// Harmony, never Steamworks directly (reached by reflection, so a build without Steam loads). Every path ends without an exception:
// any failure shows the manual (GitHub) route and logs ONE warning. No test code outside #if JB_TESTS.
//
// Tokens replaced by build/JBGuard.props (a copy per mod, so five mods never collide): JBGUARD_NAMESPACE (the mod's root namespace +
// ".JBGuard"), JBGUARD_KEY (key prefix of its Keyed texts), JBGUARD_FRAMEWORK_ID, JBGUARD_FRAMEWORK_NAME, JBGUARD_WORKSHOP_ID (from the
// framework's About/PublishedFileId.txt; a non-number until the first upload: then "install" takes the manual route), JBGUARD_INSTALL_URL.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace JBGUARD_NAMESPACE
{
    /// <summary>Runs once all defs and languages are loaded (only when the guard folder was loaded, i.e. the framework is not active).</summary>
    [StaticConstructorOnStartup]
    internal static class JBGuardStartup
    {
        static JBGuardStartup() { JBGuard.Register(); }
    }

    /// <summary>The guard of one mod: registers the mod in the shared list; the first guard of the session opens the one window.</summary>
    internal static class JBGuard
    {
        internal const string Key = "JBGUARD_KEY";
        internal const string FrameworkName = "JBGUARD_FRAMEWORK_NAME";
        internal const string WorkshopId = "JBGUARD_WORKSHOP_ID";
        internal const string InstallUrl = "JBGUARD_INSTALL_URL";
        const string FrameworkIdBuilt = "JBGUARD_FRAMEWORK_ID";
        // Shared by every guard of every mod and version. The entry format never changes: string[] { protocol, mod name, packageId, key prefix }.
        internal const string RegistryKey = "JBGuard.v1.mods";
        const string LeaderKey = "JBGuard.v1.leader";
        const string Protocol = "1";

        internal static string Name = Key;
        internal static string PackageId = "";
        static bool warned;

        /// <summary>The framework's packageId (lower case).</summary>
        internal static string FrameworkId
        {
            get
            {
#if JB_TESTS
                var v = JBGuardTest.Env("JB_GUARD_FWID");
                if (!string.IsNullOrEmpty(v)) return v.ToLowerInvariant();
#endif
                return FrameworkIdBuilt.ToLowerInvariant();
            }
        }

        internal static void Register()
        {
            try
            {
                var me = typeof(JBGuard).Assembly;
                var mcp = LoadedModManager.RunningMods.FirstOrDefault(m => m?.assemblies?.loadedAssemblies != null && m.assemblies.loadedAssemblies.Contains(me));
                if (mcp != null) { Name = mcp.Name; PackageId = mcp.PackageId ?? ""; }
                var reg = Registry(true);
                lock (reg)
                {
                    if (!reg.Any(e => e != null && e.Length > 2 && Plain(e[2]) == Plain(PackageId))) reg.Add(new[] { Protocol, Name, PackageId, Key });
                }
                Log.Message("[" + Name + "] " + FrameworkName + " is not active: " + Name + " stays switched off and only its guard loaded (it asks the player to install " + FrameworkName + ").");
                var d = AppDomain.CurrentDomain;
                if (d.GetData(LeaderKey) == null)
                {
                    d.SetData(LeaderKey, Key);
                    // the window stack exists only once the main menu is up: a tiny behaviour waits for it (no Harmony, no game needed)
                    var go = new GameObject("JBGuardOpener");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    go.AddComponent<JBGuardOpener>();
                }
            }
            catch (Exception e) { Fail("starting the guard", e); }
        }

        /// <summary>The shared list of guarded mods (created by the first guard).</summary>
        internal static List<string[]> Registry(bool create)
        {
            var d = AppDomain.CurrentDomain;
            var reg = d.GetData(RegistryKey) as List<string[]>;
            if (reg == null && create) { reg = new List<string[]>(); d.SetData(RegistryKey, reg); }
            return reg ?? new List<string[]>();
        }

        /// <summary>The guarded mods: (name, packageId, key prefix), in load order of their guards.</summary>
        internal static List<string[]> Mods()
        {
            var reg = Registry(false);
            lock (reg) return reg.Where(e => e != null && e.Length > 3).ToList();
        }

        /// <summary>Opens the window; false while the game is not ready for it yet (still loading, no window stack).</summary>
        internal static bool TryShow()
        {
            try
            {
                if (LongEventHandler.AnyEventNowOrWaiting || Current.Root == null || Find.UIRoot == null || Find.WindowStack == null) return false;
                Show();
            }
            catch (Exception e) { Fail("opening the window", e); OpenUrl(InstallUrl); }
            return true;
        }

        static void Show()
        {
            try
            {
                if (Find.WindowStack.Windows.Any(w => w != null && w.GetType().Name == nameof(JBGuardWindow))) return;
                Find.WindowStack.Add(new JBGuardWindow());
            }
            catch (Exception e) { Fail("opening the window", e); OpenUrl(InstallUrl); }
        }

        /// <summary>packageId without the Workshop postfix and a test-slot suffix, lower case.</summary>
        internal static string Plain(string pid)
        {
            string s = (pid ?? "").Trim().ToLowerInvariant();
            if (s.EndsWith("_steam")) s = s.Substring(0, s.Length - 6);
            return s;
        }

        /// <summary>A text of this guard (English fallback inside), formatted with {0}, {1}, ...</summary>
        internal static string T(string key, string fallback, params object[] args) => TOf(Key, key, fallback, args);

        /// <summary>A text by another guard's key prefix (the loss line of each listed mod).</summary>
        internal static string TOf(string prefix, string key, string fallback, params object[] args)
        {
            string s = fallback;
            try { if ((prefix + "." + key).TryTranslate(out TaggedString t)) s = t.RawText; } catch { }
            try { return args == null || args.Length == 0 ? s : string.Format(s, args); }
            catch { return fallback; }
        }

        /// <summary>One warning per session (zero on the normal path) and nothing thrown.</summary>
        internal static void Fail(string what, Exception e)
        {
            try
            {
                if (warned) return; warned = true;
                Log.Warning("[" + Name + "] Guard: " + what + " failed (" + (e == null ? "?" : e.GetType().Name + ": " + e.Message) + "). Showing the manual install route: " + InstallUrl);
            }
            catch { }
        }

        internal static void OpenUrl(string url)
        {
            try
            {
#if JB_TESTS
                if (JBGuardTest.Active) { JBGuardTest.OpenedUrls.Add(url); return; }
#endif
                Application.OpenURL(url);
            }
            catch (Exception e) { Fail("opening " + url, e); }
        }
    }

    /// <summary>Waits, frame by frame, until the main menu can show a window, opens it once, and removes itself.</summary>
    internal sealed class JBGuardOpener : MonoBehaviour
    {
        float since = -1f;
        void Update()
        {
            try
            {
                if (since < 0f) since = Time.realtimeSinceStartup;
                bool done = JBGuard.TryShow();
                if (!done && Time.realtimeSinceStartup - since > 600f) { JBGuard.Fail("waiting for the main menu", new TimeoutException("no window stack after 10 minutes")); JBGuard.OpenUrl(JBGuard.InstallUrl); done = true; }
                if (done) Destroy(gameObject);
            }
            catch (Exception e) { JBGuard.Fail("opening the window", e); Destroy(this); }
        }
    }

    /// <summary>Steam through the game's Steamworks binding, reached by reflection (no compile-time reference; absent = not on Steam).</summary>
    internal static class JBGuardSteam
    {
        static Type ugc, pfidType;
        static bool looked;

        internal static bool Ready
        {
            get
            {
#if JB_TESTS
                if (JBGuardTest.FakeSteam) return true;
#endif
                try { return Verse.Steam.SteamManager.Initialized && Types(); } catch { return false; }
            }
        }

        static bool Types()
        {
            if (!looked)
            {
                looked = true;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        if (ugc == null) ugc = a.GetType("Steamworks.SteamUGC", false);
                        if (pfidType == null) pfidType = a.GetType("Steamworks.PublishedFileId_t", false);
                    }
                    catch { }
                }
            }
            return ugc != null && pfidType != null;
        }

        static object Pfid(ulong id) => Activator.CreateInstance(pfidType, id);

        static object Call(string method, params object[] args)
        {
            var m = ugc.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(x => x.Name == method && x.GetParameters().Length == args.Length);
            if (m == null) throw new MissingMethodException("Steamworks.SteamUGC", method);
            return m.Invoke(null, args);
        }

        /// <summary>Subscribes and asks for a high-priority download.</summary>
        internal static void Subscribe(ulong id)
        {
#if JB_TESTS
            if (JBGuardTest.FakeSteam) { JBGuardTest.FakeSubscribe(id); return; }
#endif
            Call("SubscribeItem", Pfid(id));
            try { Call("DownloadItem", Pfid(id), true); } catch { }
        }

        /// <summary>Installed (and not still downloading), and the download progress 0..1 (-1 = unknown).</summary>
        internal static bool Installed(ulong id, out float progress, out ulong done, out ulong total)
        {
#if JB_TESTS
            if (JBGuardTest.FakeSteam) return JBGuardTest.FakeState(out progress, out done, out total);
#endif
            progress = -1f; done = 0; total = 0;
            uint state = Convert.ToUInt32(Call("GetItemState", Pfid(id)));
            var args = new object[] { Pfid(id), 0UL, 0UL };
            var m = ugc.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(x => x.Name == "GetItemDownloadInfo" && x.GetParameters().Length == 3);
            if (m != null && m.Invoke(null, args) is bool ok && ok)
            {
                done = Convert.ToUInt64(args[1]); total = Convert.ToUInt64(args[2]);
                if (total > 0) progress = Mathf.Clamp01((float)((double)done / total));
            }
            // k_EItemStateInstalled 4, k_EItemStateNeedsUpdate 8, k_EItemStateDownloading 16, k_EItemStateDownloadPending 32
            return (state & 4u) != 0 && (state & (8u | 16u | 32u)) == 0;
        }

        /// <summary>Lets the game see a newly installed Workshop item (vanilla does this in its "item installed" callback).</summary>
        internal static void RefreshWorkshopItems()
        {
            try
            {
                var t = GenTypes.GetTypeInAnyAssembly("Verse.Steam.WorkshopItems");
                t?.GetMethod("RebuildItemsList", BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, null);
            }
            catch { }
        }
    }

    /// <summary>The one window: what is missing, what it means for the colony, and the way out.</summary>
    internal sealed class JBGuardWindow : Window
    {
        // JB family look: KitStyle of the framework, copied as constants (the framework is what is missing here)
        static readonly Color Accent = new Color(0.95f, 0.72f, 0.30f);
        static readonly Color AccentSoft = new Color(0.95f, 0.72f, 0.30f, 0.18f);
        static readonly Color Grey = new Color(0.72f, 0.72f, 0.72f);
        static readonly Color DimGrey = new Color(0.55f, 0.55f, 0.55f);
        static readonly Color Good = new Color(0.55f, 0.78f, 0.45f);
        static readonly Color Bad = new Color(0.88f, 0.42f, 0.38f);
        static readonly Color Line = new Color(1f, 1f, 1f, 0.15f);
        const float Gap = 10f, ButtonH = 40f, SmallH = 32f;

        internal enum Stage { Ask, Downloading, Restarting, Manual }
        internal Stage Now = Stage.Ask;
        string status = "", manualWhy = "";
        float progress = -1f, startedAt, lastChangeAt, installedAt = -1f;
        ulong id, lastDone;
        Vector2 scroll;
        internal float LastContentH, LastTextAreaH;

        public JBGuardWindow()
        {
            forcePause = true; absorbInputAroundWindow = true; closeOnClickedOutside = false; closeOnCancel = false; closeOnAccept = false;
            doCloseX = false; doCloseButton = false; draggable = false; onlyOneOfTypeAllowed = true; preventCameraMotion = true;
        }

        public override Vector2 InitialSize
        {
            get
            {
                float w = Mathf.Min(780f, UI.screenWidth - 40f);
                float h = Mathf.Min(UI.screenHeight - 40f, TextHeight(w - Margin * 2f - 16f) + ButtonsHeight() + Margin * 2f + 8f);
                return new Vector2(w, Mathf.Max(320f, h));
            }
        }

        static List<string[]> Mods() { var m = JBGuard.Mods(); if (m.Count == 0) m.Add(new[] { "1", JBGuard.Name, JBGuard.PackageId, JBGuard.Key }); return m; }

        string Title() => JBGuard.T("Title", "One more mod needed: {0}", JBGuard.FrameworkName);

        string Body()
        {
            var mods = Mods();
            string fw = JBGuard.FrameworkName;
            if (mods.Count == 1)
                return JBGuard.T("One", "{0} was updated and now needs the free mod {1}. Without it this mod is switched off, and a colony saved now would lose {2}.",
                    mods[0][1], fw, Loss(mods[0]));
            var lines = new List<string> { JBGuard.T("Many", "These mods were updated and now need the free mod {0}. Without it they are switched off:", fw) };
            foreach (var m in mods) lines.Add("  •  " + JBGuard.T("ManyLine", "{0}: a colony saved now would lose {1}.", m[1], Loss(m)));
            return string.Join("\n", lines);
        }

        static string Loss(string[] m) => JBGuard.TOf(m[3], "Loss", JBGuard.T("LossDefault", "everything this mod added"));

        string Free() => JBGuard.T("Free", "{0} is free and adds nothing of its own. Installing it takes a moment; the game then restarts once and everything is back.", JBGuard.FrameworkName);

        string StatusText()
        {
            switch (Now)
            {
                case Stage.Downloading: return status;
                case Stage.Restarting: return JBGuard.T("Restarting", "{0} is switched on. Restarting the game…", JBGuard.FrameworkName);
                case Stage.Manual:
                    string where = GenFilePaths.ModsFolderPath;
                    var mods = Mods();
                    return (manualWhy.NullOrEmpty() ? "" : manualWhy + "\n\n") + JBGuard.T("Manual",
                        "Install {0} by hand: the page that just opened has the download. Unpack it and put the folder into your Mods folder:\n{1}\nThen start RimWorld again and switch on {0} in the Mods menu, directly above {2}.",
                        JBGuard.FrameworkName, where, mods[0][1]);
                default:
                    return FrameworkInstalled() ? JBGuard.T("InstalledOff", "{0} is already installed, but switched off.", JBGuard.FrameworkName) : "";
            }
        }

        float TextHeight(float w)
        {
            float h = 0f;
            Text.Font = GameFont.Medium; h += Text.CalcHeight(Title(), w) + 4f;
            Text.Font = GameFont.Small; h += 8f + Text.CalcHeight(Body(), w) + Gap + Text.CalcHeight(Free(), w) + Gap;
            string st = StatusText();
            if (!st.NullOrEmpty()) h += Text.CalcHeight(st, w) + Gap;
            if (Now == Stage.Downloading) h += 22f + Gap;
            Text.Font = GameFont.Small;
            return h;
        }

        float ButtonsHeight() => ButtonH + Gap + SmallH + Gap + 24f + Text.LineHeightOf(GameFont.Tiny) * 2f + 6f;

        internal static bool FrameworkInstalled() => FindFramework() != null;

        internal static ModMetaData FindFramework()
        {
#if JB_TESTS
            if (JBGuardTest.PretendMissing) return null;
#endif
            try
            {
                string want = JBGuard.FrameworkId;
                return ModLister.AllInstalledMods.FirstOrDefault(m => m != null && JBGuard.Plain(m.PackageId) == want);
            }
            catch { return null; }
        }

        bool HasWorkshopId(out ulong v)
        {
#if JB_TESTS
            if (JBGuardTest.FakeSteam) { v = 4242; return true; }
#endif
            return ulong.TryParse(JBGuard.WorkshopId, out v) && v > 0;
        }

        /// <summary>The text grows (status lines, the manual route): the window follows, centred, as far as the screen allows.</summary>
        void FitWindow()
        {
            var want = InitialSize;
            if (Mathf.Abs(want.y - windowRect.height) < 1f && Mathf.Abs(want.x - windowRect.width) < 1f) return;
            windowRect = new Rect((UI.screenWidth - want.x) / 2f, (UI.screenHeight - want.y) / 2f, want.x, want.y);
        }

        public override void DoWindowContents(Rect inRect)
        {
            try { FitWindow(); Draw(inRect); }
            catch (Exception e)
            {
                JBGuard.Fail("drawing the window", e);
                if (Now != Stage.Manual) Manual("", false);
                GUI.color = Color.white; Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;
            }
        }

        void Draw(Rect inRect)
        {
            float w = inRect.width;
            float bh = ButtonsHeight();
            var textArea = new Rect(inRect.x, inRect.y, w, inRect.height - bh);
            float need = TextHeight(w - 16f);
            LastContentH = need; LastTextAreaH = textArea.height;
            bool scrolls = need > textArea.height;
            var view = new Rect(0f, 0f, scrolls ? w - 16f : w, scrolls ? need : textArea.height);
            if (scrolls) Widgets.BeginScrollView(textArea, ref scroll, view);
            else GUI.BeginGroup(textArea);
            float y = 0f, tw = view.width;
            Text.Font = GameFont.Medium; GUI.color = Accent;
            float th = Text.CalcHeight(Title(), tw); Widgets.Label(new Rect(0f, y, tw, th), Title()); y += th + 4f;
            GUI.color = Line; Widgets.DrawLineHorizontal(0f, y, tw); y += 8f;
            GUI.color = Color.white; Text.Font = GameFont.Small;
            y += Para(y, tw, Body(), Color.white) + Gap;
            y += Para(y, tw, Free(), Grey) + Gap;
            string st = StatusText();
            if (!st.NullOrEmpty()) y += Para(y, tw, st, Now == Stage.Manual ? Accent : Now == Stage.Restarting ? Good : Grey) + Gap;
            if (Now == Stage.Downloading)
            {
                var bar = new Rect(0f, y, tw, 20f);
                Widgets.DrawBoxSolid(bar, new Color(0.12f, 0.12f, 0.12f, 0.85f));
                float f = progress < 0f ? Mathf.PingPong(Time.realtimeSinceStartup * 0.5f, 1f) * 0.25f : progress;
                Widgets.DrawBoxSolid(new Rect(bar.x + 1f, bar.y + 1f, Mathf.Max(0f, (bar.width - 2f) * Mathf.Clamp01(f)), bar.height - 2f), Accent);
                GUI.color = Line; Widgets.DrawBox(bar); GUI.color = Color.white;
                y += 22f + Gap;
            }
            if (scrolls) Widgets.EndScrollView(); else GUI.EndGroup();
            DrawButtons(new Rect(inRect.x, inRect.yMax - bh, w, bh));
            GUI.color = Color.white; Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;
        }

        static float Para(float y, float w, string text, Color c)
        {
            float h = Text.CalcHeight(text, w);
            GUI.color = c; Widgets.Label(new Rect(0f, y, w, h), text); GUI.color = Color.white;
            return h;
        }

        void DrawButtons(Rect r)
        {
            var mods = Mods();
            float y = r.y + 4f;
            bool busy = Now == Stage.Downloading || Now == Stage.Restarting;
            // the main way out, in the accent colour
            string main = Now == Stage.Manual ? JBGuard.T("OpenPageAgain", "Open the download page again")
                : FrameworkInstalled() ? JBGuard.T("Enable", "Switch on {0} and restart", JBGuard.FrameworkName)
                : JBGuard.T("Install", "Install {0} and restart", JBGuard.FrameworkName);
            var mr = new Rect(r.x, y, r.width, ButtonH);
            Widgets.DrawBoxSolid(mr, AccentSoft);
            if (!busy && Widgets.ButtonText(mr, main)) { if (Now == Stage.Manual) JBGuard.OpenUrl(JBGuard.InstallUrl); else Install(); }
            if (busy) { Text.Anchor = TextAnchor.MiddleCenter; GUI.color = DimGrey; Widgets.Label(mr, main); GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft; }
            GUI.color = Accent; Widgets.DrawBox(mr); GUI.color = Color.white;
            y += ButtonH + Gap;
            float half = (r.width - Gap) / 2f;
            string second = Now == Stage.Manual ? JBGuard.T("OpenModsFolder", "Open the Mods folder")
                : HasWorkshopId(out _) ? JBGuard.T("Workshop", "Open the Workshop page") : JBGuard.T("DownloadPage", "Open the download page");
            if (Widgets.ButtonText(new Rect(r.x, y, half, SmallH), second))
            {
                if (Now == Stage.Manual) JBGuard.OpenUrl("file://" + GenFilePaths.ModsFolderPath);
                else if (HasWorkshopId(out ulong wid)) JBGuard.OpenUrl(JBGuardSteam.Ready ? "steam://url/CommunityFilePage/" + wid : "https://steamcommunity.com/sharedfiles/filedetails/?id=" + wid);
                else JBGuard.OpenUrl(JBGuard.InstallUrl);
            }
            if (Widgets.ButtonText(new Rect(r.x + half + Gap, y, half, SmallH), JBGuard.T("Quit", "Quit game"))) Quit();
            y += SmallH + Gap;
            // the way on without the framework: small, with the warning repeated
            string cont = mods.Count == 1 ? JBGuard.T("ContinueOne", "Continue without {0}", mods[0][1]) : JBGuard.T("ContinueMany", "Continue without these {0} mods", mods.Count);
            Text.Font = GameFont.Tiny;
            float cw = Mathf.Min(r.width, Text.CalcSize(cont).x + 24f);
            var cr = new Rect(r.x + (r.width - cw) / 2f, y, cw, 24f);
            if (!busy && Widgets.ButtonText(cr, cont, true, true, true)) Continue();
            y += 26f;
            string warn = mods.Count == 1 ? JBGuard.T("ContinueWarnOne", "Not recommended: {0} stays switched off, and a colony saved now would lose {1}.", mods[0][1], Loss(mods[0]))
                : JBGuard.T("ContinueWarnMany", "Not recommended: these mods stay switched off, and a colony saved now would lose what they added.");
            GUI.color = DimGrey; Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(r.x, y, r.width, r.yMax - y), warn);
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
        }

        /// <summary>"Install and restart": installed but off → switch on; Steam → subscribe and wait; otherwise the manual route.</summary>
        internal void Install()
        {
            try
            {
                var fw = FindFramework();
                if (fw != null) { ActivateAndRestart(fw); return; }
                if (!JBGuardSteam.Ready) { Manual(JBGuard.T("NoSteam", "Steam is not available here, so {0} cannot be installed automatically.", JBGuard.FrameworkName), false); return; }
                if (!HasWorkshopId(out id)) { Manual("", false); return; }
                JBGuardSteam.Subscribe(id);
                Now = Stage.Downloading; startedAt = lastChangeAt = Time.realtimeSinceStartup; progress = -1f; lastDone = 0; installedAt = -1f;
                status = JBGuard.T("Subscribing", "Asking Steam for {0}…", JBGuard.FrameworkName);
                Log.Message("[" + JBGuard.Name + "] Guard: subscribed to " + JBGuard.FrameworkName + " (Workshop item " + id + "), waiting for the download");
            }
            catch (Exception e) { JBGuard.Fail("installing through Steam", e); Manual(JBGuard.T("AutoFailed", "The automatic install did not work."), true); }
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
#if JB_TESTS
            JBGuardTest.Tick(this);
#endif
            if (Now != Stage.Downloading) return;
            try
            {
                float now = Time.realtimeSinceStartup;
                if (installedAt < 0f)
                {
                    bool done = JBGuardSteam.Installed(id, out float p, out ulong got, out ulong total);
                    if (got != lastDone || Math.Abs(p - progress) > 0.001f) { lastChangeAt = now; lastDone = got; }
                    progress = p;
                    status = total > 0
                        ? JBGuard.T("Downloading", "Downloading {0}: {1} of {2} KB", JBGuard.FrameworkName, got / 1024, total / 1024)
                        : JBGuard.T("Waiting", "Waiting for Steam to download {0}…", JBGuard.FrameworkName);
                    if (done)
                    {
                        installedAt = now; progress = 1f;
                        status = JBGuard.T("Installed", "{0} is installed. Switching it on…", JBGuard.FrameworkName);
#if JB_TESTS
                        JBGuardTest.FakeDownloadFinished();
#endif
                        JBGuardSteam.RefreshWorkshopItems();
                        ModLister.RebuildModList();
                    }
                    else if (now - lastChangeAt > 120f)
                    {
                        JBGuard.Fail("waiting for Steam", new TimeoutException("no download progress for 2 minutes"));
                        Manual(JBGuard.T("SteamSlow", "Steam did not download {0} in time.", JBGuard.FrameworkName), true);
                    }
                    return;
                }
                // installed: the mod list must see the new folder (Steam may report it a moment before the folder is complete)
                var fw = FindFramework();
                if (fw != null) { ActivateAndRestart(fw); return; }
                if (now - installedAt > 20f)
                {
                    JBGuard.Fail("finding the installed framework", new InvalidOperationException("not in the mod list 20 s after Steam installed it"));
                    Manual(JBGuard.T("AutoFailed", "The automatic install did not work."), true);
                    return;
                }
                if (Time.frameCount % 30 == 0) { JBGuardSteam.RefreshWorkshopItems(); ModLister.RebuildModList(); }
            }
            catch (Exception e) { JBGuard.Fail("waiting for the download", e); Manual(JBGuard.T("AutoFailed", "The automatic install did not work."), true); }
        }

        /// <summary>
        /// Switches the framework on directly above the first mod that needs it (Harmony stays above), saves the mod list and restarts.
        /// </summary>
        internal void ActivateAndRestart(ModMetaData fw)
        {
            try
            {
                var ids = ModsConfig.ActiveModsInLoadOrder.Where(m => m != null).Select(m => m.PackageId.ToLowerInvariant()).ToList();
                string want = JBGuard.FrameworkId;
                ids.RemoveAll(x => JBGuard.Plain(x) == want);
                var ours = new HashSet<string>(Mods().Select(m => JBGuard.Plain(m[2])));
                int at = ids.FindIndex(x => ours.Contains(JBGuard.Plain(x)));
                if (at < 0) at = ids.Count;
                string fwId = fw.PackageId.ToLowerInvariant();
                ids.Insert(at, fwId);
                int harmony = ids.FindIndex(x => JBGuard.Plain(x) == "brrainz.harmony");
                if (harmony > at) { string h = ids[harmony]; ids.RemoveAt(harmony); ids.Insert(at, h); }
                ModsConfig.SetActiveToList(ids);
                if (!ModsConfig.IsActive(fwId)) throw new InvalidOperationException("the mod list did not take " + fwId);
                ModsConfig.Save();
                Now = Stage.Restarting;
                Log.Message("[" + JBGuard.Name + "] Guard: " + JBGuard.FrameworkName + " switched on above " + (at + 1 < ids.Count ? ids[at + 1] : "the end") + "; restarting");
#if JB_TESTS
                if (JBGuardTest.Active) { JBGuardTest.RestartRequested(this, ids); return; }
#endif
                LongEventHandler.ExecuteWhenFinished(GenCommandLine.Restart);
            }
            catch (Exception e) { JBGuard.Fail("switching on the framework", e); Manual(JBGuard.T("AutoFailed", "The automatic install did not work."), true); }
        }

        internal void Manual(string why, bool failed)
        {
            Now = Stage.Manual; manualWhy = why ?? "";
            if (!failed) Log.Message("[" + JBGuard.Name + "] Guard: manual install route (" + JBGuard.InstallUrl + ")");
            JBGuard.OpenUrl(JBGuard.InstallUrl);
        }

        void Quit()
        {
#if JB_TESTS
            if (JBGuardTest.Active) { JBGuardTest.Note("quit pressed"); return; }
#endif
            try { Root.Shutdown(); } catch (Exception e) { JBGuard.Fail("quitting", e); }
        }

        void Continue()
        {
            Log.Message("[" + JBGuard.Name + "] Guard: the player continues without " + JBGuard.FrameworkName + "; the guarded mods stay switched off");
            Close();
        }
    }

#if JB_TESTS
    /// <summary>
    /// Test seam (test builds only, tools/jbmod noframework): drives the window, takes screenshots at 1920x1080 and 1280x720, fakes the
    /// Steam part, records URLs instead of opening them and the restart instead of doing it, writes $JB_GUARD_TEST and quits.
    /// </summary>
    internal static class JBGuardTest
    {
        internal static string Env(string k) { try { return Environment.GetEnvironmentVariable(k); } catch { return null; } }
        internal static bool Active => !string.IsNullOrEmpty(Env("JB_GUARD_TEST"));
        internal static bool FakeSteam, PretendMissing;
        internal static readonly List<string> OpenedUrls = new List<string>();
        static readonly List<string> report = new List<string>();
        static int step, wait, fakeStart = -1, fails;
        static bool fakeSubscribed, fakeDone, finished;
        static float fakeSeenHalf = -1f;
        static float scaleWas = -1f;

        internal static void Note(string s) { report.Add(s); Log.Message("[JBGUARDTEST] " + s); }
        static void Check(bool ok, string s) { if (!ok) fails++; Note((ok ? "PASS " : "FAIL ") + s); }

        static string Lang() { try { return (LanguageDatabase.activeLanguage?.folderName ?? "English").Split(' ')[0]; } catch { return "English"; } }

        static void Shot(string name)
        {
            string dir = Env("JB_UI_SHOTS");
            if (string.IsNullOrEmpty(dir) || dir == "0") return;
            try { System.IO.Directory.CreateDirectory(dir); ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); Note("screenshot " + name); }
            catch (Exception e) { Note("screenshot failed: " + e.Message); }
        }

        static bool Fits(Window w) => w.windowRect.xMin >= -0.5f && w.windowRect.yMin >= -0.5f && w.windowRect.xMax <= UI.screenWidth + 0.5f && w.windowRect.yMax <= UI.screenHeight + 0.5f;

        static JBGuardWindow Reopen(JBGuardWindow old)
        {
            old.Close(false);
            var w = new JBGuardWindow();
            Find.WindowStack.Add(w);
            return w;
        }

        static void SetScreenHeight(int h)
        {
            if (scaleWas < 0f) scaleWas = Prefs.UIScale;
            Prefs.UIScale = Mathf.Clamp((Screen.height > 0 ? Screen.height : 1080f) / (float)h, 0.25f, 4f);
        }

        static string Size() => UI.screenWidth + "x" + UI.screenHeight;

        static void Describe(JBGuardWindow w, string tag)
        {
            int windows = Find.WindowStack.Windows.Count(x => x != null && x.GetType().Name == nameof(JBGuardWindow));
            var mods = JBGuard.Mods();
            Check(windows == 1, tag + ": one guard window on screen (" + windows + ")");
            Note(tag + ": mods listed " + mods.Count + ": " + string.Join(", ", mods.Select(m => m[1] + " (" + m[2] + ")")));
            Check(Fits(w), tag + ": window " + w.windowRect + " inside the screen " + Size());
            Check(w.LastContentH <= w.LastTextAreaH + 0.5f, tag + ": text fits without scrolling (" + w.LastContentH.ToString("0") + " of " + w.LastTextAreaH.ToString("0") + " px)");
        }

        /// <summary>One step per frame, from the leader window's WindowUpdate.</summary>
        internal static void Tick(JBGuardWindow w)
        {
            if (!Active || finished) return;
            if (wait > 0) { wait--; return; }
            string lang = Lang();
            try
            {
                switch (step)
                {
                    case 0: SetScreenHeight(1080); wait = 3; step++; break;
                    case 1: w = Reopen(w); wait = 40; step++; break;
                    case 2:
                        Note("guard window shown at " + Size() + " in " + lang + ", stage " + w.Now);
                        Check(Current.ProgramState == ProgramState.Entry, "shown at the main menu (program state " + Current.ProgramState + ")");
                        Check(FrameworkInstalledReally(), "the framework copy of this run is installed but inactive (found by packageId " + JBGuard.FrameworkId + ")");
                        Describe(w, Size());
                        Shot("guard_" + lang + "_" + Size());
                        wait = 3; step++; break;
                    case 3: SetScreenHeight(720); wait = 3; step++; break;
                    case 4: w = Reopen(w); wait = 8; step++; break;
                    case 5:
                        Describe(w, Size());
                        Shot("guard_" + lang + "_" + Size());
                        wait = 3; step++; break;
                    case 6:
                        // no Steam: the manual (GitHub) route
                        PretendMissing = true; FakeSteam = false; OpenedUrls.Clear();
                        w.Install();
                        Check(w.Now == JBGuardWindow.Stage.Manual, "without Steam: manual route (stage " + w.Now + ")");
                        Check(OpenedUrls.Contains(JBGuard.InstallUrl), "without Steam: " + JBGuard.InstallUrl + " opened (" + string.Join(", ", OpenedUrls) + ")");
                        wait = 6; step++; break;
                    case 7:
                        Check(Fits(w) && w.LastContentH <= w.LastTextAreaH + 0.5f, "manual route at " + Size() + ": fits, no scrolling (" + w.LastContentH.ToString("0") + " of " + w.LastTextAreaH.ToString("0") + ")");
                        Shot("guard_" + lang + "_" + Size() + "_manual");
                        wait = 3; step++; break;
                    case 8: SetScreenHeight(1080); wait = 3; step++; break;
                    case 9: wait = 3; step++; break;
                    case 10:
                        w = Reopen(w);
                        // Steam faked: subscribe, download progress, installed → mod list rebuilt → switched on above the first guarded mod
                        FakeSteam = true; PretendMissing = true; OpenedUrls.Clear();
                        wait = 4; step++; break;
                    case 11:
                        w.Install();
                        Check(w.Now == JBGuardWindow.Stage.Downloading && fakeSubscribed, "fake Steam: subscribed, downloading (stage " + w.Now + ")");
                        step++; break;
                    case 12:
                        if (fakeSeenHalf < 0f && fakeStart >= 0 && Time.frameCount - fakeStart > 40) { fakeSeenHalf = 1f; Shot("guard_" + lang + "_" + Size() + "_download"); }
                        if (w.Now == JBGuardWindow.Stage.Manual) { Check(false, "fake Steam ended in the manual route"); Finish(); }
                        if (Time.frameCount - fakeStart > 3000) { Check(false, "fake Steam: no restart after 3000 frames (stage " + w.Now + ")"); Finish(); }
                        break;
                }
            }
            catch (Exception e) { Check(false, "test step " + step + " threw " + e); Finish(); }
        }

        static bool FrameworkInstalledReally() { bool p = PretendMissing; PretendMissing = false; try { return JBGuardWindow.FrameworkInstalled(); } finally { PretendMissing = p; } }

        internal static void FakeSubscribe(ulong id) { fakeSubscribed = true; fakeStart = Time.frameCount; Note("fake Steam: SubscribeItem(" + id + ")"); }

        internal static bool FakeState(out float progress, out ulong done, out ulong total)
        {
            total = 2048 * 1024; int f = fakeStart < 0 ? 0 : Time.frameCount - fakeStart;
            progress = Mathf.Clamp01(f / 90f); done = (ulong)(total * progress);
            return f >= 90;
        }

        internal static void FakeDownloadFinished() { if (!FakeSteam) return; fakeDone = true; PretendMissing = false; Note("fake Steam: item installed (the run's framework copy is the installed item)"); }

        internal static void RestartRequested(JBGuardWindow w, List<string> ids)
        {
            Check(fakeDone, "restart only after the download finished");
            string fw = JBGuard.FrameworkId;
            int at = ids.FindIndex(x => JBGuard.Plain(x) == fw);
            var ours = new HashSet<string>(JBGuard.Mods().Select(m => JBGuard.Plain(m[2])));
            int first = ids.FindIndex(x => ours.Contains(JBGuard.Plain(x)));
            Check(at >= 0 && first == at + 1, "framework " + fw + " active at " + at + ", first guarded mod at " + first);
            Check(ModsConfig.IsActive(fw), "ModsConfig says the framework is active");
            Note("mod list: " + string.Join(" ", ids));
            Note("restart requested (not done in a test)");
            Finish();
        }

        static void Finish()
        {
            if (finished) return; finished = true;
            if (scaleWas >= 0f) Prefs.UIScale = scaleWas;
            Note(fails == 0 ? "GUARD PASS" : "GUARD FAIL " + fails);
            try { System.IO.File.WriteAllLines(Env("JB_GUARD_TEST"), report); } catch (Exception e) { Log.Warning("[JBGUARDTEST] could not write the report: " + e.Message); }
            LongEventHandler.ExecuteWhenFinished(() => Root.Shutdown());
        }
    }
#endif
}
