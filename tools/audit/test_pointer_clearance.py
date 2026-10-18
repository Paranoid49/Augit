"""验证原始屏幕采集的指针保护区，不移动系统指针。"""
import base64
import subprocess
import unittest
from pathlib import Path


class PointerClearanceTests(unittest.TestCase):
    def test_区域内部边缘与外部距离(self):
        source = Path(__file__).with_name('capture-screen-region.ps1').read_text(encoding='utf-8-sig')
        function = source[source.index('function Test-PointerClearance'):source.index('$targetWindow = [IntPtr]$WindowHandle')]
        cases = [(100, 200, 96, False), (99, 200, 96, False), (4, 200, 96, False),
                 (3, 200, 96, True), (496, 200, 96, True), (495, 200, 96, False),
                 (100, 103, 96, True), (100, 104, 96, False), (100, 496, 96, True),
                 (100, 200, -1, True), (400, 200, 0, True), (399, 200, 0, False)]
        checks = []
        for x, y, margin, expected in cases:
            flag = '$true' if expected else '$false'
            checks.append(f'if ((Test-PointerClearance @(100,200,300,200) {x} {y} {margin}) -ne {flag}) {{ throw "位置{x},{y}保护距{margin}错误" }}')
        script = function + chr(10) + chr(10).join(checks)
        encoded = base64.b64encode(script.encode('utf-16-le')).decode('ascii')
        result = subprocess.run(['powershell', '-NoProfile', '-EncodedCommand', encoded], capture_output=True, timeout=30)
        self.assertEqual(result.returncode, 0, result.stderr.decode(errors='replace'))


if __name__ == '__main__':
    unittest.main()
