"""验证重复投影检测和输入拒绝；合成数据不作为实机结果。"""
import copy
import importlib.util
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("shadow_edge", Path(__file__).with_name("verify-shadow-edge.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def profile():
    return {"scale": 1.75, "profiles": {side: {"samples": [
        {"distance_physical": i, "median_darkening_rgb": 40-i} for i in range(1, 17)]}
        for side in ("right", "bottom")}}


class ShadowEdgeTests(unittest.TestCase):
    def test_同剖面通过但不宣称全量验收(self):
        result = module.verify(profile(), profile())
        self.assertTrue(result["nearEdgePassed"])
        self.assertFalse(result["fullShadowVerified"])

    def test_额外投影失败且容差边界正确(self):
        actual = profile()
        actual["profiles"]["right"]["samples"][0]["median_darkening_rgb"] += 4
        self.assertFalse(module.verify(actual, profile())["nearEdgePassed"])
        actual["profiles"]["right"]["samples"][0]["median_darkening_rgb"] -= 1
        self.assertTrue(module.verify(actual, profile())["nearEdgePassed"])

    def test_拒绝错缩放缺样本和不连续距离(self):
        for mutation in ("scale", "missing", "distance"):
            actual = copy.deepcopy(profile())
            if mutation == "scale":
                actual["scale"] = 1
            elif mutation == "missing":
                actual["profiles"]["right"]["samples"].pop()
            else:
                actual["profiles"]["right"]["samples"][0]["distance_physical"] = 2
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                module.verify(actual, profile())


if __name__ == "__main__":
    unittest.main()
