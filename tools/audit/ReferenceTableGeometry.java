import com.sun.tools.attach.VirtualMachine;
import java.awt.*;
import java.lang.instrument.Instrumentation;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.ArrayList;
import javax.swing.*;
import javax.swing.table.TableColumn;

/** 单次读取已显示参考表格；不改变模型、选择、布局或绘制状态。 */
public final class ReferenceTableGeometry {
  static String table(JTable table) {
    ArrayList<String> columns = new ArrayList<>();
    for (int i = 0; i < table.getColumnCount(); i++) {
      TableColumn column = table.getColumnModel().getColumn(i);
      columns.add("{\"index\":" + i + ",\"width\":" + column.getWidth()
          + ",\"preferred\":" + column.getPreferredWidth() + ",\"minimum\":" + column.getMinWidth()
          + ",\"maximum\":" + column.getMaxWidth() + "}");
    }
    return "{\"width\":" + table.getWidth() + ",\"height\":" + table.getHeight()
        + ",\"rowHeight\":" + table.getRowHeight() + ",\"rowCount\":" + table.getRowCount()
        + ",\"autoResizeMode\":" + table.getAutoResizeMode()
        + ",\"headerHeight\":" + table.getTableHeader().getHeight()
        + ",\"columns\":[" + String.join(",", columns) + "]}";
  }
  static void visit(Component component, ArrayList<String> rows) {
    if (!component.isShowing()) return;
    if (component instanceof JTable table) rows.add(table(table));
    if (component instanceof Container container)
      for (Component child : container.getComponents()) visit(child, rows);
  }
  public static void agentmain(String output, Instrumentation instrumentation) throws Exception {
    EventQueue.invokeAndWait(() -> {
      try {
        ArrayList<String> tables = new ArrayList<>();
        for (Window window : Window.getWindows())
          if (window instanceof Dialog dialog && dialog.isShowing() && "Git Remotes".equals(dialog.getTitle())) visit(window, tables);
        if (tables.size() != 1) throw new IllegalStateException("已显示Git Remotes表格必须唯一，实际=" + tables.size());
        String json = "{\"pid\":" + ProcessHandle.current().pid() + ",\"tables\":[" + String.join(",", tables) + "]}";
        Files.writeString(Path.of(output), json + System.lineSeparator(), StandardCharsets.UTF_8, StandardOpenOption.CREATE_NEW);
      } catch (Exception error) { throw new IllegalStateException(error); }
    });
  }
  public static void main(String[] args) throws Exception {
    if (args.length == 0) {
      JTable table = new JTable(0, 2); table.setSize(330, 70);
      table.getColumnModel().getColumn(0).setPreferredWidth(70);
      table.getColumnModel().getColumn(1).setPreferredWidth(250); table.doLayout();
      String result = table(table);
      if (!result.contains("\"rowCount\":0") || !result.contains("\"width\":75")
          || !result.contains("\"width\":255") || !result.contains("\"preferred\":70")) throw new AssertionError(result);
      System.out.println(result);
      for (int width : new int[]{331, 300, 100, 30}) {
        table.setSize(width, 70); table.doLayout();
        int expected = Math.max(15, (int)Math.round(70 + Math.max(0, width - 320) / 2.0
            + Math.min(0, width - 320) * 55.0 / 290));
        if (table.getColumnModel().getColumn(0).getWidth() != expected) throw new AssertionError(table(table));
        System.out.println(table(table));
      }
      return;
    }
    if (args.length != 3) throw new IllegalArgumentException("需要PID、诊断jar和全新输出路径");
    Path output = Path.of(args[2]);
    if (!output.isAbsolute() || Files.exists(output)) throw new IllegalArgumentException("拒绝覆盖或相对输出路径");
    VirtualMachine machine = VirtualMachine.attach(args[0]);
    try { machine.loadAgent(Path.of(args[1]).toAbsolutePath().toString(), output.toString()); }
    finally { machine.detach(); }
  }
}
