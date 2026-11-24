#!/usr/bin/env python3
"""Incremental translation for a JB mod (generalised from Hearth & Harbor tools/i18n/delta.py).

    python3 -I i18n_delta.py <ModDir> extract                 English Keyed + def fields -> <ModDir>/Source/i18n/src/<Module>.json
                                                              (the previous src/ is kept as src_prev/ for `plan`)
    python3 -I i18n_delta.py <ModDir> plan [old_src_dir]      -> Source/i18n/delta/<code>/<n>.json   {"k:key" | "d:DefType/defName.field": English}
                                                              todo = keys that are new since old_src_dir (default src_prev/), whose English text
                                                              changed, or that the language lacks (without a baseline: only the lacking ones)
    python3 -I i18n_delta.py <ModDir> apply [--dry]           <- Source/i18n/delta_out/<code>/<n>.json (same keys, translated values)
Options: --prefix XX (file naming <XX>_<Module>.xml; default: KeyPrefix = "XX" found in the mod's C#)  --langs de,fr,es  (default: German plus every
language folder the mod already has; codes: de fr es pt ru uk cs zh ja ko)

Files: language files are written in place. A key that already exists in some file of that language (Keyed, or DefInjected/<DefType>) is
replaced where it is; new keys go to Keyed/<XX>_<Module>.xml or DefInjected/<DefType>/<XX>_<Module>.xml. Placeholders ({0}, {name}, [token], <color>)
must equal the English ones, else English is kept and the key is listed in delta/review_<code>.txt.

Deletion is deliberately narrow (the 1.1.0 incident in H&H wiped hand-written German venue names): apply only ever deletes keys it KNOWS, that is
keys listed in Source/i18n/known.json (written by the previous successful apply as "every key src had then") that are no longer in src. A key it
has never seen (hand-written, from rulesStrings, scenario parts, the kit's generated <XX>_Kit.xml of another language, ...) is never removed.
Without known.json (first run) nothing is deleted.
"""
import json, os, re, sys
import xml.etree.ElementTree as ET
from pathlib import Path
from xml.sax.saxutils import escape

LANGS = {"de": ("German", "German (Deutsch)"), "fr": ("French", "French (Français)"),
         "es": ("Spanish", "Spanish (Español(Castellano))"), "pt": ("PortugueseBrazilian", "PortugueseBrazilian (Português Brasileiro)"),
         "ru": ("Russian", "Russian (Русский)"), "uk": ("Ukrainian", "Ukrainian (Українська)"),
         "cs": ("Czech", "Czech (Čeština)"), "zh": ("ChineseSimplified", "ChineseSimplified (简体中文)"),
         "ja": ("Japanese", "Japanese (日本語)"), "ko": ("Korean", "Korean (한국어)")}
PH = re.compile(r"\{[^}]*\}|\[[A-Za-z_]+\]|<color=[^>]+>|</color>")
N = 120
FIELDS = {"label", "description", "labelShort", "pawnLabel", "gerundLabel", "verb", "jobString", "reportString", "letterLabel",
          "letterText", "baseInspectLine", "labelNoun", "labelNounPretty", "pawnsPlural", "beginLetter", "beginLetterLabel",
          "successfulInspectLine", "fixedName", "labelMale", "labelFemale", "inspectString", "skillLabel", "labelPlural", "slogan"}


class Ctx:
    def __init__(self, mod, prefix, langs):
        self.mod, self.prefix, self.langs = Path(mod), prefix, langs
        self.work = self.mod / "Source" / "i18n"
        self.src, self.src_prev, self.delta, self.out = (self.work / n for n in ("src", "src_prev", "delta", "delta_out"))
        self.ledger = self.work / "known.json"
        self.lang_root = self.mod / "Common" / "Languages"

    def lang_dir(self, code):
        short, long = LANGS[code]
        if self.lang_root.is_dir():
            for d in sorted(self.lang_root.iterdir()):
                if d.name == short or d.name.startswith(short + " ("): return d
        return self.lang_root / long


def detect_prefix(mod):
    for d, ds, fs in os.walk(os.path.join(mod, "Source")):
        ds[:] = [x for x in ds if x not in ("obj", "obj17", "bin", "_frozen16")]
        for f in fs:
            if f.endswith(".cs"):
                m = re.search(r'KeyPrefix\s*=\s*"([A-Za-z0-9_]+)"', open(os.path.join(d, f), encoding="utf-8", errors="replace").read())
                if m: return m.group(1)
    return None


def module_of(ctx, path, kind):
    """kind 'keyed': file stem without <XX>_ ; 'def': sub folder under Defs, else file stem."""
    stem = path.stem
    if stem.startswith(ctx.prefix + "_"): stem = stem[len(ctx.prefix) + 1:]
    if kind == "def":
        parts = path.relative_to(next(p for p in path.parents if p.name == "Defs")).parts
        if len(parts) > 1: return parts[0]
    return stem or "Core"


def extract(ctx):
    data = {}
    slot = lambda m: data.setdefault(m, {"keyed": {}, "defs": {}})
    kd = ctx.lang_root / "English" / "Keyed"
    for f in sorted(kd.glob("*.xml")) if kd.is_dir() else []:
        for el in ET.parse(f).getroot():
            if el.text and el.text.strip(): slot(module_of(ctx, f, "keyed"))["keyed"][el.tag] = el.text
    for base in (ctx.mod / "Common" / "Defs", ctx.mod / "1.7" / "Defs"):
        for f in sorted(base.rglob("*.xml")) if base.is_dir() else []:
            try:
                root = ET.parse(f).getroot()
            except ET.ParseError as e:
                print("XML error", f, e); continue
            mod = module_of(ctx, f, "def")
            for d in root:
                dn = d.findtext("defName")
                if not dn or d.get("Abstract") == "True": continue
                for child in d:
                    if child.tag in FIELDS and child.text and child.text.strip():
                        slot(mod)["defs"][f"{d.tag}/{dn}.{child.tag}"] = child.text.strip()
                    if child.tag == "stages":
                        for i, st in enumerate(child):
                            for sub in st:
                                if sub.tag in ("label", "description") and sub.text and sub.text.strip():
                                    slot(mod)["defs"][f"{d.tag}/{dn}.stages.{i}.{sub.tag}"] = sub.text.strip()
    if ctx.src.exists():
        if ctx.src_prev.exists():
            for p in ctx.src_prev.glob("*.json"): p.unlink()
        ctx.src_prev.mkdir(parents=True, exist_ok=True)
        for p in ctx.src.glob("*.json"): (ctx.src_prev / p.name).write_bytes(p.read_bytes()); p.unlink()
    ctx.src.mkdir(parents=True, exist_ok=True)
    total = 0
    for mod, d in sorted(data.items()):
        (ctx.src / f"{mod}.json").write_text(json.dumps(d, ensure_ascii=False, indent=1), encoding="utf-8")
        n = len(d["keyed"]) + len(d["defs"]); total += n
        print(f"{mod:14} keyed {len(d['keyed']):4}  defs {len(d['defs']):4}")
    print("total strings", total)


def load_src(d):
    out = {}  # flat key -> (module, english)
    if not Path(d).is_dir(): return out
    for p in sorted(Path(d).glob("*.json")):
        j = json.loads(p.read_text(encoding="utf-8"))
        for k, v in j.get("keyed", {}).items(): out["k:" + k] = (p.stem, v)
        for k, v in j.get("defs", {}).items(): out["d:" + k] = (p.stem, v)
    return out


def lang_entries(ctx, code):
    base = ctx.lang_dir(code)
    files = (list((base / "Keyed").glob("*.xml")) + list((base / "DefInjected").rglob("*.xml"))) if base.is_dir() else []
    have = {}
    for f in files:
        try:
            root = ET.parse(f).getroot()
        except Exception as e:
            print("bad xml", f, e); continue
        if f.parent.name == "Keyed":
            for e in root: have["k:" + e.tag] = f
        else:
            t = f.parent.relative_to(base / "DefInjected").as_posix()
            for e in root: have[f"d:{t}/{e.tag}"] = f
    return have, files, base


def plan(ctx, old_dir):
    new, old = load_src(ctx.src), load_src(old_dir)
    if not new: print("no src: run `extract` first"); return 1
    if ctx.delta.exists():
        for p in ctx.delta.rglob("*.json"): p.unlink()
    total = {}
    for code in ctx.langs:
        have, _, _ = lang_entries(ctx, code)
        todo = {k: v for k, (m, v) in new.items() if v and v.strip() and (k not in have or (bool(old) and (k not in old or old[k][1] != v)))}
        items = sorted(todo.items())
        d = ctx.delta / code; d.mkdir(parents=True, exist_ok=True)
        for i in range(0, len(items), N):
            (d / f"{i // N:03d}.json").write_text(json.dumps(dict(items[i:i + N]), ensure_ascii=False, indent=1), encoding="utf-8")
        total[code] = len(items)
    print(json.dumps(total))
    return 0


def write_xml(path, pairs):
    path.parent.mkdir(parents=True, exist_ok=True)
    body = "\n".join(f"  <{k}>{escape(v)}</{k}>" for k, v in pairs)
    path.write_text('<?xml version="1.0" encoding="utf-8"?>\n<LanguageData>\n' + body + ("\n" if body else "") + "</LanguageData>\n", encoding="utf-8")


def apply(ctx, dry=False):
    new = load_src(ctx.src)
    if not new: print("no src: run `extract` first"); return 1
    ledger = None
    if ctx.ledger.exists():
        ledger = set(json.loads(ctx.ledger.read_text(encoding="utf-8")).get("keys", []))
    else:
        print("note: no known.json yet: nothing will be deleted this run")
    deletable = (ledger - set(new)) if ledger is not None else set()
    for code in ctx.langs:
        tr = {}
        for p in sorted((ctx.out / code).glob("*.json")) if (ctx.out / code).is_dir() else []:
            try:
                tr.update(json.loads(p.read_text(encoding="utf-8")))
            except Exception as e:
                print("bad json", p, e)
        review = []
        _, files, base = lang_entries(ctx, code)
        content = {}
        for f in files:
            try:
                content[f] = [(e.tag, e.text or "") for e in ET.parse(f).getroot()]
            except Exception:
                content[f] = None          # unparsable: never rewrite or delete such a file
        index = {}
        for f, pairs in content.items():
            if pairs is None: continue
            pre = "k:" if f.parent.name == "Keyed" else "d:" + f.parent.relative_to(base / "DefInjected").as_posix() + "/"
            for i, (k, _) in enumerate(pairs): index[pre + k] = (f, i)
        removed = 0
        for fk, (f, i) in list(index.items()):
            if fk in deletable:
                content[f][i] = None; removed += 1
        added = changed = 0
        for fk, t in tr.items():
            if fk not in new: continue
            mod, en = new[fk]
            if not t or sorted(PH.findall(t)) != sorted(PH.findall(en)):
                review.append(fk); t = en
            if fk in index and content[index[fk][0]][index[fk][1]] is not None:
                f, i = index[fk]; content[f][i] = (content[f][i][0], t); changed += 1
            else:
                if fk.startswith("k:"):
                    f, tag = base / "Keyed" / f"{ctx.prefix}_{mod}.xml", fk[2:]
                else:
                    dtype, tag = fk[2:].split("/", 1)
                    f = base / "DefInjected" / dtype / f"{ctx.prefix}_{mod}.xml"
                if content.get(f, []) is None: continue
                content.setdefault(f, []).append((tag, t)); added += 1
        if not dry:
            for f, pairs in content.items():
                if pairs is None: continue
                pairs = [p for p in pairs if p]
                if pairs: write_xml(f, pairs)
                elif f.exists(): f.unlink()
            if review:
                ctx.delta.mkdir(parents=True, exist_ok=True)
                (ctx.delta / f"review_{code}.txt").write_text("\n".join(review) + "\n", encoding="utf-8")
        print(f"{code}: {changed} replaced, {added} added, {removed} removed (known keys only), {len(review)} kept English (placeholder mismatch or empty)" + (" [dry]" if dry else ""))
    if not dry:
        ctx.work.mkdir(parents=True, exist_ok=True)
        ctx.ledger.write_text(json.dumps({"keys": sorted(new)}, ensure_ascii=False, indent=0), encoding="utf-8")
    return 0


def main(argv):
    args = [a for a in argv[1:] if not a.startswith("--")]
    opt = lambda n: argv[argv.index(n) + 1] if n in argv else None
    for v in (opt("--prefix"), opt("--langs")):
        if v in args: args.remove(v)
    if len(args) < 2 or args[1] not in ("extract", "plan", "apply"):
        print(__doc__); return 2
    mod = os.path.abspath(args[0])
    prefix = opt("--prefix") or detect_prefix(mod)
    if not prefix:
        print('ERROR no key prefix: pass --prefix XX (or KeyPrefix = "XX" in the mod C#)'); return 1
    if opt("--langs"): langs = opt("--langs").split(",")
    else:
        root = Path(mod, "Common", "Languages")
        langs = ["de"] + [c for c, (s, _) in LANGS.items() if c != "de" and root.is_dir() and any(d.name == s or d.name.startswith(s + " (") for d in root.iterdir())]
    bad = [c for c in langs if c not in LANGS]
    if bad: print("ERROR unknown language code(s)", bad, "known:", " ".join(LANGS)); return 1
    ctx = Ctx(mod, prefix, langs)
    if args[1] == "extract": extract(ctx); return 0
    if args[1] == "plan": return plan(ctx, args[2] if len(args) > 2 else ctx.src_prev)
    return apply(ctx, "--dry" in argv)


if __name__ == "__main__":
    sys.exit(main(sys.argv))
