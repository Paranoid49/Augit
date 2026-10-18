import java.awt.*;
import java.awt.font.FontRenderContext;
import java.awt.geom.AffineTransform;
import java.lang.instrument.Instrumentation;
import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;
import javax.swing.*;

/** 只读参考进程诊断：在事件线程读取实际组件，不改变布局、字体或文档。 */
public final class ReferenceMergeGeometry {
  private static final ArrayList<String> rows = new ArrayList<>();
  public static void premain(String output, Instrumentation instrumentation) {
    java.util.Timer timer = new java.util.Timer("Augit reference geometry", true);
    timer.schedule(new java.util.TimerTask() {
      int attempts;
      public void run() {
        EventQueue.invokeLater(() -> {
          try {
            rows.clear();
            for (Window window : Window.getWindows()) if (window.isShowing()) visit(window, "", window);
            Files.writeString(Path.of(output), "{\"pid\":" + ProcessHandle.current().pid() + ",\"components\":[" + String.join(",", rows) + "]}\n", StandardCharsets.UTF_8);
          } catch (Exception error) { error.printStackTrace(); }
        });
        if (++attempts >= 12) timer.cancel();
      }
    }, 5000, 5000);
  }
  private static String quote(Object value) {
    if (value == null) return "null";
    return "\"" + value.toString().replace("\\", "\\\\").replace("\"", "\\\"").replace("\n", "\\n").replace("\r", "\\r").replace("\t", "\\t") + "\"";
  }
  private static Object call(Object target, String name) throws Exception {
    Method method = target.getClass().getMethod(name);
    method.setAccessible(true);
    return method.invoke(target);
  }
  private static String font(Font font) throws Exception {
    if (font == null) return "null";
    Class<?> utilities = Class.forName("sun.font.FontUtilities");
    Object physical = utilities.getMethod("getFont2D", Font.class).invoke(null, font);
    String sample = "first line base value ours value theirs value last line";
    int[] glyphs = font.createGlyphVector(new FontRenderContext(new AffineTransform(), true, false), sample).getGlyphCodes(0, sample.length(), null);
    return "{\"name\":" + quote(font.getFontName()) + ",\"size\":" + font.getSize2D() + ",\"physical\":" + quote(physical) + ",\"undisplayableIndex\":" + font.canDisplayUpTo(sample) + ",\"glyphCodes\":" + Arrays.toString(glyphs) + "}";
  }
  private static Object field(Object target, String name) throws Exception {
    for (Class<?> type = target.getClass(); type != null; type = type.getSuperclass()) {
      try { Field field = type.getDeclaredField(name); field.setAccessible(true); return field.get(target); }
      catch (NoSuchFieldException ignored) { }
    }
    throw new NoSuchFieldException(name);
  }
  // 只读取已经形成的布局缓存；不调用布局生成或绘制，不遍历编辑器/项目对象图。
  private static void cachedFragments(Object value, Set<Object> seen, ArrayList<String> found, int depth) throws Exception {
    if (value == null || depth > 12 || !seen.add(value)) return;
    if (value instanceof Iterable<?> values) {
      for (Object item : values) cachedFragments(item, seen, found, depth + 1);
      return;
    }
    if (value instanceof Object[] values) {
      for (Object item : values) cachedFragments(item, seen, found, depth + 1);
      return;
    }
    String name = value.getClass().getName();
    if (!name.startsWith("com.intellij.openapi.editor.impl.view.")) return;
    if (name.endsWith("SimpleTextFragment")) {
      found.add("{\"class\":" + quote(name) + ",\"text\":" + quote(new String((char[])field(value, "myText")))
        + ",\"font\":" + font((Font)field(value, "myFont")) + ",\"positions\":"
        + Arrays.toString((float[])field(value, "myCharPositions")) + "}");
      return;
    }
    for (Class<?> type = value.getClass(); type != null && type.getName().startsWith("com.intellij.openapi.editor.impl.view."); type = type.getSuperclass()) {
      for (Field candidate : type.getDeclaredFields()) {
        String key = candidate.getName().toLowerCase(Locale.ROOT);
        if (Modifier.isStatic(candidate.getModifiers()) || !(key.contains("chunk") || key.contains("fragment") || key.contains("run"))) continue;
        candidate.setAccessible(true);
        cachedFragments(candidate.get(value), seen, found, depth + 1);
      }
    }
  }
  private static void visit(Component component, String path, Window window) throws Exception {
    if (!component.isShowing()) return;
    String type = component.getClass().getName();
    Point screen = component.getLocationOnScreen();
    StringBuilder row = new StringBuilder("{\"path\":" + quote(path) + ",\"class\":" + quote(type) + ",\"bounds\":" + quote(component.getBounds()) + ",\"screen\":" + quote(screen) + ",\"font\":" + quote(component.getFont()));
    if (component instanceof Window) row.append(",\"scale\":").append(quote(component.getGraphicsConfiguration().getDefaultTransform()));
    if (component instanceof JComponent jc) row.append(",\"insets\":").append(quote(jc.getInsets()));
    // 逻辑screen坐标经过整数换算，不能据此猜测原始像素；同时登记实际绘制变换。
    if (component instanceof JTextField || type.contains("DialogRootPane")) {
      Graphics graphics = component.getGraphics();
      try {
        if (graphics instanceof Graphics2D g) {
          row.append(",\"componentGraphics\":{\"transform\":").append(quote(g.getTransform()))
            .append(",\"clip\":").append(quote(g.getClipBounds())).append('}');
        }
      } finally { if (graphics != null) graphics.dispose(); }
    }
    if (type.contains("EditorGutterComponent")) {
      for (String method : new String[]{"getWhitespaceSeparatorOffset", "getLineNumberAreaWidth", "getLineNumberAreaOffset", "getIconsAreaWidth", "getFoldingAreaOffset"}) {
        try { row.append(",").append(quote(method)).append(':').append(quote(call(component, method))); }
        catch (Exception error) { row.append(",").append(quote(method + "Error")).append(':').append(quote(error.toString())); }
      }
    }
    if (type.contains("EditorComponent")) {
      try {
        Object editor = null;
        for (Class<?> c = component.getClass(); c != null && editor == null; c = c.getSuperclass()) {
          for (Field field : c.getDeclaredFields()) {
            if (field.getType().getName().endsWith("EditorImpl")) { field.setAccessible(true); editor = field.get(component); break; }
          }
        }
        if (editor != null) {
          Object scheme = call(editor, "getColorsScheme");
          Class<?> fontType = Class.forName("com.intellij.openapi.editor.colors.EditorFontType");
          Object plain = fontType.getField("PLAIN").get(null);
          Method getFont = scheme.getClass().getMethod("getFont", fontType); getFont.setAccessible(true);
          row.append(",\"editorFont\":").append(font((Font)getFont.invoke(scheme, plain)));
          row.append(",\"lineHeight\":").append(call(editor, "getLineHeight"));
          row.append(",\"ascent\":").append(call(editor, "getAscent"));
          Method offsetToXY = editor.getClass().getMethod("offsetToXY", int.class);
          row.append(",\"firstCharacter\":").append(quote(offsetToXY.invoke(editor, 0)));
          Object document = call(editor, "getDocument");
          row.append(",\"text\":").append(quote(call(document, "getText")));
          // 使用当前编辑器缓存的绘制上下文，不创建替代字体或更改参考渲染设置。
          Object view = call(editor, "getView");
          ArrayList<String> fragments = new ArrayList<>();
          cachedFragments(field(field(view, "myTextLayoutCache"), "myLines"),
            Collections.newSetFromMap(new IdentityHashMap<>()), fragments, 0);
          row.append(",\"cachedFragments\":[").append(String.join(",", fragments)).append(']');
          Method getContext = view.getClass().getDeclaredMethod("getFontRenderContext");
          getContext.setAccessible(true);
          FontRenderContext context = (FontRenderContext)getContext.invoke(view);
          row.append(",\"renderContext\":{\"transform\":").append(quote(context.getTransform()))
            .append(",\"antialiasing\":").append(quote(context.getAntiAliasingHint()))
            .append(",\"fractionalMetrics\":").append(quote(context.getFractionalMetricsHint())).append('}');
          String text = (String)call(document, "getText");
          int offset = 0;
          ArrayList<String> positions = new ArrayList<>();
          for (String line : text.split("\n", -1)) {
            positions.add("{\"offset\":" + offset + ",\"point\":" + quote(offsetToXY.invoke(editor, offset)) + "}");
            offset += line.length() + 1;
          }
          row.append(",\"linePositions\":[").append(String.join(",", positions)).append(']');
          Graphics graphics = component.getGraphics();
          try {
            if (graphics instanceof Graphics2D g) {
              row.append(",\"componentGraphics\":{\"transform\":").append(quote(g.getTransform()))
                .append(",\"clip\":").append(quote(g.getClipBounds())).append('}');
            }
          } finally { if (graphics != null) graphics.dispose(); }
        }
      } catch (Exception error) { row.append(",\"editorError\":").append(quote(error.toString())); }
    }
    rows.add(row.append('}').toString());
    if (component instanceof Container container) {
      Component[] children = container.getComponents();
      for (int index = 0; index < children.length; index++) visit(children[index], path + "/" + index, window);
    }
  }
  public static void main(String[] args) throws Exception {
    if (!quote("a\n\"b\\c").equals("\"a\\n\\\"b\\\\c\"")) throw new AssertionError("JSON转义错误");
    class Parent { private final String token = "只读"; }
    class Child extends Parent { }
    if (!field(new Child(), "token").equals("只读")) throw new AssertionError("继承字段读取错误");
    try { field(new Child(), "missing"); throw new AssertionError("缺失字段未拒绝"); }
    catch (NoSuchFieldException expected) { }
    ArrayList<Object> cycle = new ArrayList<>(); cycle.add(cycle); cycle.add(new Child());
    ArrayList<String> fragments = new ArrayList<>();
    cachedFragments(cycle, Collections.newSetFromMap(new IdentityHashMap<>()), fragments, 0);
    if (!fragments.isEmpty()) throw new AssertionError("越界读取非布局对象");
    System.out.println("只读诊断序列化自检通过");
  }
}
