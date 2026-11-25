#!/usr/bin/env python3
"""Release preparation for one JB mod (spec SharedKit.md section 4.1). Never uploads anything.

    python3 -I release.py <ModDir> [--version X.Y.Z] [--dry] [--no-build] [--desc] [--gallery] [--title]
                          [--no-wpub-dry] [--release-root DIR] [--date YYYY-MM-DD] [--changenote TEXT] [--csproj FILE] [--strict]

Steps (all checks run, then the first failing group stops before anything is copied):
  1 build      release build with -p:JBTests=false (skip: --no-build). csproj: --csproj, else Source/**: one that imports JBMod.props, else
               *.rw17.csproj, else the only *.csproj (obj/bin/_frozen16 ignored).
  2 dll scan   no test harness in 1.7/, 1.6/ and Common/Assemblies (JBFramework.Testing, KitTestRunner, KitTestSuite, KitSuites, SaveFingerprint, JB_TEST,
               JB_UI_SHOTS, JB_PROBE, HearthTestRunner), ASCII and UTF-16. A dependent mod ships no JBFramework.dll and DEFINES no type in the
               framework's namespace (tools/PublicApi --types; a copied framework source file would shadow the real framework), and the
               API level it states covers every framework member it binds to (tools/public_api.py uses; api/Since.txt).
  2b dependency  a dependent mod lists the framework in modDependencies (with steamWorkshopUrl, checked against the framework's
               About/PublishedFileId.txt once it exists) and in loadAfter.
  2c guard     a dependent mod ships the missing-framework guard: for every version whose DLL needs the framework, each LoadFolders.xml
               entry loads only with it (IfModActive / IfModActiveAll) or is a Guard folder loaded only without it (IfModNotActive);
               Guard/<v>/Assemblies/*.Guard.dll exists, binds to neither the framework nor Harmony, has no test code, and carries the
               framework's Workshop id once About/PublishedFileId.txt of the framework exists (placeholder before: warning).
               The framework itself (a folder with build/Identity.props): built for 1.7 AND 1.6, `tools/public_api.py check` must pass (the
               public API is additive only), and the copy leaves out sample/, build/, api/, TestMod/, guard/ and API.md.
  3 freeze     freeze16.py --check (1.6 byte-identical to Source/Frozen16.sha256); About lists 1.6 but no manifest = error.
  4 layout     LoadFolders.xml (v1.6 if About lists 1.6, v1.7; every listed folder exists), no content dirs in the mod root, About fields
               (packageId, name, author, supportedVersions, modVersion == version), PublishedFileId numeric, Preview.png < 1,000,000 bytes.
  5 defs       static: defName unique per (def type) in each version view (1.7 view errors; the frozen 1.6 view only warns); texPath/uiIconPath/
               iconPath resolve inside the view (a missing file next to existing mod textures is an error, a path with no mod textures nearby
               is a warning "assumed vanilla").
  6 notes      CHANGELOG.md exists, top entry == version, change note = top entry.  --desc: Workshop/description.bbcode through desc_lint.py.
  7 copy       rsync --delete to <release-root>/Upload/<Mod> excluding Source Workshop tools obj bin .git *.csproj _frozen16 *.pre-kit *.pdb
               .DS_Store .gitignore .gitattributes; the copy is verified (no excluded names, 1.6 hashes, DLL scan again).
  8 job        <release-root>/Jobs/<date>-<mod>/job.json in WPub format (ids are numbers; description only with --desc; preview/images/visibility
               from Workshop/job_extra.json only with --gallery; title from it only with --title).
  9 wpub dry   _Tools/WPub/run_dryrun.sh <job> (exit 0 required; it needs one of the 3 shared test slots). The REAL upload command is only
               printed. This script never calls run_real.sh.
--dry: do 1-6 (and the build unless --no-build) but write nothing under the release root and run no WPub dry run; prints the job it would write.
--release-root defaults to <ModDir>/../_Release. --strict: warnings fail too.
Exit 0 = everything passed, 1 = a check failed, 2 = usage.
"""
import datetime, hashlib, importlib.util, json, os, re, shutil, subprocess, sys
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
WPUB = os.path.normpath(os.path.join(HERE, "..", "..", "_Tools", "WPub"))      # 22_RimWorld/_Tools/WPub (framework lives in 22_RimWorld/JBFramework)
CONTENT = ["Defs", "Patches", "Languages", "Textures", "Sounds"]
ROOT_FORBIDDEN = CONTENT + ["Assemblies", "Resources"]
ROOT_ALLOWED = {"About", "Common", "Source", "Workshop", "tools", ".git", ".gitignore", ".gitattributes", "LoadFolders.xml", "CHANGELOG.md",
                "README.md", "LICENSE", "LICENSE.md", "LICENSE.txt", "CREDITS.md", "AGENTS.md", ".DS_Store"}
FORBIDDEN_STRINGS = ["JBFramework.Testing", "KitTestRunner", "KitTestSuite", "KitSuites", "SaveFingerprint", "JB_TEST", "JB_UI_SHOTS", "JB_PROBE",
                     "HearthTestRunner"]
# the framework DLL names its test assembly in [InternalsVisibleTo("JBFramework.Testing")]: that one string is allowed there
FRAMEWORK_ALLOWED_STRINGS = {"JBFramework.Testing"}
FRAMEWORK_EXCLUDES = ["/sample/", "/build/", "/api/", "/TestMod/", "/src/", "/docs/", "/guard/", "/API.md"]
FRAMEWORK_ROOT_ALLOWED = {"sample", "build", "api", "TestMod", "docs", "guard", "API.md"}
RSYNC_EXCLUDES = ["/Source/", "/Workshop/", "/tools/", "obj/", "bin/", ".git/", "*.csproj", "_frozen16/", "*.pre-kit", "*.pdb", ".DS_Store",
                  ".gitignore", ".gitattributes"]
TEX_TAGS = {"texPath", "uiIconPath", "iconPath"}
TEX_EXT = (".png", ".jpg", ".jpeg", ".psd", ".tga", ".bmp")


def load(name):
    spec = importlib.util.spec_from_file_location(name, os.path.join(HERE, name + ".py"))
    m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m); return m


class Report:
    def __init__(self): self.errors, self.warns, self.group = [], [], ""
    def err(self, m): self.errors.append(f"[{self.group}] {m}"); print("ERROR", f"[{self.group}]", m)
    def warn(self, m): self.warns.append(f"[{self.group}] {m}"); print("WARN ", f"[{self.group}]", m)
    def ok(self, m): print("ok   ", f"[{self.group}]", m)


def sha(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""): h.update(b)
    return h.hexdigest()


def read_about(mod):
    p = os.path.join(mod, "About", "About.xml")
    r = ET.parse(p).getroot()
    g = lambda t: (r.findtext(t) or "").strip()
    return {"name": g("name"), "author": g("author"), "packageId": g("packageId"), "modVersion": g("modVersion"), "description": r.findtext("description") or "",
            "versions": [e.text.strip() for e in r.findall("./supportedVersions/li") if e.text and e.text.strip()]}


def load_folders(mod):
    """-> {'1.6': ['1.6/Root','1.6'], '1.7': [...]} or None when no LoadFolders.xml"""
    p = os.path.join(mod, "LoadFolders.xml")
    if not os.path.isfile(p): return None
    out = {}
    for v in ET.parse(p).getroot():
        if re.fullmatch(r"v\d+\.\d+", v.tag):
            out[v.tag[1:]] = [(li.text or "").strip().strip("/") for li in v.findall("li")]
    return out


def find_csproj(mod, forced):
    if forced: return forced
    cands = []
    for d, ds, fs in os.walk(os.path.join(mod, "Source")):
        ds[:] = [x for x in ds if x not in ("obj", "obj17", "bin", "_frozen16")]
        cands += [os.path.join(d, f) for f in fs if f.endswith(".csproj")]
    if not cands: return None
    kit = [c for c in cands if "JBMod.props" in open(c, encoding="utf-8", errors="replace").read()]
    rw = [c for c in cands if c.endswith(".rw17.csproj")]
    for pool in (kit, rw, cands):
        if len(pool) == 1: return pool[0]
        if len(pool) > 1: return "AMBIGUOUS:" + ", ".join(os.path.relpath(c, mod) for c in pool)
    return None


def is_framework(mod):
    return os.path.isfile(os.path.join(mod, "build", "Identity.props"))


def fw_identity():
    s = open(os.path.join(HERE, "..", "build", "Identity.props"), encoding="utf-8").read()
    return {k: re.search(r"<%s>([^<]*)</%s>" % (k, k), s).group(1).strip() for k in ("JBFrameworkNamespace", "JBFrameworkAssemblyName")}


def scan_dll(path, framework=False):
    data = open(path, "rb").read()
    return [s for s in FORBIDDEN_STRINGS if (s.encode() in data or s.encode("utf-16-le") in data) and not (framework and s in FRAMEWORK_ALLOWED_STRINGS)]


def defined_types(dll):
    """Full names of the types DEFINED in a DLL (tools/PublicApi --types), or None when the tool cannot run."""
    pa = load("public_api")
    try:
        pa.list_api(os.path.join(HERE, "..", "1.7", "Assemblies", fw_identity()["JBFrameworkAssemblyName"] + ".dll"))   # builds the tool if needed
    except SystemExit as e:
        print("   ", e); return None
    p = subprocess.run(["dotnet", os.path.join(pa.TOOL, "bin", "PublicApi.dll"), "--types", dll], env=pa.dotnet(), capture_output=True, text=True)
    return [l.strip() for l in p.stdout.splitlines() if l.strip()] if p.returncode == 0 else None


def uses_framework(dll):
    asm = fw_identity()["JBFrameworkAssemblyName"]
    data = open(dll, "rb").read()
    return asm.encode() in data


def step_dependency(mod, R):
    """A dependent mod lists the framework in About.xml with a Workshop link (players without it get a subscribe button)."""
    R.group = "dependency"
    if is_framework(mod): return
    s = open(os.path.join(HERE, "..", "build", "Identity.props"), encoding="utf-8").read()
    fwpid = re.search(r"<JBFrameworkPackageId>([^<]*)</JBFrameworkPackageId>", s).group(1).strip().lower()
    r = ET.parse(os.path.join(mod, "About", "About.xml")).getroot()
    deps = [li for li in r.findall("./modDependencies/li")] + [li for li in r.findall("./modDependenciesByVersion/*/li")]
    mine = [li for li in deps if (li.findtext("packageId") or "").strip().lower() == fwpid]
    built = any(os.path.isfile(os.path.join(mod, v, "Assemblies", f)) and uses_framework(os.path.join(mod, v, "Assemblies", f))
                for v in ("1.7", "1.6") if os.path.isdir(os.path.join(mod, v, "Assemblies")) for f in os.listdir(os.path.join(mod, v, "Assemblies")) if f.endswith(".dll"))
    if not mine:
        (R.err if built else R.ok)(f"About.xml lists no modDependency {fwpid}" + ("" if built else " (the mod does not use the framework)")); return
    load_after = [(e.text or "").strip().lower() for e in r.findall("./loadAfter/li")]
    if fwpid not in load_after: R.err(f"About.xml: add <li>{fwpid}</li> to <loadAfter>")
    fid_file = os.path.join(HERE, "..", "About", "PublishedFileId.txt")
    fid = open(fid_file).read().strip() if os.path.isfile(fid_file) else ""
    for li in mine:
        url = (li.findtext("steamWorkshopUrl") or "").strip()
        if not url: R.err(f"About.xml: the {fwpid} dependency has no <steamWorkshopUrl> (players without the framework get no subscribe link)")
        elif "JBFRAMEWORK_WORKSHOP_ID" in url:
            (R.err if fid else R.warn)(f"About.xml: steamWorkshopUrl still holds the placeholder JBFRAMEWORK_WORKSHOP_ID" + (f": put {fid} there" if fid else " (the framework is not uploaded yet)"))
        elif fid and not url.endswith("/" + fid): R.err(f"About.xml: steamWorkshopUrl {url} does not point at the framework's Workshop id {fid}")
        elif not fid: R.warn("the framework has no About/PublishedFileId.txt yet: steamWorkshopUrl not checked against it")
        else: R.ok(f"framework dependency with Workshop link {url}")
    guard_check(mod, fwpid, R)


def guard_check(mod, fwpid, R):
    """The missing-framework guard (README "Ship the guard with your mod"): for every version whose DLL needs the framework, each
    LoadFolders.xml entry loads only WITH the framework (IfModActive / IfModActiveAll naming it) or is a Guard folder loaded only WITHOUT it
    (IfModNotActive), and the guard DLL of that version exists, references neither the framework nor Harmony and carries the framework's
    Workshop id once the framework has one."""
    R.group = "guard"
    p = os.path.join(mod, "LoadFolders.xml")
    if not os.path.isfile(p):
        R.err("no LoadFolders.xml: every folder loads even when JB Framework is missing (red errors, a save loses the mod's things); "
              "README: Ship the guard with your mod"); return
    fid_file = os.path.join(HERE, "..", "About", "PublishedFileId.txt")
    fid = open(fid_file).read().strip() if os.path.isfile(fid_file) else ""
    ids = lambda li, a: [x.strip().lower() for x in (li.get(a) or "").split(",") if x.strip()]
    for v in ET.parse(p).getroot():
        if not re.fullmatch(r"v\d+\.\d+", v.tag): continue
        ver = v.tag[1:]
        lis = v.findall("li")
        folders = [(li.text or "").strip().strip("/") for li in lis]
        # a version needs the guard when one of its folders holds a DLL that binds to the framework (a frozen 1.6 of the old mod does not)
        needs = False
        for f in folders:
            ad = os.path.join(mod, f, "Assemblies")
            if os.path.isdir(ad) and any(uses_framework(os.path.join(ad, x)) for x in os.listdir(ad) if x.endswith(".dll") and not x.endswith(".Guard.dll")):
                needs = True
        if not needs: R.ok(f"<v{ver}>: no DLL that needs the framework, no guard needed"); continue
        guard, bad = [], []
        for li, f in zip(lis, folders):
            anyof, allof, noneof = ids(li, "IfModActive"), ids(li, "IfModActiveAll"), ids(li, "IfModNotActive")
            with_fw = (anyof and all(x == fwpid for x in anyof)) or fwpid in allof
            without_fw = fwpid in noneof
            if without_fw and not with_fw:
                if f.split("/")[0] != "Guard": bad.append(f"'{f}' loads only without the framework but is not a Guard folder")
                else: guard.append(f)
            elif not with_fw: bad.append(f"'{f}' loads without JB Framework (IfModActive=\"{fwpid}\" or IfModActiveAll=\"{fwpid},<other>\" missing)")
            elif f.split("/")[0] == "Guard": bad.append(f"'{f}' is a Guard folder but loads with the framework")
        for b in bad: R.err(f"<v{ver}>: {b}")
        if not guard: R.err(f"<v{ver}>: no Guard folder (IfModNotActive=\"{fwpid}\"): a player without JB Framework gets red errors and no explanation"); continue
        dlls = [os.path.join(mod, g, "Assemblies", x) for g in guard if os.path.isdir(os.path.join(mod, g, "Assemblies")) for x in os.listdir(os.path.join(mod, g, "Assemblies")) if x.endswith(".Guard.dll")]
        if not dlls: R.err(f"<v{ver}>: no *.Guard.dll in {', '.join(guard)} (set <JBGuard>true</JBGuard> in the csproj and build for {ver})"); continue
        for d in dlls:
            data = open(d, "rb").read()
            rel = os.path.relpath(d, mod)
            if b"0Harmony" in data or uses_framework(d): R.err(f"{rel} references Harmony or the framework (it must load without both)")
            if scan_dll(d): R.err(f"{rel} contains test code (built with JBGuardTests=true?)")
            if b"JBFRAMEWORK_WORKSHOP_ID" in data or "JBFRAMEWORK_WORKSHOP_ID".encode("utf-16-le") in data:
                (R.err if fid else R.warn)(f"{rel} holds the Workshop id placeholder" + (f": rebuild (the framework's id {fid} is in its About/PublishedFileId.txt now)" if fid else " (framework not uploaded yet: its install button opens the GitHub page)"))
            elif fid and fid.encode("utf-16-le") not in data: R.err(f"{rel} does not carry the framework's Workshop id {fid}: rebuild")
        langs = [g for g in guard if os.path.isdir(os.path.join(mod, g, "Languages", "English"))]
        if not langs: R.err(f"<v{ver}>: the guard has no English texts (Guard/Languages/English/Keyed, written by the build)")
        if not bad: R.ok(f"<v{ver}>: every folder needs the framework, guard {', '.join(guard)} ({len(dlls)} DLL)")


def step_build(mod, a, R):
    R.group = "build"
    cs = find_csproj(mod, a.get("--csproj"))
    if cs is None: R.warn("no csproj under Source/: nothing to build (assemblies used as they are)"); return
    if cs.startswith("AMBIGUOUS:"): R.err("several csproj files, pass --csproj: " + cs[10:]); return
    env = dict(os.environ, PATH=os.path.expanduser("~/.dotnet") + os.pathsep + os.environ.get("PATH", ""), DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
    versions = [None, "1.6"] if is_framework(mod) else [None]   # the framework ships a 1.6 DLL too (compile-only, best effort)
    for v in versions:
        cmd = ["dotnet", "build", cs, "-c", "Release", "-nologo", "-v", "q", "-clp:ErrorsOnly", "-p:JBTests=false", "-p:JBRelease=true"] + ([f"-p:RimVersion={v}"] if v else [])
        print("$", " ".join(cmd))
        p = subprocess.run(cmd, env=env, capture_output=True, text=True)
        if p.returncode != 0:
            for l in (p.stdout + p.stderr).splitlines()[-25:]: print("   ", l)
            R.err(f"dotnet build failed ({os.path.relpath(cs, mod)}{', RimWorld ' + v if v else ''})")
        else:
            R.ok(f"built {os.path.relpath(cs, mod)}{' for RimWorld ' + v + ' (compile-only)' if v else ''}")


def step_dll(mod, R, label="dll scan"):
    R.group = label
    dlls = []
    for d in (os.path.join(mod, "1.7", "Assemblies"), os.path.join(mod, "1.6", "Assemblies"), os.path.join(mod, "Common", "Assemblies")):
        if os.path.isdir(d): dlls += [os.path.join(d, f) for f in sorted(os.listdir(d)) if f.endswith(".dll")]
    guards = [os.path.join(dp, f) for v in ("1.7", "1.6") for dp in [os.path.join(mod, "Guard", v, "Assemblies")] if os.path.isdir(dp) for f in sorted(os.listdir(dp)) if f.endswith(".dll")]
    for g in guards:
        hits = scan_dll(g)
        if hits: R.err(f"{os.path.relpath(g, mod)} contains test code: {', '.join(hits)} (built with JBGuardTests=true?)")
        else: R.ok(f"{os.path.relpath(g, mod)} clean")
    if not any(os.sep + "1.7" + os.sep in d or os.sep + "Common" + os.sep in d for d in dlls): R.warn("no DLL in 1.7/Assemblies")
    if not dlls: return
    fw = is_framework(mod)
    ident = fw_identity()
    ns, asm = ident["JBFrameworkNamespace"], ident["JBFrameworkAssemblyName"]
    for d in dlls:
        base = os.path.basename(d).lower()
        if base.startswith(asm.lower() + ".testing"):
            R.err(f"{os.path.relpath(d, mod)}: the test harness never ships (it lives in the local TestMod/)"); continue
        if base == asm.lower() + ".dll" and not fw:
            R.err(f"{os.path.relpath(d, mod)}: a dependent mod must not ship JB Framework (reference it with Private=false; build/JBMod.props does)")
            continue
        hits = scan_dll(d, framework=fw and base == asm.lower() + ".dll")
        if hits: R.err(f"{os.path.relpath(d, mod)} contains test code: {', '.join(hits)} (built with JBTests=true?)")
        else: R.ok(f"{os.path.relpath(d, mod)} clean")
        if not fw:
            types = defined_types(d)
            if types is None: R.err(f"{os.path.relpath(d, mod)}: could not list its types (tools/PublicApi)")
            else:
                bad = [t for t in types if t == ns or t.startswith(ns + ".")]
                if bad: R.err(f"{os.path.relpath(d, mod)} defines framework types (copied framework source?): {', '.join(bad[:8])}")
                else: R.ok(f"{os.path.relpath(d, mod)} defines no {ns}.* type ({len(types)} types)")
            if os.sep + "1.7" + os.sep in d and uses_framework(d):
                pa = load("public_api")
                try: stamped, need, used, unresolved = pa.uses(d, os.path.join(HERE, "..", "1.7", "Assemblies", asm + ".dll"))
                except SystemExit as e: R.err(f"{os.path.relpath(d, mod)}: could not list its framework use ({e})"); continue
                if unresolved: R.err(f"{os.path.relpath(d, mod)} binds to {len(unresolved)} member(s) this framework build does not have: {unresolved[0][2:]}")
                if stamped < need: R.err(f"{os.path.relpath(d, mod)} uses API level {need} but states {stamped}: set <JBMinApiLevel>{need}</JBMinApiLevel> in its csproj")
                else: R.ok(f"{os.path.relpath(d, mod)}: {len(used)} framework members, needs API level {need}, states {stamped}")
    if fw:
        R.group = "public api"
        p = subprocess.run([sys.executable, "-I", os.path.join(HERE, "public_api.py"), "check"], capture_output=True, text=True)
        for l in (p.stdout + p.stderr).splitlines()[-12:]: print("   ", l)
        (R.ok if p.returncode == 0 else R.err)(f"tools/public_api.py check exit {p.returncode}")


def step_freeze(mod, about, R):
    R.group = "freeze"
    man = os.path.join(mod, "Source", "Frozen16.sha256")
    if "1.6" in about["versions"] and not os.path.isfile(man):
        cs = find_csproj(mod, None)
        built16 = is_framework(mod) or (cs and not cs.startswith("AMBIGUOUS:") and "JBMod.props" in open(cs, encoding="utf-8", errors="replace").read())
        if built16:
            # a framework mod (or the framework) builds 1.6 from source (compile-only, best effort): the DLL must exist, nothing is frozen
            dll16 = [f for f in (os.listdir(os.path.join(mod, "1.6", "Assemblies")) if os.path.isdir(os.path.join(mod, "1.6", "Assemblies")) else []) if f.endswith(".dll")]
            (R.ok if dll16 else R.err)(f"1.6 built from source (compile-only, best effort): {', '.join(dll16) or 'no DLL in 1.6/Assemblies (jbmod <Mod> build)'}")
            return
        R.err("About lists 1.6 but Source/Frozen16.sha256 is missing (run freeze16.py)"); return
    if not os.path.isfile(man):
        R.ok("no frozen 1.6 (About does not list it)"); return
    p = subprocess.run([sys.executable, "-I", os.path.join(HERE, "freeze16.py"), mod, "--check"], capture_output=True, text=True)
    out = (p.stdout + p.stderr).strip()
    (R.ok if p.returncode == 0 else R.err)(out.splitlines()[-1] if out else "freeze16 --check printed nothing")
    if p.returncode != 0:
        for l in out.splitlines()[:15]: print("   ", l)


def step_layout(mod, about, version, R):
    R.group = "layout"
    lf = load_folders(mod)
    if lf is None: R.err("LoadFolders.xml missing")
    else:
        for v in about["versions"]:
            if v in ("1.6", "1.7") and v not in lf: R.err(f"About lists {v} but LoadFolders.xml has no <v{v}> entry")
            elif v not in lf: R.warn(f"About lists {v}: no <v{v}> entry in LoadFolders.xml (game falls back to default loading)")
        if "1.7" in lf and "1.7" not in about["versions"]: R.warn("LoadFolders has <v1.7> but About does not list 1.7")
        for v, folders in lf.items():
            for f in folders:
                if not os.path.isdir(os.path.join(mod, f) if f else mod): R.err(f"LoadFolders <v{v}> lists '{f}' which does not exist")
    for c in ROOT_FORBIDDEN:
        d = os.path.join(mod, c)
        if os.path.isdir(d) and any(files for _, _, files in os.walk(d)):
            R.err(f"content in the mod root: {c}/ (loaded by no version once LoadFolders.xml exists; move it to Common/)")
    # a root folder that LoadFolders.xml names (also in a conditional <li IfModActive=…> entry, e.g. Areas/<Id>) is mod content
    named = {f.replace("\\", "/").split("/")[0] for fs in (lf or {}).values() for f in fs if f}
    for n in sorted(os.listdir(mod)):
        if n in ROOT_ALLOWED or n in ROOT_FORBIDDEN or re.fullmatch(r"\d+\.\d+", n) or n in named: continue
        if is_framework(mod) and n in FRAMEWORK_ROOT_ALLOWED: continue
        R.warn(f"unexpected entry in the mod root: {n}")
    a = about
    if not a["name"]: R.err("About: <name> missing")
    if not a["author"]: R.err("About: <author> missing")
    if not re.fullmatch(r"[A-Za-z0-9_\-]+(\.[A-Za-z0-9_\-]+)+", a["packageId"] or ""): R.err(f"About: packageId {a['packageId']!r} missing or malformed")
    if not a["versions"]: R.err("About: supportedVersions empty")
    if not a["modVersion"]: (R.err if version else R.warn)("About: <modVersion> missing")
    elif version and a["modVersion"] != version: R.err(f"About modVersion {a['modVersion']} != release version {version}")
    if len(a["description"]) > 1500: R.warn(f"About description is {len(a['description'])} characters (keep ~1500)")
    pid = os.path.join(mod, "About", "PublishedFileId.txt")
    if not os.path.isfile(pid): R.warn("About/PublishedFileId.txt missing: first upload, the job CREATES a Workshop item (write its id into the file afterwards)")
    elif not open(pid).read().strip().isdigit(): R.err("About/PublishedFileId.txt is not numeric")
    pv = os.path.join(mod, "About", "Preview.png")
    if not os.path.isfile(pv): R.err("About/Preview.png missing")
    elif os.path.getsize(pv) >= 1_000_000: R.err(f"About/Preview.png is {os.path.getsize(pv)} bytes (< 1,000,000 needed)")
    if not R.errors or all(not e.startswith("[layout]") for e in R.errors): R.ok("layout")


def parse_defs(folder_paths, R, fatal):
    """-> defs {(tag, defName): [file,...]}, texrefs [(file, tag, value)]"""
    defs, refs = {}, []
    for base in folder_paths:
        dd = os.path.join(base, "Defs")
        for d, _, fs in os.walk(dd):
            for f in sorted(fs):
                if not f.endswith(".xml"): continue
                p = os.path.join(d, f)
                try: root = ET.parse(p).getroot()
                except ET.ParseError as e:
                    (R.err if fatal else R.warn)(f"{os.path.relpath(p, base)}: XML does not parse: {e}"); continue
                for dfn in root:
                    if not isinstance(dfn.tag, str): continue
                    name = dfn.findtext("defName")
                    if name and name.strip(): defs.setdefault((dfn.tag.split(".")[-1], name.strip()), []).append(p)
                    for el in dfn.iter():
                        if el.tag in TEX_TAGS and el.text and el.text.strip(): refs.append((p, el.tag, el.text.strip()))
    return defs, refs


def tex_index(folder_paths):
    files = {}
    for base in folder_paths:
        t = os.path.join(base, "Textures")
        for d, _, fs in os.walk(t):
            for f in fs:
                if f.lower().endswith(TEX_EXT):
                    rel = os.path.relpath(os.path.join(d, f), t).replace(os.sep, "/")
                    files[os.path.splitext(rel)[0]] = rel
    return files


def step_defs(mod, R):
    R.group = "defs"
    lf = load_folders(mod)
    if lf is None: R.warn("no LoadFolders.xml: defs lint skipped"); return
    for v in sorted(lf):
        fatal = v != "1.6"          # 1.6 is frozen: nothing can be fixed there, so its findings are warnings
        L = R.err if fatal else R.warn
        paths = [os.path.join(mod, f) if f else mod for f in lf[v]]
        defs, refs = parse_defs(paths, R, fatal)
        n = 0
        for (tag, name), files in sorted(defs.items()):
            if len(files) > 1:
                n += 1; L(f"view {v}: duplicate {tag} '{name}' in {', '.join(os.path.relpath(f, mod) for f in files)}")
        tex = tex_index(paths)
        low = {k.lower(): k for k in tex}
        dirs = {k.rsplit("/", 1)[0] for k in tex if "/" in k}
        miss = 0
        for f, tag, val in refs:
            if "{" in val or val.startswith("http"): continue
            norm = val.replace("\\", "/").strip("/")
            ok = norm in tex or any(k.startswith(norm + "_") or k.startswith(norm + "/") for k in tex)
            if ok: continue
            miss += 1
            where = f"{os.path.relpath(f, mod)} <{tag}>{val}"
            if norm.lower() in low or any(k.lower().startswith(norm.lower() + "_") or k.lower().startswith(norm.lower() + "/") for k in tex):
                L(f"view {v}: texture path differs only in case (breaks on Linux): {where}")
            elif norm.rsplit("/", 1)[0] in dirs if "/" in norm else False:
                L(f"view {v}: texture missing next to the mod's own textures: {where}")
            else:
                R.warn(f"view {v}: texture not in the mod, assumed vanilla/other mod: {where}")
        R.ok(f"view {v}: {len(defs)} defs, {len(refs)} texture references, {n} duplicate defs, {miss} unresolved textures")


def step_notes(mod, about, version, a, R, changelog):
    R.group = "notes"
    note = a.get("--changenote")
    try:
        entries = changelog.parse(os.path.join(mod, "CHANGELOG.md"))
        if note is None: note = changelog.note(entries)
        top = entries[0]["version"]
        if version and top != version: R.err(f"top CHANGELOG entry {top} != release version {version}")
        elif not version: version = top
        if not entries[0]["date"]: R.warn("top CHANGELOG entry has no date")
    except ValueError as e:
        if note is None: R.err(f"{e} (or pass --changenote)")
        else: R.warn(str(e))
    desc_path = os.path.join(mod, "Workshop", "description.bbcode")
    if "--desc" in a:
        if not os.path.isfile(desc_path): R.err("--desc: Workshop/description.bbcode missing")
        else:
            dl = load("desc_lint")
            text = open(desc_path, encoding="utf-8").read()
            keys = None
            ex = os.path.join(mod, "Workshop", "job_extra.json")
            if "--gallery" in a and os.path.isfile(ex): keys = {i["key"] for i in json.load(open(ex)).get("images", [])}
            elif "--gallery" in a: keys = set()
            else: keys = {k for k in re.findall(r"\[\[IMG:([A-Za-z0-9_\-]+)\]\]", text)}      # keys unchecked without gallery
            res = dl.lint(text, set(about["versions"]), keys)
            for lvl, ln, msg in res: (R.err if lvl == "ERROR" else R.warn)(f"description.bbcode:{ln}: {msg}")
            if not any(r[0] == "ERROR" for r in res): R.ok(f"description.bbcode {dl.size_of(text)} bytes")
            try:
                if entries and changelog.inject(text, changelog.block(entries)) != text:
                    R.warn("description Changelog block differs from CHANGELOG.md (changelog.py inject)")
            except ValueError: pass
    return note, version


def step_copy(mod, dest, R, dry):
    R.group = "copy"
    if os.path.realpath(dest) == os.path.realpath(mod) or os.path.realpath(dest).startswith(os.path.realpath(mod) + os.sep):
        R.err("destination is the mod itself or inside it"); return False
    if dry:
        R.ok(f"dry: would rsync --delete to {dest}"); return True
    os.makedirs(dest, exist_ok=True)
    excl = RSYNC_EXCLUDES + (FRAMEWORK_EXCLUDES if is_framework(mod) else [])
    cmd = ["rsync", "-a", "--delete", "--delete-excluded"] + [f"--exclude={e}" for e in excl] + [mod.rstrip("/") + "/", dest.rstrip("/") + "/"]
    p = subprocess.run(cmd, capture_output=True, text=True)
    if p.returncode: R.err("rsync failed: " + p.stderr.strip()); return False
    bad = []
    n = 0
    for d, ds, fs in os.walk(dest):
        rel = os.path.relpath(d, dest)
        for x in ds + fs:
            r = os.path.normpath(os.path.join(rel, x))
            if x in ("obj", "bin", ".git", "_frozen16", ".DS_Store", ".gitignore") or x.endswith((".csproj", ".pre-kit", ".pdb")) or r in ("Source", "Workshop", "tools"): bad.append(r)
            if is_framework(mod) and r in ("sample", "build", "api", "TestMod", "src", "API.md"): bad.append(r)
        n += len(fs)
    if bad: R.err("excluded names in the copy: " + ", ".join(bad[:10]))
    for d, _, fs in os.walk(os.path.join(mod, "1.6")):
        for f in fs:
            if f == ".DS_Store": continue
            s = os.path.join(d, f); t = os.path.join(dest, os.path.relpath(s, mod))
            if not os.path.isfile(t) or sha(s) != sha(t): R.err(f"1.6 file differs in the copy: {os.path.relpath(s, mod)}")
    for d in ("1.7", "1.6", "Common"):
        for root, _, fs in os.walk(os.path.join(dest, d)):
            for f in fs:
                if f.endswith(".dll"):
                    if f.lower().startswith(fw_identity()["JBFrameworkAssemblyName"].lower() + ".testing"): R.err(f"copy: {f} is the test harness"); continue
                    hits = scan_dll(os.path.join(root, f), framework=is_framework(mod) and f.lower() == fw_identity()["JBFrameworkAssemblyName"].lower() + ".dll")
                    if hits: R.err(f"copy: {f} contains test code {hits}")
    if not any(e.startswith("[copy]") for e in R.errors): R.ok(f"{n} files copied to {dest}")
    return not any(e.startswith("[copy]") for e in R.errors)


def make_job(mod, about, dest, note, a):
    pidf = os.path.join(mod, "About", "PublishedFileId.txt")
    ex = {}
    exp = os.path.join(mod, "Workshop", "job_extra.json")
    if os.path.isfile(exp): ex = json.load(open(exp, encoding="utf-8"))
    # first upload (no PublishedFileId yet): a WPub "create" job with title and preview; later releases update by id
    if os.path.isfile(pidf): item = {"id": int(open(pidf).read().strip()), "name": os.path.basename(mod)}
    else:
        item = {"create": True, "name": os.path.basename(mod), "title": ex.get("title") or about.get("name") or os.path.basename(mod),
                "preview": os.path.join(mod, "About", "Preview.png")}
    if "--title" in a and ex.get("title"): item["title"] = ex["title"]
    if "--gallery" in a:
        absf = lambda p: p if os.path.isabs(p) else os.path.normpath(os.path.join(mod, "Workshop", p))
        if ex.get("preview"): item["preview"] = absf(ex["preview"])
        if ex.get("images"): item["images"] = [{"key": i["key"], "path": absf(i["path"])} for i in ex["images"]]
        if ex.get("visibility"): item["visibility"] = ex["visibility"]
    if "--desc" in a: item["description"] = os.path.join(mod, "Workshop", "description.bbcode")
    item["changenote"] = note
    item["content"] = dest
    item["tags"] = ["Mod"] + sorted(about["versions"], key=lambda v: [int(x) for x in v.split(".")], reverse=True)
    return {"items": [item]}


def main(argv):
    flags = {"--dry", "--no-build", "--desc", "--gallery", "--title", "--no-wpub-dry", "--strict"}
    valued = {"--version", "--release-root", "--date", "--changenote", "--csproj"}
    a, pos, it = {}, [], iter(argv[1:])
    for x in it:
        if x in flags: a[x] = True
        elif x in valued: a[x] = next(it, None)
        elif x.startswith("--"): print("unknown option", x); return 2
        else: pos.append(x)
    if len(pos) != 1: print(__doc__); return 2
    mod = os.path.abspath(pos[0])
    if not os.path.isfile(os.path.join(mod, "About", "About.xml")): print("not a mod folder (About/About.xml missing):", mod); return 2
    name = os.path.basename(mod)
    rroot = os.path.abspath(a.get("--release-root") or os.path.join(os.path.dirname(mod), "_Release"))
    dry = "--dry" in a
    changelog = load("changelog")
    R = Report()
    about = read_about(mod)
    version = a.get("--version") or about["modVersion"] or None
    print(f"== release {name} {version or '(version from CHANGELOG)'}{' [dry]' if dry else ''}")

    if "--no-build" not in a: step_build(mod, a, R)
    if R.errors: return summary(R, a)
    step_dll(mod, R); step_dependency(mod, R); step_freeze(mod, about, R); step_layout(mod, about, version, R); step_defs(mod, R)
    note, version = step_notes(mod, about, version, a, R, changelog)
    if R.errors or ("--strict" in a and R.warns): return summary(R, a)

    dest = os.path.join(rroot, "Upload", name)
    if not step_copy(mod, dest, R, dry): return summary(R, a)
    R.group = "job"
    job = make_job(mod, about, dest, note, a)
    jdir = os.path.join(rroot, "Jobs", f"{a.get('--date') or datetime.date.today().isoformat()}-{name}")
    jpath = os.path.join(jdir, "job.json")
    if dry:
        print(f"dry: would write {jpath}:\n" + json.dumps(job, indent=1, ensure_ascii=False)); R.ok("dry run complete, nothing written")
        return summary(R, a)
    os.makedirs(jdir, exist_ok=True)
    json.dump(job, open(jpath, "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    R.ok(f"wrote {jpath}")
    R.group = "wpub dry"
    if "--no-wpub-dry" in a: R.warn("WPub dry run skipped (--no-wpub-dry)")
    else:
        sh = os.path.join(WPUB, "run_dryrun.sh")
        if not os.path.isfile(sh): R.err(f"{sh} not found")
        else:
            print("$", sh, jpath, "(waits for a free test slot)")
            p = subprocess.run(["/bin/bash", sh, jpath], capture_output=True, text=True)
            for l in (p.stdout + p.stderr).splitlines()[-12:]: print("   ", l)
            (R.ok if p.returncode == 0 else R.err)(f"WPub dry run exit {p.returncode}")
    real = os.path.join(WPUB, "run_real.sh")
    print(f"\nReal upload (ONLY after Julius says go; not run by this tool):\n  /bin/bash {real} {jpath}")
    return summary(R, a)


def summary(R, a):
    print(f"\n{len(R.errors)} errors, {len(R.warns)} warnings")
    bad = R.errors or ("--strict" in a and R.warns)
    print("RELEASE CHECK FAILED" if bad else "RELEASE CHECK PASSED")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
