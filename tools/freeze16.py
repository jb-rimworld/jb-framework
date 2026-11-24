#!/usr/bin/env python3
"""Freeze a mod's RimWorld 1.6 state and move the shared root content to Common/ for 1.7+.

    python3 -I freeze16.py <ModDir> [--only17]

Before: root Defs/Patches/Languages/Textures/Sounds are loaded by 1.6 AND 1.7 (no LoadFolders.xml).
After:  1.6 loads  1.6/Root + 1.6   (byte-identical copy of today's state, never edited again)
        1.7 loads  Common + 1.7     (root content moved here; all new work happens here)
The script proves statically that both versions resolve exactly the same files as before (1.6 cannot run here any more),
writes Source/Frozen16.sha256 and refuses to run twice. --only17: mod has no 1.6 (only move root -> Common, v1.7 entry).
`freeze16.py <ModDir> --check` verifies an already frozen mod against its manifest (used by the release check).
"""
import hashlib, os, shutil, subprocess, sys

CONTENT = ["Defs", "Patches", "Languages", "Textures", "Sounds"]


def sha(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""): h.update(b)
    return h.hexdigest()


def view(mod, folders):
    """What the game resolves: later folders win for the same relative path. Returns {relpath: sha}."""
    out = {}
    for folder in folders:
        base = os.path.join(mod, folder) if folder else mod
        for c in CONTENT + ["Assemblies"]:
            top = os.path.join(base, c)
            for d, _, fs in os.walk(top):
                for f in fs:
                    if f == ".DS_Store": continue
                    p = os.path.join(d, f)
                    out[os.path.relpath(p, base)] = sha(p)
    return out


def manifest(mod):
    rows = []
    for d, _, fs in os.walk(os.path.join(mod, "1.6")):
        for f in sorted(fs):
            if f == ".DS_Store": continue
            p = os.path.join(d, f)
            rows.append(f"{sha(p)}  {os.path.relpath(p, mod)}")
    return sorted(rows, key=lambda r: r.split("  ", 1)[1])


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    only17 = "--only17" in sys.argv
    mod = os.path.abspath(args[0])
    man = os.path.join(mod, "Source", "Frozen16.sha256")
    if "--check" in sys.argv:
        if not os.path.exists(man): print("no manifest (mod has no frozen 1.6)"); return 0
        want = open(man).read().split("\n")
        want = [w for w in want if w]
        got = manifest(mod)
        if want != got:
            for line in sorted(set(want) ^ set(got)): print("DIFF", line)
            print("FROZEN 1.6 CHANGED"); return 1
        print(f"frozen 1.6 intact ({len(got)} files)"); return 0

    if os.path.exists(os.path.join(mod, "LoadFolders.xml")): print("LoadFolders.xml exists: already frozen or manual layout"); return 1
    if os.path.exists(os.path.join(mod, "Common")): print("Common/ exists: stop"); return 1
    about = open(os.path.join(mod, "About", "About.xml"), encoding="utf-8").read()
    has16 = "<li>1.6</li>" in about and not only17
    if has16 and not os.path.isdir(os.path.join(mod, "1.6", "Assemblies")): print("no 1.6/Assemblies"); return 1
    if os.path.isdir(os.path.join(mod, "Assemblies")): print("root Assemblies/ present: manual"); return 1

    old16 = view(mod, ["", "1.6"]) if has16 else None
    old17 = view(mod, ["", "1.7"])
    present = [c for c in CONTENT if os.path.isdir(os.path.join(mod, c))]
    if has16:
        for c in present:
            shutil.copytree(os.path.join(mod, c), os.path.join(mod, "1.6", "Root", c), copy_function=shutil.copy2)
    os.makedirs(os.path.join(mod, "Common"), exist_ok=True)
    git = os.path.isdir(os.path.join(mod, ".git"))
    for c in present:
        if git: subprocess.run(["git", "-C", mod, "mv", c, os.path.join("Common", c)], check=True)
        else: shutil.move(os.path.join(mod, c), os.path.join(mod, "Common", c))
    lf = ['<?xml version="1.0" encoding="utf-8"?>', "<loadFolders>"]
    if has16: lf.append("  <v1.6><li>1.6/Root</li><li>1.6</li></v1.6>")
    lf += ["  <v1.7><li>Common</li><li>1.7</li></v1.7>", "</loadFolders>", ""]
    open(os.path.join(mod, "LoadFolders.xml"), "w", encoding="utf-8").write("\n".join(lf))

    ok = True
    new17 = view(mod, ["Common", "1.7"])
    if new17 != old17: ok = False; print("1.7 VIEW DIFFERS", sorted(set(new17.items()) ^ set(old17.items()))[:10])
    if has16:
        new16 = view(mod, ["1.6/Root", "1.6"])
        if new16 != old16: ok = False; print("1.6 VIEW DIFFERS", sorted(set(new16.items()) ^ set(old16.items()))[:10])
        os.makedirs(os.path.join(mod, "Source"), exist_ok=True)
        open(man, "w").write("\n".join(manifest(mod)) + "\n")
    print(f"{os.path.basename(mod)}: 1.7 view {len(new17)} files" + (f", 1.6 view {len(old16)} files, frozen" if has16 else ", no 1.6") + (" OK" if ok else " FAILED"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
