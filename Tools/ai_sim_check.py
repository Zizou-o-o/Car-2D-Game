# Mirrors TopDown2DCarController + AIDriver in Python to check the new racing-line
# logic before it ever reaches Unity. Not a physics replacement: it answers
# "does the car follow the line, stay on the road, and keep a sane speed?".
import math

# --- car (values from the scene instance of the player car) ---
MAX_SPEED = 15.0
ACCEL = 6.0
TURN_FACTER = 5.0
DRIFT = 0.9
SPEED_LOSS_RATE = 12.0
DT = 0.02

# --- ai ---
MAP_RES = 16
SEEK = 1.0
FORWARD = 0.35
LOOKAHEAD_PER_SPEED = 0.5
MIN_LOOKAHEAD = 2.0
MAX_LOOKAHEAD = 9.0
ROAD_SAMPLES = 5
ROAD_LOOK_MIN = 1.5
ROAD_LOOK_MAX = 6.0
ROAD_TOLERANCE = 0.15
CORNER_SCALE = 0.8
MAX_LAT_ACCEL = 22.0
PREDICT_TIME = 0.35
OFFROAD_PREDICT_FACTOR = 0.6
DEADBAND = 0.25
STEER_FULL_ANGLE = 50.0
DIRECTION_SMOOTHING = 10.0
ROAD_HALF_WIDTH = 2.0

CURRENT_PATH = None


def on_road(p):
    """The road is a corridor of half width 2 around the racing line."""
    return CURRENT_PATH.distance_to(p) <= ROAD_HALF_WIDTH


class Path:
    def __init__(self, points, closed=True):
        self.points = points
        self.closed = closed
        n = len(points)
        self.seg_count = n if closed else n - 1
        self.cum = [0.0] * (self.seg_count + 1)
        total = 0.0
        for i in range(self.seg_count):
            self.cum[i] = total
            total += math.dist(self.point_at(i), self.point_at(i + 1))
        self.cum[self.seg_count] = total
        self.total = total

    def point_at(self, i):
        if not self.closed:
            return self.points[max(0, min(i, len(self.points) - 1))]
        return self.points[i % len(self.points)]

    def wrap(self, arc):
        if self.total <= 1e-5:
            return 0.0
        return arc % self.total if self.closed else max(0.0, min(arc, self.total))

    def locate(self, arc):
        low, high, seg = 0, self.seg_count - 1, 0
        while low <= high:
            mid = (low + high) // 2
            if self.cum[mid] <= arc:
                seg = mid
                low = mid + 1
            else:
                high = mid - 1
        length = self.cum[seg + 1] - self.cum[seg]
        t = max(0.0, min(1.0, (arc - self.cum[seg]) / length)) if length > 1e-6 else 0.0
        return seg, t

    def sqr_dist(self, world, segment):
        a, b = self.point_at(segment), self.point_at(segment + 1)
        ab = (b[0] - a[0], b[1] - a[1])
        l2 = ab[0] ** 2 + ab[1] ** 2
        t = 0.0 if l2 <= 1e-8 else max(0.0, min(1.0, ((world[0] - a[0]) * ab[0] + (world[1] - a[1]) * ab[1]) / l2))
        px, py = a[0] + ab[0] * t, a[1] + ab[1] * t
        return (world[0] - px) ** 2 + (world[1] - py) ** 2

    def distance_to(self, world):
        return math.sqrt(min(self.sqr_dist(world, i) for i in range(self.seg_count)))

    def closest(self, world, hint=-1):
        best, best_sqr = 0, float("inf")
        if 0 <= hint < self.seg_count:
            for i in range(max(0, hint - 3), min(self.seg_count - 1, hint + 3) + 1):
                d = self.sqr_dist(world, i)
                if d < best_sqr:
                    best_sqr, best = d, i
            if best_sqr > 64.0:
                best_sqr = float("inf")
                for i in range(self.seg_count):
                    d = self.sqr_dist(world, i)
                    if d < best_sqr:
                        best_sqr, best = d, i
        else:
            for i in range(self.seg_count):
                d = self.sqr_dist(world, i)
                if d < best_sqr:
                    best_sqr, best = d, i
        return best

    def project(self, world, segment):
        a, b = self.point_at(segment), self.point_at(segment + 1)
        ab = (b[0] - a[0], b[1] - a[1])
        l2 = ab[0] ** 2 + ab[1] ** 2
        t = 0.0 if l2 <= 1e-8 else max(0.0, min(1.0, ((world[0] - a[0]) * ab[0] + (world[1] - a[1]) * ab[1]) / l2))
        return self.cum[segment] + t * math.sqrt(l2)

    def tangent(self, arc):
        seg, _ = self.locate(self.wrap(arc))
        a, b = self.point_at(seg), self.point_at(seg + 1)
        d = math.hypot(b[0] - a[0], b[1] - a[1])
        return ((b[0] - a[0]) / d, (b[1] - a[1]) / d)

    def curvature(self, arc):
        span = max(0.4, min(2.0, self.total * 0.03))
        t0, t1 = self.tangent(arc), self.tangent(arc + span)
        dot = max(-1.0, min(1.0, t0[0] * t1[0] + t0[1] * t1[1]))
        return math.acos(dot) / span

    def sample(self, world, ahead, hint=-1):
        nearest = self.closest(world, hint)
        arc = self.wrap(self.project(world, nearest) + ahead)
        seg, t = self.locate(arc)
        a, b = self.point_at(seg), self.point_at(seg + 1)
        pos = (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)
        return {"position": pos, "nearest": nearest, "arc": arc, "curvature": self.curvature(arc)}


def circle_path(radius=8.0, count=100):
    return Path([(radius * math.cos(2 * math.pi * i / count),
                  radius * math.sin(2 * math.pi * i / count)) for i in range(count)])


def oval_path():
    # straight, then a tight 90 degree corner of radius 3, straight, corner...
    pts = [(-6.0 + 12.0 * i / 19.0, -3.0) for i in range(20)]
    for i in range(1, 10):
        a = -math.pi / 2 + math.pi * i / 10.0
        pts.append((6.0 + 3.0 * math.cos(a), 3.0 * math.sin(a)))
    pts += [(6.0 - 12.0 * i / 19.0, 3.0) for i in range(20)]
    for i in range(1, 10):
        a = math.pi / 2 + math.pi * i / 10.0
        pts.append((-6.0 + 3.0 * math.cos(a), 3.0 * math.sin(a)))
    return Path(pts)


class Car:
    def __init__(self, p, heading):
        self.p = p
        self.angle = heading * 180.0 / math.pi
        self.v = (0.0, 0.0)
        self.damping = 0.0
        self.speed_limit = 0.0

    @property
    def up(self):
        a = math.radians(self.angle)
        return (-math.sin(a), math.cos(a))

    @property
    def right(self):
        a = math.radians(self.angle)
        return (math.cos(a), math.sin(a))

    @property
    def speed(self):
        return math.hypot(*self.v)

    def fixed_update(self, turn_input, throttle, surface_multiplier):
        up, right = self.up, self.right
        cmax = MAX_SPEED * surface_multiplier
        if self.speed_limit > 0:
            cmax = min(cmax, self.speed_limit)

        forward_speed = up[0] * self.v[0] + up[1] * self.v[1]

        skip_force = False
        if forward_speed > cmax and throttle == 0:
            skip_force = True
        if forward_speed < -cmax * 0.5 and throttle == 0:
            skip_force = True
        if self.speed ** 2 > cmax ** 2 and throttle > 0:
            skip_force = True

        if not skip_force:
            self.damping = 0.0 if throttle != 0 else self.damping + (3.0 - self.damping) * min(1.0, DT * 3.0)
            self.v = (self.v[0] + up[0] * throttle * ACCEL * DT,
                      self.v[1] + up[1] * throttle * ACCEL * DT)

        if self.damping > 0:
            factor = 1.0 / (1.0 + self.damping * DT)
            self.v = (self.v[0] * factor, self.v[1] * factor)

        fv = up[0] * self.v[0] + up[1] * self.v[1]
        rv = right[0] * self.v[0] + right[1] * self.v[1]
        self.v = (up[0] * fv + right[0] * rv * DRIFT, up[1] * fv + right[1] * rv * DRIFT)

        k = max(0.0, min(1.0, self.speed / 8.0))
        self.angle -= turn_input * TURN_FACTER * k

        if surface_multiplier < 0.999 or self.speed_limit > 0:
            if self.speed > cmax:
                new_speed = self.speed - min(self.speed - cmax, SPEED_LOSS_RATE * DT)
                scale = new_speed / self.speed
                self.v = (self.v[0] * scale, self.v[1] * scale)

        self.p = (self.p[0] + self.v[0] * DT, self.p[1] + self.v[1] * DT)


def signed_angle(a, b):
    """Unity's Vector2.SignedAngle: positive when b is counter-clockwise from a."""
    dot = max(-1.0, min(1.0, a[0] * b[0] + a[1] * b[1]))
    det = a[0] * b[1] - a[1] * b[0]
    return math.degrees(math.atan2(det, dot))


def road_danger(origin, direction, look_ahead):
    on = 0
    for s in range(1, ROAD_SAMPLES + 1):
        p = (origin[0] + direction[0] * look_ahead * s / ROAD_SAMPLES,
             origin[1] + direction[1] * look_ahead * s / ROAD_SAMPLES)
        if on_road(p):
            on += 1
    off = 1.0 - on / ROAD_SAMPLES
    return max(0.0, min(1.0, (off - ROAD_TOLERANCE) / max(0.01, 1.0 - ROAD_TOLERANCE)))


def ai_step(car, path, hint, chosen, cross_gain=0.0, lookahead_scale=1.0):
    speed = car.speed
    ahead = max(MIN_LOOKAHEAD, min(MAX_LOOKAHEAD, MIN_LOOKAHEAD + speed * LOOKAHEAD_PER_SPEED)) * lookahead_scale
    sample = path.sample(car.p, ahead, hint)
    hint = sample["nearest"]

    aim_x, aim_y = sample["position"]

    if cross_gain > 0.0:
        # cross-track error at the closest point on the line
        seg = sample["nearest"]
        arc = path.project(car.p, seg)
        nearest_seg, t = path.locate(path.wrap(arc))
        a, b = path.point_at(nearest_seg), path.point_at(nearest_seg + 1)
        nearest = (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)
        tx, ty = path.tangent(arc)
        nx, ny = -ty, tx                       # left normal
        error = (car.p[0] - nearest[0]) * nx + (car.p[1] - nearest[1]) * ny
        aim_x -= nx * error * cross_gain
        aim_y -= ny * error * cross_gain

    dx = aim_x - car.p[0]
    dy = aim_y - car.p[1]
    length = math.hypot(dx, dy)
    aim = (dx / length, dy / length) if length > 0.01 else car.up

    road_ahead = ROAD_LOOK_MIN + (ROAD_LOOK_MAX - ROAD_LOOK_MIN) * min(1.0, speed / MAX_SPEED)

    scores = [0.0] * MAP_RES
    for i in range(MAP_RES):
        a = 2 * math.pi * i / MAP_RES
        d = (math.cos(a), math.sin(a))
        seek = max(0.0, d[0] * aim[0] + d[1] * aim[1]) * SEEK
        forward_i = max(0.0, d[0] * car.up[0] + d[1] * car.up[1]) * FORWARD
        interest = max(seek, forward_i)
        scores[i] = interest - road_danger(car.p, d, road_ahead)

    best = max(range(MAP_RES), key=lambda i: scores[i])
    desired_a = 2 * math.pi * best / MAP_RES
    desired = (math.cos(desired_a), math.sin(desired_a))

    if chosen is None:
        chosen = desired
    else:
        t = 1.0 - math.exp(-DIRECTION_SMOOTHING * DT)
        sx = chosen[0] + (desired[0] - chosen[0]) * t
        sy = chosen[1] + (desired[1] - chosen[1]) * t
        n = math.hypot(sx, sy)
        chosen = (sx / n, sy / n) if n > 0.0001 else desired

    angle = signed_angle(car.up, chosen)
    steer = max(-1.0, min(1.0, angle / STEER_FULL_ANGLE))
    if abs(angle) < 2.0:
        steer = 0.0

    radius = 1.0 / max(0.0001, sample["curvature"])
    yaw_rate = TURN_FACTER * math.pi / 180.0 / DT
    target = min(MAX_SPEED, radius * yaw_rate * CORNER_SCALE, math.sqrt(MAX_LAT_ACCEL * radius))

    if on_road(car.p):
        predicted = (car.p[0] + car.v[0] * PREDICT_TIME, car.p[1] + car.v[1] * PREDICT_TIME)
        if not on_road(predicted):
            target *= OFFROAD_PREDICT_FACTOR

    target = max(1.0, min(MAX_SPEED, target))

    if speed > target + 0.75:
        throttle = 0.0
    elif speed < target - DEADBAND:
        throttle = 1.0
    else:
        throttle = 0.5

    car.speed_limit = target if target < MAX_SPEED - 0.1 else 0.0
    return -steer, throttle, hint, chosen


def run(name, path, seconds=40.0, cross_gain=0.0, lookahead_scale=1.0, start_arc=0.0, start_speed=0.0):
    global CURRENT_PATH
    CURRENT_PATH = path

    tangent = path.tangent(start_arc)
    seg, t = path.locate(path.wrap(start_arc))
    a, b = path.point_at(seg), path.point_at(seg + 1)
    start = (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)
    car = Car(start, math.atan2(tangent[1], tangent[0]))
    car.v = (tangent[0] * start_speed, tangent[1] * start_speed)

    hint, chosen = -1, None
    worst_lateral = 0.0
    worst_time = 0.0
    worst_after_start = 0.0
    speeds = []
    zero_direction = 0
    stuck_run = 0
    longest_slow = 0.0
    progress = 0.0
    previous_arc = None
    samples = []

    for step in range(int(seconds / DT)):
        turn, throttle, hint, chosen = ai_step(car, path, hint, chosen, cross_gain, lookahead_scale)
        if abs(chosen[0]) + abs(chosen[1]) < 0.0001:
            zero_direction += 1

        car.fixed_update(turn, throttle, 1.0)

        lateral = path.distance_to(car.p)
        if lateral > worst_lateral:
            worst_lateral = lateral
            worst_time = step * DT
        if step * DT > 5.0:
            worst_after_start = max(worst_after_start, lateral)
        speeds.append(car.speed)

        if step % 50 == 0:
            samples.append((step * DT, lateral, car.speed))

        arc = path.sample(car.p, 0.0, hint)["arc"]
        if previous_arc is not None:
            delta = path.wrap(arc - previous_arc)
            if delta > path.total / 2:
                delta -= path.total
            progress += delta
        previous_arc = arc

        if car.speed < 0.6:
            stuck_run += 1
            longest_slow = max(longest_slow, stuck_run * DT)
        else:
            stuck_run = 0

    print(f"{name}")
    print(f"  distance from line    worst {worst_lateral:5.2f} at {worst_time:4.1f}s"
          f"   after 5s: {worst_after_start:4.2f}  (road half width {ROAD_HALF_WIDTH})")
    print(f"  average speed         {sum(speeds) / len(speeds):5.2f}   max {max(speeds):5.2f}")
    print(f"  laps in {seconds:>4.0f}s          {progress / path.total:5.2f}")
    print(f"  longest stop          {longest_slow:5.2f} s")
    print(f"  zero directions       {zero_direction}")
    print("  first seconds (t, lateral, speed): " +
          ", ".join(f"{t:.1f}/{l:.2f}/{s:.1f}" for t, l, s in samples[:8]))
    print()


run("circle, radius 8 (easy)", circle_path(), seconds=20.0)
run("oval, radius-3 corners (tight)", oval_path(), seconds=20.0)
run("oval, entering the hairpin at 13 u/s (braking test)", oval_path(), seconds=12.0,
    start_arc=7.0, start_speed=13.0)
print("--- cross-track gain (circle, 20 s) ---")
for gain in (0.5, 1.0):
    run(f"gain {gain}", circle_path(), seconds=20.0, cross_gain=gain)
