"""独立JBR度量对照；不把AWT测试图当作PyCharm实测画面。"""
import json
import os
from pathlib import Path
import subprocess
import unittest


@unittest.skipUnless(os.environ.get("AUGIT_REFERENCE_JAVA") and os.environ.get("AUGIT_REFERENCE_FONT"),
                     "需显式指定参考JBR与字体")
class FractionalMetricsTests(unittest.TestCase):
    def run_metrics(self, *mode):
        return subprocess.run([os.environ["AUGIT_REFERENCE_JAVA"],
            str(Path(__file__).with_name("PixelFontMetrics.java")),
            os.environ["AUGIT_REFERENCE_FONT"], *mode],
            capture_output=True, timeout=30)

    def test_开关改变字宽但不改变既有行盒(self):
        records = []
        for mode in ((), ("on",), ("off",)):
            process = self.run_metrics(*mode)
            self.assertEqual(process.returncode, 0, process.stderr)
            rows = [json.loads(line) for line in process.stdout.decode("utf-8").splitlines()]
            self.assertEqual([row["size"] for row in rows], list(range(9, 41)))
            records.append(rows)
        self.assertEqual(records[0], records[1])
        for enabled, disabled in zip(records[1], records[2]):
            self.assertEqual(enabled["lineHeight"], disabled["lineHeight"])
            self.assertEqual(enabled["baseline"], disabled["baseline"])
        self.assertAlmostEqual(records[1][4]["spaceWidth"], 7.8, places=3)
        self.assertAlmostEqual(records[2][4]["spaceWidth"], 8.0, places=3)

    def test_错误模式不静默回落(self):
        self.assertNotEqual(self.run_metrics("invalid").returncode, 0)
        for scale in ("0", "-1", "NaN", "Infinity"):
            self.assertNotEqual(self.run_metrics("off", scale).returncode, 0)


if __name__ == "__main__":
    unittest.main()
