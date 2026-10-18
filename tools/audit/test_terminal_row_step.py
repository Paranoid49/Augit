"""行格分析使用相同字形；所有临时图片由测试上下文清理。"""
import importlib.util
from pathlib import Path
import tempfile
import unittest

from PIL import Image, ImageDraw

spec = importlib.util.spec_from_file_location('row_step', Path(__file__).with_name('measure-terminal-row-step.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class TerminalRowStepTests(unittest.TestCase):
    def test_light_ink_on_dark_background_preserves_source(self):
        with tempfile.TemporaryDirectory(prefix='Augit-row-step-') as directory:
            file = Path(directory) / 'dark.png'
            image = Image.new('RGB', (20, 140), (25, 26, 28))
            draw = ImageDraw.Draw(image)
            for y in [3, 34, 66, 97]:
                draw.rectangle((2, y, 8, y + 5), fill=(209, 211, 217))
            image.save(file)
            original = file.read_bytes()
            result = module.measure_repeated_rows(file, 0, 15, 4, 31.5, threshold=160, ink='light')
            self.assertEqual(result['tops'], [3, 34, 66, 97])
            self.assertEqual(result['steps'], [31, 32, 31])
            self.assertEqual(result['ink'], 'light')
            self.assertEqual(file.read_bytes(), original)
            with self.assertRaises(ValueError):
                module.measure_repeated_rows(file, 0, 15, 4, 31.5, ink='auto')

    def test_repeated_fractional_rows_and_cumulative_drift(self):
        with tempfile.TemporaryDirectory(prefix='Augit-row-step-') as directory:
            file = Path(directory) / 'repeated.png'
            image = Image.new('RGB', (20, 140), 'white')
            draw = ImageDraw.Draw(image)
            for y in [3, 34, 66, 97]:
                draw.rectangle((2, y, 8, y + 5), fill='black')
            image.save(file)
            result = module.measure_repeated_rows(file, 0, 15, 4, 31.5)
            self.assertEqual(result['steps'], [31, 32, 31])
            self.assertEqual(result['maxAbsoluteDrift'], .5)
            self.assertFalse(result['pixelRestorationVerified'])
            wrong = module.measure_repeated_rows(file, 0, 15, 4, 31)
            self.assertEqual(wrong['maxAbsoluteDrift'], 1)
            with self.assertRaises(ValueError):
                module.measure_repeated_rows(file, 0, 15, 3, 31.5)
            with self.assertRaises(ValueError):
                module.measure_repeated_rows(file, 0, 21, 4, 31.5)
            region = module.measure_repeated_rows(file, 0, 15, 3, 31.5, top=20, bottom=120)
            self.assertEqual(region['tops'], [34, 66, 97])
            self.assertEqual(region['region'], [0, 20, 15, 120])
            self.assertEqual(region['sha256'], result['sha256'])
            for top, bottom in [(-1, 120), (20, 141), (30, 30)]:
                with self.assertRaises(ValueError):
                    module.measure_repeated_rows(file, 0, 15, 3, 31.5, top=top, bottom=bottom)

    def test_same_character_not_whole_line_top(self):
        with tempfile.TemporaryDirectory(prefix='Augit-row-step-') as directory:
            file = Path(directory) / 'rows.png'
            image = Image.new('RGB', (60, 60), 'white')
            draw = ImageDraw.Draw(image)
            draw.rectangle((11, 8, 15, 15), fill='black')
            draw.rectangle((1, 31, 5, 38), fill='black')
            draw.rectangle((1, 2, 5, 15), fill='black')
            draw.rectangle((11, 34, 15, 38), fill='black')
            image.save(file)
            result = module.measure_row_step(file, ['Wa', 'az'], 0, 10, [(0, 20), (25, 45)])
            self.assertEqual(result['medianStep'], 23)
            self.assertFalse(result['pixelRestorationVerified'])

    def test_reject_blank_and_out_of_bounds(self):
        with tempfile.TemporaryDirectory(prefix='Augit-row-step-') as directory:
            file = Path(directory) / 'blank.png'
            Image.new('RGB', (20, 40), 'white').save(file)
            for origin in [0, 30]:
                with self.assertRaises(ValueError):
                    module.measure_row_step(file, ['a', 'a'], origin, 10, [(0, 20), (20, 40)])

    def test_reject_unmatched_text(self):
        with tempfile.TemporaryDirectory(prefix='Augit-row-step-') as directory:
            file = Path(directory) / 'blank.png'
            Image.new('RGB', (20, 40), 'white').save(file)
            with self.assertRaises(ValueError):
                module.measure_row_step(file, ['a', 'b'], 0, 10, [(0, 20), (20, 40)])


if __name__ == '__main__':
    unittest.main()
