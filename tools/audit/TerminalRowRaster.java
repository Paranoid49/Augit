import java.awt.Font;
import java.awt.Color;
import java.awt.RenderingHints;
import java.awt.image.BufferedImage;
import java.io.File;
import javax.imageio.ImageIO;

// 独立JBR渲染用于分析分数行格的舍入规律，不冒充PyCharm窗口截图。
class TerminalRowRaster {
  public static void main(String[] args) throws Exception {
    Font font = Font.createFont(Font.TRUETYPE_FONT, new File(args[0])).deriveFont(13f);
    File output = new File(args[1]); output.mkdirs();
    for (int phase = 0; phase < 4; phase++) {
      BufferedImage image = new BufferedImage(350, 700, BufferedImage.TYPE_INT_RGB);
      var g = image.createGraphics();
      try {
        g.setColor(Color.WHITE); g.fillRect(0, 0, image.getWidth(), image.getHeight());
        g.scale(1.75, 1.75); g.translate(0, phase / 4.0 / 1.75);
        g.setRenderingHint(RenderingHints.KEY_TEXT_ANTIALIASING, RenderingHints.VALUE_TEXT_ANTIALIAS_LCD_HRGB);
        g.setRenderingHint(RenderingHints.KEY_FRACTIONALMETRICS, RenderingHints.VALUE_FRACTIONALMETRICS_OFF);
        g.setFont(font); g.setColor(Color.BLACK);
        var metrics = g.getFontMetrics();
        for(int row=0; row<20; row++) g.drawString("dddddddd", 20, 20+metrics.getAscent()+row*metrics.getHeight());
        System.out.println("phase="+phase+" height="+metrics.getHeight()+" ascent="+metrics.getAscent());
        ImageIO.write(image, "png", new File(output,"jbr-phase-"+phase+".png"));
      } finally {g.dispose();}
    }
  }
}
