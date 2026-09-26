"""Regression for a mirrored adult torso: the added deformation must mirror too."""

import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from auto_model import _chest_region, deform


def check():
    pairs = [
        {"base_center": [-0.18, 1.2, 0.00], "center": [-0.23, 1.2, 0.16], "radius": 0.22},
        {"base_center": [0.18, 1.2, 0.00], "center": [0.23, 1.2, 0.16], "radius": 0.22},
    ]
    regions = [_chest_region(pair, {}, 0.0, 2.0) for pair in pairs]
    for strength in (-500, 100, 1000):
        points = np.array([
            [side * x, y, z]
            for side in (-1.0, 1.0)
            for x in (0.14, 0.22, 0.30)
            for y in (1.10, 1.20, 1.30)
            for z in (0.15, 0.22, 0.29)
        ])
        edited, _ = deform(points, strength, "chest", (0.0, regions, 2.0, 1.0))
        left = edited[:len(edited) // 2]
        right = edited[len(edited) // 2:]
        error = np.max(np.abs(left * [-1, 1, 1] - right))
        assert error < 1e-9, f"left/right deformation differs at {strength}%: {error}"
    print("PASS mirrored torso deformation")


if __name__ == "__main__":
    check()
