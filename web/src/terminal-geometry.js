// 对齐参考CharacterGrid的最大字符步长；只用于已加载的随包等宽字体。
// JBR关闭分数度量：设备字号取整，再将0.6em步长取整，不缩放字形。
export function terminalCellWidth(family, size, scale, bundledLoaded) {
  const first = String(family).split(',')[0].trim().replaceAll(String.fromCharCode(34), '').replaceAll(String.fromCharCode(39), '');
  if (first !== 'JetBrains Mono' || !bundledLoaded || !Number.isFinite(size) || size < 9 || size > 40 || !Number.isFinite(scale) || scale <= 0) return 0;
  return Math.round(Math.round(size * scale) * .6) / scale;
}

// 参考基础行距1.0使用AWT字体高度：hhea ascent/descent分别向上取整。
// 不叠加编辑器的1.2行距；无随包字体时让xterm沿用浏览器度量。
export function terminalCellHeight(family, size, scale, bundledLoaded) {
  if (terminalCellWidth(family, size, scale, bundledLoaded) === 0) return 0;
  return Math.ceil(size * 1.02) + Math.ceil(size * .3);
}
