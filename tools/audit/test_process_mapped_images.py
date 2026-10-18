"""设备路径必须完整匹配，不能把相似卷名解析为错误产物。"""
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('mapped_images', Path(__file__).with_name('read-process-mapped-images.py'))
mapped = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mapped)


class DevicePathTests(unittest.TestCase):
    def test_case_and_longest_device_prefix(self):
        separator = chr(92)
        volume = separator.join(['', 'Device', 'HarddiskVolume1'])
        self.assertEqual(mapped.resolve_device_path(volume + separator + 'a.dll',
                         {volume: 'D:'}), 'D:' + separator + 'a.dll')
        self.assertEqual(mapped.resolve_device_path((volume + '0').lower() + separator + 'a.dll',
                         {volume: 'C:', volume + '0': 'D:'}), 'D:' + separator + 'a.dll')

    def test_unknown_and_prefix_collision_fail(self):
        volume = chr(92).join(['', 'Device', 'HarddiskVolume1'])
        for value in [volume + '0' + chr(92) + 'a.dll', 'a.dll']:
            with self.assertRaises(ValueError):
                mapped.resolve_device_path(value, {volume: 'C:'})

    def test_invalid_process_fails_without_fabricated_rows(self):
        with self.assertRaises(OSError):
            mapped.inspect(0)


if __name__ == '__main__':
    unittest.main()
