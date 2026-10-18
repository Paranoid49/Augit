"""用替身 Win32 返回值验证真实采集器前置门禁；不操作桌面。"""
import base64
import subprocess
import unittest
from pathlib import Path


class ScreenRegionDiagnosticsTests(unittest.TestCase):
    def test_所有者坐标读取校验实例与直接关系(self):
        source = Path(__file__).with_name('capture-screen-region.ps1').read_text(encoding='utf-8-sig')
        function = source[source.index('function Read-OwnerGeometry'):source.index('$targetWindow = [IntPtr]$WindowHandle')]
        prefix = '''
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
public static class AuditScreenRead {
 public struct Point { public int X,Y; }
 public struct Rect { public int Left,Top,Right,Bottom; }
 public static uint GetWindowThreadProcessId(IntPtr h,out uint p) {p=200;return 1;}
 public static IntPtr GetWindow(IntPtr h,uint c) {return (IntPtr)50;}
 public static bool GetWindowRect(IntPtr h,out Rect r) {r=new Rect{Left=-12,Top=-12,Right=2892,Bottom=1740};return true;}
 public static bool GetClientRect(IntPtr h,out Rect r) {r=new Rect{Right=2880,Bottom=1716};return true;}
 public static bool ClientToScreen(IntPtr h,ref Point p) {p.X=0;p.Y=0;return true;}
 public static uint GetDpiForWindow(IntPtr h) {return 168;}
}
'@
$targetWindow = [IntPtr]100
'''
        for owner, process, succeeds in [(50, 200, True), (51, 200, False), (50, 201, False)]:
            script = chr(10).join([prefix, function, f'$OwnerWindowHandle={owner}; $ExpectedProcessId={process}', 'Read-OwnerGeometry | ConvertTo-Json -Compress'])
            encoded = base64.b64encode(script.encode('utf-16-le')).decode('ascii')
            result = subprocess.run(['pwsh', '-NoProfile', '-EncodedCommand', encoded], capture_output=True, timeout=30)
            self.assertEqual(result.returncode == 0, succeeds, result.stderr)
            if succeeds:
                import json
                record = json.loads(result.stdout)
                self.assertEqual(record['clientOriginPhysical'], [0, 0])
                self.assertEqual(record['windowRectPhysical'][:2], [-12, -12])

    def probe(self, foreground, expected_process=200):
        source = Path(__file__).with_name('capture-screen-region.ps1').read_text(encoding='utf-8-sig')
        # 执行原脚本身份校验段，截在任何截图或文件写入之前。
        start = source.index('$targetWindow = [IntPtr]$WindowHandle')
        end = source.index('    $windowRect = New-Object AuditScreenRead+Rect')
        prefix = """
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
public static class AuditScreenRead {
    public static IntPtr Foreground;
    public static IntPtr GetForegroundWindow() { return Foreground; }
    public static uint GetWindowThreadProcessId(IntPtr window, out uint processId) {
        processId = window.ToInt64() == 100 ? 200u : 400u; return 1;
    }
    public static IntPtr SetThreadDpiAwarenessContext(IntPtr value) { return IntPtr.Zero; }
}
'@
$WindowHandle = 100
$ExpectedActivation = 'Active'
"""
        setup = f'[AuditScreenRead]::Foreground = [IntPtr]{foreground}; $ExpectedProcessId = {expected_process}'
        script = chr(10).join([prefix, setup, source[start:end], 'Write-Output "IDENTITY_GATE_PASSED"', '} finally {}'])
        encoded = base64.b64encode(script.encode('utf-16-le')).decode('ascii')
        return subprocess.run(['powershell', '-NoProfile', '-NonInteractive', '-EncodedCommand', encoded],
                              capture_output=True, timeout=30)

    def test_失焦拒绝且报告双方身份(self):
        result = self.probe(300)
        self.assertNotEqual(result.returncode, 0)
        output = result.stdout + result.stderr
        for field in (b'targetWindow=100', b'targetProcessId=200',
                      b'foregroundWindow=300', b'foregroundProcessId=400'):
            self.assertIn(field, output)
        self.assertNotIn(b'IDENTITY_GATE_PASSED', output)

    def test_错误实例仍在前景判断之前拒绝(self):
        result = self.probe(300, 999)
        self.assertNotEqual(result.returncode, 0)
        self.assertNotIn(b'foregroundWindow=', result.stdout + result.stderr)
        self.assertNotIn(b'IDENTITY_GATE_PASSED', result.stdout + result.stderr)

    def test_前景属于目标时通过身份门禁(self):
        result = self.probe(100)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn(b'IDENTITY_GATE_PASSED', result.stdout)


if __name__ == '__main__':
    unittest.main()
