#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
"""Finds the folders of other mods for tools/jbmod (extras of `test`/`saveload`/…, entries of `compat` sets).

    python3 -I resolve_mods.py <packageId|folder> …

One line per argument:
    GAME    <packageid> <folder>   installed in the game's Mods folder or the Workshop folder (loaded by packageId)
    CACHE   <packageid> <folder>   only in the Rimling compat cache (~/Library/Caches/RimlingModCompat): jbmod links the folder
    DIR     <packageid> <folder>   the argument was a folder of a prebuilt mod (a specific Workshop item, a cached copy): linked as is
    MISSING <packageid>            nowhere
packageIds are written lower-case (RimWorld's ModsConfig.IsActive looks them up lower-case: a mixed-case id in ModsConfig.xml loads
the mod but every IfModActive / IsActive check of it fails). The cache is searched up to 7 levels deep (dl/steamapps/workshop/content/
294100/<id>); with several cached copies of one packageId the newest About.xml wins: pass the folder to pick one.
"""
import glob, os, re, sys

HOME = os.path.expanduser("~")
ROOTS = [HOME + "/Library/Application Support/Steam/steamapps/common/RimWorld/Mods",
         HOME + "/Library/Application Support/Steam/steamapps/workshop/content/294100"]
CACHE = HOME + "/Library/Caches/RimlingModCompat"


def pid_of(about):
    try:
        m = re.search(r"<packageId>\s*([^<\s]+)\s*</packageId>", open(about, encoding="utf-8", errors="ignore").read())
        return m.group(1).lower() if m else None
    except Exception:
        return None


def find(root, pid, depths):
    hits = []
    for d in depths:
        for about in glob.glob(root + "/*" * d + "/About/About.xml"):
            if os.path.basename(os.path.dirname(os.path.dirname(about))).startswith("_jb_"):
                continue   # jbmod's own slot links
            if pid_of(about) == pid:
                hits.append(os.path.dirname(os.path.dirname(about)))
        if hits:
            break
    if not hits:
        return None
    return max(hits, key=lambda h: os.path.getmtime(os.path.join(h, "About", "About.xml")))


def main(argv):
    for a in argv[1:]:
        if os.path.isfile(os.path.join(a, "About", "About.xml")):
            d = os.path.abspath(a)
            print("DIR", pid_of(os.path.join(d, "About", "About.xml")) or "?", d)
            continue
        pid = a.lower()
        hit = next((h for h in (find(r, pid, (1,)) for r in ROOTS) if h), None)
        if hit:
            print("GAME", pid, hit); continue
        hit = find(CACHE, pid, range(1, 8)) if os.path.isdir(CACHE) else None
        print(("CACHE " + pid + " " + hit) if hit else ("MISSING " + pid))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
