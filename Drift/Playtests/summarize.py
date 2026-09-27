"""Summarize Drift playtest result files (written by Drift/Playtest/* in the Editor).

usage: python Playtests/summarize.py [file.json ...]   (default: the newest file per scenario)
"""
import glob
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))


def newest_per_scenario():
    best = {}
    for f in sorted(glob.glob(os.path.join(HERE, "*.json"))):
        scen = os.path.basename(f).rsplit("_", 2)[0]
        best[scen] = f
    return list(best.values())


def show(path):
    d = json.load(open(path, encoding="utf-8"))
    print(f"== {d['scenario']}  ({os.path.basename(path)})  mode {d.get('mode')}  bot {d.get('bot')}  "
          f"seed {d['seed']}  {d['screenW']}x{d['screenH']}  pipeline {d.get('pipeline')}  real {d['realSeconds']:.0f}s")
    for p in d["phases"]:
        dur = p["t1"] - p["t0"]
        print(f"  [{p['name']:<12}] {dur:6.0f}s  noticed {p['noticed']:3d}/{p['allEvents']:<4d} "
              f"gap med {p['medianGap']:5.1f} max {p['maxGap']:5.1f}  10s-cover {p['windowsCovered']*100:3.0f}%  "
              f"zoom {p['zoom']:.2f} px/u {p['pxPerUnitAtPlayer']:.0f} animal {p['animalPx']:.0f}px  "
              f"main {p['avgMainMs']:.1f}ms gc {p['gcKbPerFrame']:.2f}KB draws {p["batches"]:.0f} setpass {p['setPass']:.0f} tris {p['trisK']:.0f}k  "
              f"flow {p['opticFlow']:.2f} jitter {p['cameraJitter']:.1f} merges {p['merges']} land {p['landArea0']:.0f}->{p['landArea1']:.0f}")
        if p["noticedByKind"]:
            print(f"      kinds: {p['noticedByKind']}")
    for l in d.get("levels", []):
        dur = l["t1"] - l["t0"]
        print(f"  L{l['level']}: {dur:5.0f}s  track {l['avgTrackSpeed']:5.1f} u/s  flow {l['opticFlow']:.2f}  threats {l['threats']:3d} "
              f"pickups {l['pickups']:3d}  dec/10s {l['decisionsPer10s']:.1f}  near {l['nearMisses']}  hits {l['hits']}  "
              f"dodges {l['dodges']}  flotsam {l['flotsam']}  boost {l['boostShare']*100:.0f}% surf {l['surfShare']*100:.0f}% stagger {l['staggerShare']*100:.0f}%")
    if d.get("memory"):
        m0, m1 = d["memory"][0], d["memory"][-1]
        print(f"  memory: total {m0['totalMb']:.0f} -> {m1['totalMb']:.0f} MB, mono {m0['monoMb']:.0f} -> {m1['monoMb']:.0f} MB, "
              f"gfx {m0['gfxMb']:.0f} -> {m1['gfxMb']:.0f} MB, islands {m0['islands']} -> {m1['islands']}, save {m1['saveKb']:.0f} KB "
              f"({len(d['memory'])} samples)")
        objs = [m["gameObjects"] for m in d["memory"] if m["gameObjects"] >= 0]
        if objs:
            print(f"  transforms: {objs[0]} -> {objs[-1]} (max {max(objs)})")
    for n in d.get("notes", [])[:60]:
        print("  note:", n)
    for e in d.get("errors", [])[:20]:
        print("  ERROR:", e)


if __name__ == "__main__":
    files = sys.argv[1:] or newest_per_scenario()
    for f in files:
        show(f)
        print()
