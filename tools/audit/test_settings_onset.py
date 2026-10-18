"""局部诊断严格保留一帧反例和证据边界。"""
import importlib.util
import unittest
from pathlib import Path
import numpy as np

spec = importlib.util.spec_from_file_location('onset', Path(__file__).with_name('analyze-settings-onset.py'))
onset = importlib.util.module_from_spec(spec)
spec.loader.exec_module(onset)


class OnsetTests(unittest.TestCase):
    def test_变帧率原始解码不补帧且保留微秒时基(self):
        command = onset.decoder_command(Path('movie.mkv'), [1, 2, 40, 40])
        self.assertEqual(command[command.index('-fps_mode')+1], 'passthrough')
        self.assertEqual(command[command.index('-enc_time_base')+1], '1:1000000')
        self.assertEqual(command[command.index('-vf')+1], 'crop=40:40:1:2')

    def test_single_frame_difference_is_retained(self):
        frames = np.zeros((4, 2, 2, 3), dtype=np.uint8)
        frames[1, 0, 0, 0] = 3
        result = onset.transitions(frames, [0, .02, .04, .06])
        self.assertEqual(result['firstChangedFrame'], 1)
        self.assertEqual(result['changedPixels'], [0, 1, 0, 0])

    def test_static_is_not_fabricated_as_open(self):
        result = onset.transitions(np.zeros((2, 2, 2, 3), dtype=np.uint8), [0, .02])
        self.assertIsNone(result['firstChangedSeconds'])

    def test_invalid_timestamps_are_rejected(self):
        with self.assertRaises(ValueError):
            onset.transitions(np.zeros((2, 2, 2, 3), dtype=np.uint8), [.02, .01])
