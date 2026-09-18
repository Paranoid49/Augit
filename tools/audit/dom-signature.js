// 布局签名抽取器（实时外壳与视觉稿共用）。
//
// 设计目标：同一段脚本分别跑在 WebView2 里的实时外壳与 Chromium 里的视觉稿页面上，
// 按键路径（tag[index] 链）对齐后逐项比对几何、排版、配色与间距，把"跟视觉稿不一致"
// 变成一份可逐条修、也可复跑的差异清单，而不是靠人眼看截图。
//
// 只读 DOM 与计算样式，不修改页面；不依赖任何库。
// 参数：(CSS 选择器，可选 { prune: "选择器,选择器" })。
// prune 命中的元素**自身**仍然记录（容器矩形要参与比对），但不展开其子树：
// 数据行（提交、树行、代码行）在视觉稿里是样例数据、在实时外壳里是真实仓库数据，
// 逐行比对只会淹没有意义的差异。
((selector, options) => {
  const root = document.querySelector(selector);
  if (!root) return null;
  const prune = options && options.prune ? String(options.prune) : '';
  const STYLE_FIELDS = [
    'display', 'position', 'boxSizing', 'flexDirection', 'alignItems', 'justifyContent',
    'gridTemplateColumns', 'gridTemplateRows', 'gap', 'padding', 'margin', 'borderWidth',
    'borderRadius', 'fontFamily', 'fontSize', 'fontWeight', 'fontStyle', 'lineHeight',
    'letterSpacing', 'color', 'backgroundColor', 'borderColor', 'overflow', 'textOverflow',
    'whiteSpace', 'textAlign', 'userSelect', 'opacity', 'boxShadow',
  ];
  const short = (value, limit) => {
    const text = String(value === null || value === undefined ? '' : value).replace(/\s+/g, ' ').trim();
    return text.length > limit ? text.slice(0, limit) : text;
  };
  // textContent 会把 <style>/<script> 里的源码算进来：xterm 会在自己的容器里注入样式表，
  // 于是终端的"文字"变成 `.xterm-rows span { display: inline...` 这种 CSS 片段，
  // 逐页对照就会报出一堆不存在的文字差异。含样式/脚本的节点先剥掉再取文本。
  const visibleText = el => {
    if (!el.querySelector('style,script')) return el.textContent;
    const copy = el.cloneNode(true);
    copy.querySelectorAll('style,script').forEach(node => node.remove());
    return copy.textContent;
  };
  const ownText = el => Array.from(el.childNodes)
    .filter(node => node.nodeType === 3)
    .map(node => node.textContent)
    .join(' ')
    .replace(/\s+/g, ' ')
    .trim();
  // 只看**直接子节点**：早先用 querySelector 取"第一个后代 svg"，于是每个容器都继承了
  // 子树里第一个图标，容器之间必然对不上（每个页面上百条假差异），而真正的图标节点本身
  // 也会被它的父节点重复比对一次。图标只属于承载它的那个节点。
  const iconName = el => {
    const svg = Array.from(el.children).find(node =>
      node.tagName.toLowerCase() === 'svg' && node.hasAttribute('data-augit-icon'));
    return svg ? svg.getAttribute('data-augit-icon') : '';
  };
  const rows = [];
  const walk = (el, depth, path) => {
    const rect = el.getBoundingClientRect();
    const style = getComputedStyle(el);
    const row = {
      depth,
      path,
      tag: el.tagName.toLowerCase(),
      id: el.id || '',
      cls: short(el.className && el.className.baseVal !== undefined ? el.className.baseVal : el.className, 120),
      rect: [Math.round(rect.x), Math.round(rect.y), Math.round(rect.width), Math.round(rect.height)],
      ownText: short(ownText(el), 80),
      text: short(visibleText(el), 60),
      icon: iconName(el),
      scroll: [el.scrollWidth, el.scrollHeight, el.clientWidth, el.clientHeight],
    };
    for (const field of STYLE_FIELDS) {
      row[field] = field === 'fontFamily' ? short(String(style[field]).split(',')[0].replace(/["']/g, ''), 40) : short(style[field], 60);
    }
    rows.push(row);
    if (prune && el !== root && el.matches(prune)) return;
    // 只用元素子节点建路径：文本节点不影响结构对齐。
    const children = Array.from(el.children);
    for (let index = 0; index < children.length; index++) {
      walk(children[index], depth + 1, path + '/' + children[index].tagName.toLowerCase() + '[' + index + ']');
    }
  };
  walk(root, 0, selector);
  return rows;
})
