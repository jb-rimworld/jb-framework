#!/usr/bin/env python3
"""Renders the framework's own UI icons into Common/Textures/UI/JBFramework/ (128 px, transparent, flat house style).

    uv run --with resvg-py --with pillow python3 -I tools/art/build.py

Style and primitives come from Hearth & Harbor (HearthHarbor/tools/art/hhart, imported read-only). The icons are optional: every
page falls back to a vanilla icon or to no icon, never to a pink square. SPDX-License-Identifier: MIT
"""
import math, sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(ROOT.parent / "HearthHarbor" / "tools" / "art"))
from hhart.style import Canvas, P, dark, light, OUT  # noqa: E402
from hhart.registry import render_png  # noqa: E402

OUTDIR = ROOT / "Common" / "Textures" / "UI" / "JBFramework"
ICONS = {}


def icon(name):
    def deco(fn):
        ICONS[name] = fn
        return fn
    return deco


def bar(c, x, y, w, h, col):
    c.rect(x, y, w, h, col, r=h / 2, sw=3.6, hl=False)


@icon("Mark")
def mark(c):
    """three stacked foundation plates, the top one amber"""
    c.rect(14, 82, 100, 26, P["iron"], r=8, sw=4)
    c.rect(22, 56, 84, 26, P["steel"], r=8, sw=4)
    c.rect(30, 24, 68, 32, P["amber"], r=8, sw=4)
    c.circle(64, 40, 7, P["cream"], sw=3, hl=False, sh=False)


@icon("Mods")
def mods(c):
    """a fanned stack of three cards"""
    for i, col in enumerate([P["iron"], P["steel"], P["paper"]]):
        c.rect(18 + i * 12, 20 + i * 14, 70, 74, col, r=8, sw=4)
    bar(c, 52, 62, 34, 9, P["amber"])
    bar(c, 52, 78, 24, 9, P["iron"])


@icon("Overview")
def overview(c):
    """four tiles, two switched on"""
    for i in range(4):
        x, y = 16 + (i % 2) * 52, 16 + (i // 2) * 52
        on = i in (0, 3)
        c.rect(x, y, 44, 44, P["amber"] if on else P["steel"], r=8, sw=4)
        c.circle(x + 22, y + 22, 8, P["cream"] if on else dark(P["steel"], .3), sw=3, hl=False, sh=False)


@icon("General")
def general(c):
    """three sliders"""
    for i, k in enumerate([0.7, 0.35, 0.55]):
        y = 30 + i * 34
        bar(c, 14, y - 5, 100, 10, P["steel"])
        c.circle(14 + 100 * k, y, 12, P["amber"] if i == 0 else P["paper"], sw=4)


@icon("Compat")
def compat(c):
    """two linked rings"""
    def ring(x, col):
        c.raw('<circle cx="%d" cy="64" r="26" fill="none" stroke="%s" stroke-width="19"/>' % (x, OUT))
        c.raw('<circle cx="%d" cy="64" r="26" fill="none" stroke="%s" stroke-width="12"/>' % (x, col))
    ring(46, P["steel"])
    ring(82, P["amber"])


@icon("Diagnosis")
def diagnosis(c):
    """magnifier over a list"""
    c.rect(14, 14, 72, 92, P["paper"], r=6, sw=4)
    for i in range(4):
        bar(c, 24, 28 + i * 18, 44, 8, P["steel"])
    c.line(84, 84, 112, 112, OUT, w=16)
    c.line(84, 84, 110, 110, P["iron"], w=10)
    c.circle(76, 76, 22, P["glass"], sw=5)


@icon("About")
def about(c):
    """a round info badge (dot and bar, no letters)"""
    c.circle(64, 64, 52, P["navy"], sw=4)
    c.circle(64, 38, 9, P["cream"], sw=3, hl=False, sh=False)
    c.rect(55, 54, 18, 44, P["cream"], r=6, sw=3, hl=False)


def main():
    OUTDIR.mkdir(parents=True, exist_ok=True)
    for name, fn in ICONS.items():
        c = Canvas(1, 1, False)
        fn(c)
        path = OUTDIR / (name + ".png")
        render_png(c.svg(), 128, 128).save(path, optimize=True)
        print("wrote", path.relative_to(ROOT))


if __name__ == "__main__":
    main()
