"""双端门禁须拒绝自比、JPEG、缺条件和篡改；临时样本自动清理。"""
import copy
import hashlib
import importlib.util
from pathlib import Path
import tempfile
import unittest

from PIL import Image

spec = importlib.util.spec_from_file_location("pair", Path(__file__).with_name("verify-reference-pair.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ReferencePairTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="Augit-pair-test-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        Image.new("RGB", (8, 8), "white").save(self.root / "sample.png")
        side = {key: "相同条件" for key in module.CONDITIONS}
        side.update(file="sample.png", sha256=hashlib.sha256((self.root / "sample.png").read_bytes()).hexdigest(),
                    imageSize=[8, 8], capture="screen", unoccluded=True, resampled=False,
                    contentEvidence="测试样本，非产品验收", application="PyCharm", version="2026.2.1")
        self.pair = {"reference": side, "actual": copy.deepcopy(side)}
        self.pair["actual"]["application"] = "Augit"
        Image.new("RGB", (8, 8), "black").save(self.root / "actual.png")
        self.pair["actual"].update(file="actual.png",
            sha256=hashlib.sha256((self.root / "actual.png").read_bytes()).hexdigest())

    def test_应用名称不能掩盖同文件或复制图(self):
        for same_file in (True, False):
            pair = copy.deepcopy(self.pair)
            if same_file:
                pair["actual"]["file"] = "sample.png"
            else:
                (self.root / "actual.png").write_bytes((self.root / "sample.png").read_bytes())
            pair["actual"]["sha256"] = pair["reference"]["sha256"]
            self.assertFalse(module.verify_pair(pair, self.root)["comparable"])

    def test_同态只允许量测不宣称复原(self):
        result = module.verify_pair(self.pair, self.root)
        self.assertTrue(result["comparable"])
        self.assertFalse(result["pixelRestorationVerified"])

    def test_逐项条件缺失和不同均拒绝(self):
        for key in module.CONDITIONS:
            for missing in (True, False):
                with self.subTest(key=key, missing=missing):
                    pair = copy.deepcopy(self.pair)
                    if missing:
                        del pair["actual"][key]
                    else:
                        pair["actual"][key] = "不同条件"
                    self.assertFalse(module.verify_pair(pair, self.root)["comparable"])

    def test_自比遮挡重采样和错误哈希拒绝(self):
        for key, value in (("application", "Augit"), ("version", "2026.2.3"),
                           ("unoccluded", False), ("resampled", True),
                           ("capture", "CDP"), ("contentEvidence", ""), ("sha256", "0")):
            pair = copy.deepcopy(self.pair)
            pair["reference"][key] = value
            self.assertFalse(module.verify_pair(pair, self.root)["comparable"])

    def test_JPEG改名PNG仍拒绝(self):
        Image.new("RGB", (8, 8), "white").save(self.root / "sample.png", format="JPEG")
        digest = hashlib.sha256((self.root / "sample.png").read_bytes()).hexdigest()
        self.pair["reference"]["sha256"] = digest
        result = module.verify_pair(self.pair, self.root)
        self.assertFalse(result["comparable"])
        self.assertIn("参考不是原始无损PNG", result["errors"])


if __name__ == "__main__":
    unittest.main()
