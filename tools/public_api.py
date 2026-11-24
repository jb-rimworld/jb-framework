#!/usr/bin/env python3
"""Public API snapshot of JB Framework: api/PublicApi.txt, generated from the built 1.7 DLL.

    python3 -I tools/public_api.py generate [--dll PATH]   write api/PublicApi.txt (after a release that ADDS public API)
    python3 -I tools/public_api.py check    [--dll PATH]   compare the built DLL with api/PublicApi.txt (every release)
    python3 -I tools/public_api.py show     [--dll PATH]   print the current API
    python3 -I tools/public_api.py uses <consumer.dll> [--dll PATH]
                                                           which framework API a dependent mod binds to, the API level that needs
                                                           (api/Since.txt) and the level stamped into the mod; exit 1 when the stamp is lower

Rules (FRAMEWORK.md section 2): the public API is additive only.
  check FAILS (exit 1) when a line of the snapshot is missing from the DLL (removed, renamed or changed member),
  and when lines were added but build/Identity.props JBFrameworkApiLevel was not raised above the snapshot's level.
  check passes with a note when lines were added AND the level was raised: then run `generate` and commit the snapshot.
  check also FAILS when a public method has an optional parameter (a default value is compiled into every caller and blocks
  adding an overload later: write overloads instead).
  generate records every line that is new against the previous snapshot in api/Since.txt ("<level><TAB><line>"); lines not listed
  there are API level 1.
The DLL is listed by tools/PublicApi (Mono.Cecil): one sorted line per public type and public/protected member, including
parameter names and default values. Build the framework first: dotnet build Source/JBFramework -c Release.
SPDX-License-Identifier: MIT
"""
import os, re, subprocess, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TOOL = os.path.join(ROOT, "tools", "PublicApi")
SNAP = os.path.join(ROOT, "api", "PublicApi.txt")
SINCE = os.path.join(ROOT, "api", "Since.txt")


def identity():
    s = open(os.path.join(ROOT, "build", "Identity.props"), encoding="utf-8").read()
    get = lambda k: re.search(r"<%s>([^<]*)</%s>" % (k, k), s).group(1).strip()
    return {"name": get("JBFrameworkName"), "asm": get("JBFrameworkAssemblyName"), "version": get("JBFrameworkVersion"), "level": int(get("JBFrameworkApiLevel"))}


def dotnet():
    env = dict(os.environ)
    env["PATH"] = os.path.expanduser("~/.dotnet") + os.pathsep + env.get("PATH", "")
    env.setdefault("DOTNET_CLI_TELEMETRY_OPTOUT", "1"); env.setdefault("DOTNET_NOLOGO", "1")
    return env


def list_api(dll):
    env = dotnet()
    exe = os.path.join(TOOL, "bin", "PublicApi.dll")
    src_newer = not os.path.exists(exe) or any(os.path.getmtime(os.path.join(TOOL, f)) > os.path.getmtime(exe) for f in ("Program.cs", "PublicApi.csproj"))
    if src_newer:
        r = subprocess.run(["dotnet", "build", TOOL, "-c", "Release", "-o", os.path.join(TOOL, "bin"), "-v", "q", "-nologo"], env=env, capture_output=True, text=True)
        if r.returncode != 0:
            sys.exit("building tools/PublicApi failed:\n" + r.stdout[-3000:] + r.stderr[-2000:])
    if not os.path.exists(dll):
        sys.exit("DLL not found: %s (build first: dotnet build Source/JBFramework -c Release)" % dll)
    r = subprocess.run(["dotnet", exe, dll], env=env, capture_output=True, text=True)
    if r.returncode != 0:
        sys.exit("PublicApi failed:\n" + r.stderr)
    return [l for l in r.stdout.splitlines() if l.strip()]


def read_snapshot():
    if not os.path.exists(SNAP):
        return None, []
    level, lines = None, []
    for l in open(SNAP, encoding="utf-8").read().splitlines():
        m = re.match(r"# api-level (\d+)", l)
        if m: level = int(m.group(1))
        elif l.strip() and not l.startswith("#"): lines.append(l)
    return level, lines


def read_since():
    since = {}
    if os.path.exists(SINCE):
        for l in open(SINCE, encoding="utf-8").read().splitlines():
            if l.strip() and not l.startswith("#") and "\t" in l:
                lv, line = l.split("\t", 1); since[line] = int(lv)
    return since


def optional_params(api):
    return [l for l in api if ": method " in l and re.search(r"\([^)]*=[^)]*\)", l)]


def uses(consumer, dll):
    list_api(dll)   # builds the tool if needed
    r = subprocess.run(["dotnet", os.path.join(TOOL, "bin", "PublicApi.dll"), "--uses", consumer, dll], env=dotnet(), capture_output=True, text=True)
    if r.returncode != 0: sys.exit("PublicApi --uses failed:\n" + r.stderr)
    out = r.stdout.splitlines()
    stamped = int(out[0].split()[-1]) if out and out[0].startswith("# requires") else 0
    used = [l for l in out[1:] if l.strip() and not l.startswith("#")]
    unresolved = [l for l in out[1:] if l.startswith("# unresolved")]
    since = read_since()
    need = max([since.get(l, 1) for l in used] or [1])
    return stamped, need, used, unresolved


def main(argv):
    if len(argv) >= 3 and argv[1] == "uses":
        idn = identity()
        dll = os.path.join(ROOT, "1.7", "Assemblies", idn["asm"] + ".dll")
        if "--dll" in argv: dll = argv[argv.index("--dll") + 1]
        stamped, need, used, unresolved = uses(argv[2], dll)
        for l in used: print("USES  " + l)
        for l in unresolved: print("WARN  " + l[2:] + " (not in this framework build: built against a newer one?)")
        print("%s binds to %d framework members, needs API level %d, stamped %d" % (os.path.basename(argv[2]), len(used), need, stamped))
        if stamped < need:
            print("ERROR the mod uses API level %d but states %d: set <JBMinApiLevel>%d</JBMinApiLevel> in its csproj" % (need, stamped, need)); return 1
        return 1 if unresolved else 0
    if len(argv) < 2 or argv[1] not in ("generate", "check", "show"):
        print(__doc__); return 2
    idn = identity()
    dll = os.path.join(ROOT, "1.7", "Assemblies", idn["asm"] + ".dll")
    if "--dll" in argv: dll = argv[argv.index("--dll") + 1]
    api = list_api(dll)
    if argv[1] == "show":
        print("\n".join(api)); return 0
    opt = optional_params(api)
    if argv[1] == "generate":
        if opt:
            for l in opt: print("OPTIONAL PARAMETER  " + l)
            print("ERROR public methods with optional parameters: write overloads instead"); return 1
        os.makedirs(os.path.dirname(SNAP), exist_ok=True)
        old_level, old = read_snapshot()
        if old_level is not None:
            since = read_since()
            new = sorted(set(api) - set(old))
            if new and idn["level"] > old_level:
                for l in new: since[l] = idn["level"]
                body = ["# Lines of api/PublicApi.txt added after API level 1, with the level that added them. Generated by tools/public_api.py."]
                body += ["%d\t%s" % (lv, l) for l, lv in sorted(since.items(), key=lambda x: (x[1], x[0]))]
                open(SINCE, "w", encoding="utf-8").write("\n".join(body) + "\n")
        head = ["# Public API of %s %s. Generated by tools/public_api.py from the built 1.7 DLL. Do not edit." % (idn["name"], idn["version"]),
                "# Additive only: a line may never disappear or change. New lines need a higher JBFrameworkApiLevel (build/Identity.props).",
                "# api-level %d" % idn["level"]]
        open(SNAP, "w", encoding="utf-8").write("\n".join(head + api) + "\n")
        print("wrote %s (%d lines, api-level %d)" % (os.path.relpath(SNAP, ROOT), len(api), idn["level"])); return 0
    level, snap = read_snapshot()
    if level is None:
        print("ERROR no snapshot yet: run `python3 -I tools/public_api.py generate`"); return 1
    cur = set(api); old = set(snap)
    removed = sorted(old - cur); added = sorted(cur - old)
    rc = 0
    for l in opt: print("OPTIONAL PARAMETER  " + l)
    if opt:
        print("ERROR %d public method(s) with optional parameters: write overloads instead (a default is compiled into every caller)." % len(opt)); rc = 1
    for l in removed: print("REMOVED/CHANGED  " + l)
    if removed:
        print("ERROR %d line(s) of the public API disappeared or changed. The API is additive only: restore them (add new members instead)." % len(removed)); rc = 1
    for l in added: print("ADDED  " + l)
    if added and idn["level"] <= level:
        print("ERROR %d line(s) added but JBFrameworkApiLevel is still %d (snapshot: %d). Raise it in build/Identity.props, rebuild, then `generate`." % (len(added), idn["level"], level)); rc = 1
    elif added:
        print("NOTE %d line(s) added at api-level %d: run `generate` and commit api/PublicApi.txt with the release." % (len(added), idn["level"]))
    if rc == 0 and not added: print("OK public API unchanged (%d lines, api-level %d)" % (len(cur), level))
    return rc


if __name__ == "__main__":
    sys.exit(main(sys.argv))
