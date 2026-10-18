import java.awt.Font;
import java.awt.RenderingHints;
import java.awt.geom.AffineTransform;
import java.awt.image.BufferedImage;
import java.io.File;
import java.util.Locale;

// 对照EditorView采用的粗斜体W步长；独立JBR度量不代替PyCharm实机截图。
class TerminalGridMetrics {
  public static void main(String[] args) throws Exception {
    Font base = Font.createFont(Font.TRUETYPE_FONT, new File(args[0]));
    var graphics = new BufferedImage(100, 100, BufferedImage.TYPE_INT_ARGB).createGraphics();
    try {
      graphics.setRenderingHint(RenderingHints.KEY_TEXT_ANTIALIASING, RenderingHints.VALUE_TEXT_ANTIALIAS_LCD_HRGB);
      graphics.setRenderingHint(RenderingHints.KEY_FRACTIONALMETRICS, RenderingHints.VALUE_FRACTIONALMETRICS_OFF);
      for (double scale : new double[] {1, 1.25, 1.5, 1.75, 2, 2.25, 2.5, 3}) {
        graphics.setTransform(AffineTransform.getScaleInstance(scale, scale));
        for (int size = 9; size <= 40; size++) {
          double width = base.deriveFont((float)size).getStringBounds("W", graphics.getFontRenderContext()).getWidth();
          double expected = Math.round(Math.round(size * scale) * .6) / scale;
          if (Math.abs(width - expected) > .00001) throw new AssertionError("参考最大字宽与公式不符");
          System.out.printf(Locale.ROOT, "%.2f,%d,%.6f%n", scale, size, width);
        }
      }
    } finally { graphics.dispose(); }
  }
}
