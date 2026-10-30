"""Distribution of sideways speed / slip angle, to pick sensible drift thresholds."""
import math

src = open("Tools/ai_sim_check.py").read()
exec(src.split('\nrun("circle')[0])


def stats(values):
    values = sorted(values)
    if not values:
        return "no samples"
    return "  ".join(f"p{int(p * 100):02d} {values[min(len(values) - 1, int(p * len(values)))]:5.1f}"
                     for p in (0.5, 0.75, 0.9, 0.95))


def collect(car, seconds, path=None):
    laterals, slips, speeds = [], [], []
    hint, chosen = -1, None

    for step in range(int(seconds / DT)):
        if path is not None:
            turn, throttle, hint, chosen = ai_step(car, path, hint, chosen)
        else:
            turn, throttle = 1.0, 1.0

        car.fixed_update(turn, throttle, 1.0)

        if car.speed < 3.5:
            continue

        up, right = car.up, car.right
        forward = up[0] * car.v[0] + up[1] * car.v[1]
        lateral = right[0] * car.v[0] + right[1] * car.v[1]
        laterals.append(abs(lateral))
        slips.append(math.degrees(math.atan2(abs(lateral), max(0.01, abs(forward)))))
        speeds.append(car.speed)

    return laterals, slips, speeds


runs = {}
for label, path in (("oval", oval_path()), ("circle", circle_path())):
    CURRENT_PATH = path
    tangent = path.tangent(0.0)
    car = Car(path.point_at(0), math.atan2(tangent[1], tangent[0]))
    car.v = (tangent[0] * 6.0, tangent[1] * 6.0)
    runs[label] = collect(car, 10.0, path)

car = Car((0.0, 0.0), 0.0)
car.v = (0.0, 12.0)
runs["player full lock"] = collect(car, 6.0)

for label, (laterals, slips, speeds) in runs.items():
    print(label)
    print("  lateral u/s ", stats(laterals))
    print("  slip deg    ", stats(slips))
    print("  speed       ", stats(speeds))

print()
for lateral_threshold, slip_threshold in ((3.0, 20.0), (3.2, 24.0), (3.5, 25.0),
                                          (4.0, 30.0), (4.5, 35.0), (5.0, 40.0)):
    parts = []
    for label, (laterals, slips, speeds) in runs.items():
        hits = sum(1 for l, s in zip(laterals, slips)
                   if l >= lateral_threshold or s >= slip_threshold)
        parts.append(f"{label} {100.0 * hits / max(1, len(laterals)):4.1f}%")
    print(f"lateral {lateral_threshold:4.1f} / slip {slip_threshold:4.1f} deg -> " + "   ".join(parts))
