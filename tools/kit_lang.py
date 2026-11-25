#!/usr/bin/env python3
"""Per-mod Keyed language files of a mod built on JB Framework: write <XX>_Settings.xml from the settings dump, check missing keys.

    python3 -I kit_lang.py <ModDir> [--prefix XX] [--dump settings_dump.json] [--write | --check]

Without an option it CHECKS (read only, as --check): nothing in the mod is written unless --write is given (jbmod `lang --write`,
or `lang --refresh` = make a fresh dump in the game, then write).

Since the framework became its own mod (24.11.2026) its texts live ONCE in JBFramework/Common/Languages/<Lang>/Keyed/JBFramework.xml
(keys JBF.*); nothing is stamped into the mods any more. A mod only translates its own registration labels (<XX>.Setting.*,
<XX>.Section.*, <XX>.Preset.*, <XX>.Page.*, <XX>.Compat.*, <XX>.Enum.*, <XX>.SettingCat.*).

--write:
  * --dump (or <ModDir>/Source/settings_dump.json when it exists): the registry dump of the in-game settings-dump suite:
        {"prefix": "XX", "keyed": {"XX.Setting.lo.regrowDays": "Days to regrow a limb", ...}}
    -> <ModDir>/Common/Languages/English/Keyed/<XX>_Settings.xml (regenerated; English only, other languages go through i18n_delta.py).
  Existing hand-written files are never touched; only the generated file is rewritten. An old <XX>_Kit.xml (stamped kit texts) is
  reported as obsolete; delete it by hand.

--check (read only, exit 1 on errors):
  ERROR  English lacks a key of the dump; a Keyed XML does not parse; a key is defined twice in one language;
         a value is empty; C# code under <ModDir>/Source translates a literal "XX.…" key that English does not define.
  WARN   German lacks keys that English has; placeholders ({0}, {name}, [token]) differ between English and German;
         an obsolete <XX>_Kit.xml is still there.
The prefix comes from --prefix, else from `KeyPrefix = "XX"` in the mod's C# (KitInfo). English fallback text lives in code, so a
missing translation shows English, never a raw key.
"""
import json, os, re, sys
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape

HERE = os.path.dirname(os.path.abspath(__file__))
LANG_DIRS = {"English": "English", "German": "German (Deutsch)", "French": "French (Français)", "Spanish": "Spanish (Español(Castellano))",
             "PortugueseBrazilian": "PortugueseBrazilian (Português Brasileiro)", "Russian": "Russian (Русский)",
             "Ukrainian": "Ukrainian (Українська)", "Czech": "Czech (Čeština)",
             "ChineseSimplified": "ChineseSimplified (简体中文)", "Japanese": "Japanese (日本語)", "Korean": "Korean (한국어)"}
PH = re.compile(r"\{[^}]*\}|\[[A-Za-z_]+\]|<color=[^>]+>|</color>")


def lang_folder(mod, name):
    """Existing Common/Languages folder for a language key ('German' matches 'German (Deutsch)'), else the canonical name."""
    base = os.path.join(mod, "Common", "Languages")
    if os.path.isdir(base):
        for d in sorted(os.listdir(base)):
            if d == name or d.startswith(name + " ("):
                return os.path.join(base, d)
    return os.path.join(base, LANG_DIRS.get(name, name))


def detect_prefix(mod):
    for d, _, fs in os.walk(os.path.join(mod, "Source")):
        d_ = d.replace(os.sep, "/")
        if "/obj" in d_ or "/bin" in d_ or "_frozen16" in d_: continue
        for f in fs:
            if f.endswith(".cs"):
                m = re.search(r'KeyPrefix\s*=\s*"([A-Za-z0-9_]+)"', open(os.path.join(d, f), encoding="utf-8", errors="replace").read())
                if m: return m.group(1)
    return None


def read_keyed(path):
    """-> (list of (key, value)), raises ET.ParseError"""
    return [(e.tag, (e.text or "")) for e in ET.parse(path).getroot()]


def write_keyed(path, pairs, header=None):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    body = "\n".join(f"  <{k}>{escape(v)}</{k}>" for k, v in pairs)
    head = '<?xml version="1.0" encoding="utf-8"?>\n'
    if header: head += f"<!-- {header} -->\n"
    open(path, "w", encoding="utf-8").write(head + "<LanguageData>\n" + body + ("\n" if body else "") + "</LanguageData>\n")


def keyed_of(mod, lang):
    """All Keyed pairs of one language folder -> (dict key->(value,file), problems)"""
    out, problems = {}, []
    d = os.path.join(lang_folder(mod, lang), "Keyed")
    if not os.path.isdir(d): return out, problems
    for f in sorted(os.listdir(d)):
        if not f.endswith(".xml"): continue
        p = os.path.join(d, f)
        try:
            pairs = read_keyed(p)
        except ET.ParseError as e:
            problems.append(f"{lang}/{f}: XML does not parse: {e}"); continue
        for k, v in pairs:
            if k in out: problems.append(f"{lang}: key {k} defined twice ({out[k][1]} and {f})")
            if not v.strip(): problems.append(f"{lang}/{f}: key {k} is empty")
            out[k] = (v, f)
    return out, problems


def code_keys(mod, prefix):
    """Literal "<P>.xxx" keys that C# translates: .Translate() / .TranslateSimple() / Translate("..") / T("..") (heuristic)."""
    found = {}
    pat = re.compile(r'"(%s\.[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)*)"' % re.escape(prefix))
    for d, ds, fs in os.walk(os.path.join(mod, "Source")):
        ds[:] = [x for x in ds if x not in ("obj", "obj17", "bin", "_frozen16")]
        for f in fs:
            if not f.endswith(".cs"): continue
            txt = open(os.path.join(d, f), encoding="utf-8", errors="replace").read()
            for m in pat.finditer(txt):
                after, before = txt[m.end():m.end() + 30], txt[max(0, m.start() - 30):m.start()]
                if re.match(r"\s*\.\s*Translate", after) or re.search(r"\b(Translate|TranslateSimple|Tr|T|Keyed\.T)\s*\(\s*$", before):
                    found.setdefault(m.group(1), os.path.relpath(os.path.join(d, f), mod))
    return found


def main(argv):
    args = [a for a in argv[1:] if not a.startswith("--")]
    opt = lambda n: argv[argv.index(n) + 1] if n in argv else None
    if not args: print(__doc__); return 2
    mod = os.path.abspath(args[0])
    for v in (opt("--prefix"), opt("--dump")):
        if v in args: args.remove(v)
    dump_path = opt("--dump") or os.path.join(mod, "Source", "settings_dump.json")
    dump = None
    if os.path.isfile(dump_path):
        dump = json.load(open(dump_path, encoding="utf-8"))
    elif opt("--dump"):
        print("ERROR dump not found:", dump_path); return 1
    # the prefix: --prefix, else KeyPrefix in the code, else the dump's (the game knows it even when it is the ShortId default)
    prefix = opt("--prefix") or detect_prefix(mod) or (dump or {}).get("prefix")
    if not prefix:
        print('ERROR no key prefix: pass --prefix XX, put KeyPrefix = "XX" into the KitInfo of the mod, or run `jbmod <Mod> lang --refresh`'); return 1
    if dump and dump.get("prefix") not in (None, prefix):
        print(f"ERROR dump prefix {dump.get('prefix')} != {prefix}"); return 1
    obsolete = [os.path.join(lang_folder(mod, l), "Keyed", f"{prefix}_Kit.xml") for l in ("English", "German")]
    obsolete = [o for o in obsolete if os.path.isfile(o)]

    if "--write" in argv:
        for o in obsolete: print("note: obsolete stamped kit texts (now in JB Framework itself), delete:", os.path.relpath(o, mod))
        if dump:
            out = os.path.join(lang_folder(mod, "English"), "Keyed", f"{prefix}_Settings.xml")
            write_keyed(out, list(dump.get("keyed", {}).items()), "Generated by JB Framework tools/kit_lang.py from the settings dump suite.")
            print("wrote", os.path.relpath(out, mod), f"({len(dump.get('keyed', {}))} keys)")
        else:
            print("note: no settings dump; <Prefix>_Settings.xml not written (run `jbmod <Mod> test kit.settingsdump`)")
        return 0

    errors, warns = [], []
    en, probs = keyed_of(mod, "English"); errors += probs
    de, probs = keyed_of(mod, "German"); errors += probs
    need = {}
    for o in obsolete: warns.append("obsolete stamped kit texts (JB Framework ships them once now): " + os.path.relpath(o, mod))
    if dump:
        for k in dump.get("keyed", {}): need[k] = "settings dump"
    for k, src in need.items():
        if k not in en: errors.append(f"English lacks {k} ({src})")
    for k, f in code_keys(mod, prefix).items():
        if k not in en: errors.append(f"English lacks {k} (translated in {f})")
    if de or os.path.isdir(lang_folder(mod, "German")):
        miss = [k for k in en if k not in de]
        if miss: warns.append(f"German lacks {len(miss)} of {len(en)} English keys, e.g. {', '.join(miss[:5])}")
        for k in en:
            if k in de and sorted(PH.findall(en[k][0])) != sorted(PH.findall(de[k][0])):
                warns.append(f"placeholders differ in German for {k}")
    else:
        warns.append("no German language folder")
    for w in warns: print("WARN ", w)
    for e in errors: print("ERROR", e)
    print(f"kit_lang check: prefix {prefix}, English {len(en)} keys, German {len(de)} keys, {len(errors)} errors, {len(warns)} warnings")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
