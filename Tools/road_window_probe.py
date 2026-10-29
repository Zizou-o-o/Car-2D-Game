import random

source = open("Tools/ai_sim_check.py").read()
head = source.split('run("circle')[0]
ns = {}
exec(head, ns)

path = ns["circle_path"]()
ns["CURRENT_PATH"] = path

worst = 0.0

for _ in range(5000):
    q = (random.uniform(-12.0, 12.0), random.uniform(-12.0, 12.0))
    truth = path.distance_to(q)
    ns["ROAD_HINT"] = path.closest(q)
    windowed = ns["road_distance"](q)
    worst = max(worst, abs(truth - windowed))

print("worst difference windowed vs global:", round(worst, 6))

# how many segments does a 6-unit look-ahead need on this path?
print("path segments:", path.seg_count, "segment length:", round(path.total / path.seg_count, 3))
