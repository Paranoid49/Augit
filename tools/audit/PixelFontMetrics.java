import java.awt.Font;
import java.awt.FontMetrics;
import java.awt.Graphics2D;
import java.awt.RenderingHints;
import java.awt.geom.AffineTransform;
import java.awt.image.BufferedImage;
import java.io.File;

// 使用参考JBR及独立开源字体测量AWT度量；不冒充实际PyCharm截图。
class PixelFontMetrics {
  public static void main(String[] args) throws Exception {
    if (args.length < 1 || args.length > 3 ||
        (args.length >= 2 && !args[1].equals("on") && !args[1].equals("off"))) {
      throw new IllegalArgumentException("参数：字体路径 [on|off] [DPI比例]");
    }
    Font base = Font.createFont(Font.TRUETYPE_FONT, new File(args[0]));
    BufferedImage image = new BufferedImage(400, 160, BufferedImage.TYPE_INT_ARGB);
    Graphics2D graphics = image.createGraphics();
    try {
      graphics.setRenderingHint(RenderingHints.KEY_TEXT_ANTIALIASING, RenderingHints.VALUE_TEXT_ANTIALIAS_LCD_HRGB);
      // 默认保留旧数据口径；off用于核查Windows编辑器的整数栅格布局。
      boolean fractional = args.length < 2 || !args[1].equals("off");
      graphics.setRenderingHint(RenderingHints.KEY_FRACTIONALMETRICS, fractional
        ? RenderingHints.VALUE_FRACTIONALMETRICS_ON : RenderingHints.VALUE_FRACTIONALMETRICS_OFF);
      double scale = args.length < 3 ? 1.75 : Double.parseDouble(args[2]);
      if (!Double.isFinite(scale) || scale <= 0) throw new IllegalArgumentException("DPI比例须为有限正数");
      graphics.setTransform(AffineTransform.getScaleInstance(scale, scale));
      for (int size = 9; size <= 40; size++) {
        Font font = base.deriveFont((float)size);
        FontMetrics metrics = graphics.getFontMetrics(font);
        int height = metrics.getHeight();
        int line = (int)Math.ceil(height * 1.2f);
        int descent = metrics.getDescent() + (line - height) / 2;
        System.out.printf("{\"size\":%d,\"ascent\":%d,\"descent\":%d,\"leading\":%d,\"height\":%d,\"lineHeight\":%d,\"baseline\":%d,\"spaceWidth\":%.6f}%n", size, metrics.getAscent(), metrics.getDescent(), metrics.getLeading(), height, line, line-descent, font.getStringBounds(" ", graphics.getFontRenderContext()).getWidth());
      }
    } finally { graphics.dispose(); }
  }
}
