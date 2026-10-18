"""针对当前已观察前景窗口验证原始屏幕采集；不启动窗口或注入输入。"""
import argparse
import hashlib
import json
import subprocess
import tempfile
import unittest
from pathlib import Path

from PIL import Image


class ScreenRegionTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix="Augit-screen-test-")
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.output = self.root / "screen.png"

    def capture(self, process_id=None, region=None, extra=()):
        command = ["powershell", "-NoProfile", "-File", str(Path(__file__).with_name("capture-screen-region.ps1")),
                   "-WindowHandle", str(options.window), "-ExpectedProcessId", str(process_id or options.process),
                   "-RegionFrom", str(region or options.region), "-Output", str(self.output)]
        return subprocess.run(command+list(extra), capture_output=True, timeout=30)

    def test_原始PNG尺寸哈希及重复物理区域(self):
        result = self.capture()
        self.assertEqual(result.returncode, 0, result.stderr.decode(errors="replace"))
        record = json.loads(Path(str(self.output)+".json").read_text(encoding="utf-8"))
        prior = json.loads(Path(options.region).read_text(encoding="utf-8"))
        with Image.open(self.output) as image:
            self.assertEqual(image.format, "PNG")
            self.assertEqual(list(image.size), prior["regionPhysical"][2:])
        self.assertEqual(record["regionPhysical"], prior["regionPhysical"])
        self.assertEqual(record["sha256"], hashlib.sha256(self.output.read_bytes()).hexdigest())
        self.assertTrue(record["stableProcessFrameAndDpi"])
        self.assertIsNone(record["unoccluded"])
        self.assertFalse(record["resampled"])
        self.assertEqual(record['activationState'],'active')
        self.assertTrue(record['stableActivation'])
        self.assertEqual(record['foregroundWindow'],options.window)
        self.assertEqual(record['foregroundProcessId'],options.process)

    def test_非活动必须指定独立前景且不能冒用活动窗口(self):
        for extra in [('-ExpectedActivation','Inactive'),
                      ('-ExpectedActivation','Inactive','-ExpectedForegroundWindow',str(options.window),
                       '-ExpectedForegroundProcessId',str(options.process))]:
            with self.subTest(extra=extra):
                self.assertNotEqual(self.capture(extra=extra).returncode,0)
                self.assertFalse(self.output.exists())

    def test_错误实例不生成证据(self):
        self.assertNotEqual(self.capture(process_id=2147483647).returncode, 0)
        self.assertEqual(list(self.root.iterdir()), [])

    def test_已有文件不得覆盖(self):
        self.output.write_bytes(b"preserved")
        self.assertNotEqual(self.capture().returncode, 0)
        self.assertEqual(self.output.read_bytes(), b"preserved")
        self.assertFalse(Path(str(self.output)+".json").exists())

    def test_错误区域来源缩放及坐标拒绝(self):
        prior = json.loads(Path(options.region).read_text(encoding="utf-8"))
        for patch in ({"method": "PrintWindow"}, {"dpi": 1}, {"regionPhysical": [0.5, 0, 10, 10]},
                      {"regionPhysical": [-999999, 0, 10, 10]}):
            with self.subTest(patch=patch):
                source = self.root / "invalid.json"
                source.write_text(json.dumps({**prior, **patch}), encoding="utf-8")
                self.assertNotEqual(self.capture(region=source).returncode, 0)
                self.assertFalse(self.output.exists())
                self.assertFalse(Path(str(self.output)+".json").exists())


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--window", required=True, type=int)
    parser.add_argument("--process", required=True, type=int)
    parser.add_argument("--region", required=True)
    options = parser.parse_args()
    unittest.main(argv=[__file__])
