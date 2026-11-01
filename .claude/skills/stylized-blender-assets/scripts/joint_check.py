"""
Z-fighting check ACROSS modules: places assets per a layout, merges everything into one mesh and runs
the same detector as preview.py. Use it for modular kits (walls/floors/roofs that meet at joints).

    python joint_check.py <kit>.py --layout layout.json [--set NAME=VALUE]
layout = [[asset_name, x, y, z, rot_deg_about_Z], ...]  (same format as preview.py)
"""
import argparse, ast, json, os, sys, types
from math import radians, cos, sin
import numpy as np
here = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, here)
import mathutils_shim as mathutils; sys.modules["mathutils"] = mathutils
from unittest.mock import MagicMock
sys.modules["bpy"] = MagicMock()
_src = open(os.path.join(here, "preview.py")).read()            # reuse preview's bmesh icosphere shim
exec(_src[_src.index("# --- real icosphere"):_src.index('sys.modules["bmesh"] = bm_mod') + len('sys.modules["bmesh"] = bm_mod')])
import zfight

ap = argparse.ArgumentParser()
ap.add_argument("script"); ap.add_argument("--layout", required=True)
ap.add_argument("--set", action="append", default=[])
a = ap.parse_args()
g = {"__name__": "__kit_preview__", "__KIT_PREVIEW__": True, "__ATLANTIS_PREVIEW__": True}
for kv in a.set:
    k, v = kv.split("=", 1)
    try: g[k] = ast.literal_eval(v)
    except Exception: g[k] = v
exec(compile(open(a.script).read(), a.script, "exec"), g)
assets = {x.name: x for x in g["PREVIEW_REGISTRY"]}
lay = open(a.layout).read() if os.path.exists(a.layout) else a.layout
V, F, P, off, poff = [], [], [], 0, 0
for name, x, y, z, rot in json.loads(lay):
    A = assets[name]; c, s = cos(radians(rot)), sin(radians(rot))
    for mb in (A.S, A.D, A.P):
        if not mb.v: continue
        for vx, vy, vz in mb.v:
            V.append((c * vx - s * vy + x, s * vx + c * vy + y, vz + z))
        F += [[i + off for i in f] for f in mb.f]
        P += [p + poff for p in mb.pid]
        off += len(mb.v); poff += mb.npieces
problems = zfight.report("LAYOUT (all modules merged)", V, F, P)
print("ALL CLEAN" if not problems else "FIX THE JOINTS ABOVE")
