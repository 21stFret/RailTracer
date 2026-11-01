"""Python twin of the Unity TrainCarBuilder placement rules (Blender coords: car along +Y, centred on 0)."""
import json, sys
WALL = {"S": ("Wall_1m_Solid", 1), "P": ("Wall_1m_Pilaster", 1), "w": ("Wall_1m_WindowSmall", 1),
        "W": ("Wall_2m_Window", 2), "D": ("Wall_2m_Door", 2)}
ROOF = {"S": "Roof_1m", "P": "Roof_1m_Rib", "w": "Roof_1m", "W": "Roof_2m_Lamp", "D": "Roof_2m_Lamp"}
def car(side_a, side_b=None, start="EndCap_Door", end="EndCap_Door", runner=True, x_off=0.0):
    side_b = side_b or side_a
    la = sum(WALL[t][1] for t in side_a if t in WALL); lb = sum(WALL[t][1] for t in side_b if t in WALL)
    assert la == lb, (la, lb)
    total = la + 1.0
    y0 = -total / 2
    L = [[start, x_off, y0 + 0.25, 0, 180], [end, x_off, y0 + total - 0.25, 0, 0]]
    for side, rot, roofs in ((side_a, 0, True), (side_b, 180, False)):
        s = y0 + 0.5
        for t in side:
            if t == "|":
                if roofs: L.append(["Partition_Door", x_off, s, 0, 0])
                continue
            name, ln = WALL[t]
            L.append([name, x_off, s + ln / 2, 0, rot])
            if roofs: L.append([ROOF[t], x_off, s + ln / 2, 0, 0])
            s += ln
    for k in range(int(la)):
        L.append(["Floor_1m" if runner else "Floor_1m_Plain", x_off, y0 + 0.5 + k + 0.5, 0, 0])
    return L
if __name__ == "__main__":
    print(json.dumps(car(*sys.argv[1:])))
