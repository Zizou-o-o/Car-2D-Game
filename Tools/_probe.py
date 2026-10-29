import importlib.util, random, math
spec = importlib.util.spec_from_file_location("m", r"Tools/ai_sim_check.py")
m = importlib.util.module_from_spec(spec)
import sys
sys.argv = ["x"]
# avoid running the tests: import executes top-level run() calls...
