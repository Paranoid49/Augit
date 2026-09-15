// 极简 Markdown 渲染器：只生成受控标签，所有文本都经过转义，因此文档里的原始 HTML 不会生效。
// 覆盖 ux-spec 要求的范围：标题、段落、列表、代码块、行内代码、引用、表格、任务列表、删除线、链接与图片。
// 远程图片只作为占位提示呈现，与既有原生实现一致，不发起网络请求。

const ESCAPES = { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" };

function escapeHtml(text) {
  return String(text).replace(/[&<>"']/g, (character) => ESCAPES[character]);
}

function escapeAttribute(text) {
  return escapeHtml(text);
}

/** 行内语法：先转义，再按受控规则还原标记。 */
function renderInline(text) {
  let output = escapeHtml(text);
  output = output.replace(/`([^`]+)`/g, (_, code) => `<code>${code}</code>`);
  output = output.replace(/~~([^~]+)~~/g, "<del>$1</del>");
  output = output.replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>");
  output = output.replace(/(^|[^*])\*([^*]+)\*/g, "$1<em>$2</em>");
  // 图片：仅相对路径与 data: 直接呈现，远程地址给出去网络请求的占位说明。
  output = output.replace(/!\[([^\]]*)\]\(([^)\s]+)\)/g, (_, alt, source) => {
    const remote = /^(https?:)?\/\//i.test(source);
    if (remote) {
      return `<span class="markdown-blocked" title="${escapeAttribute(source)}">远程图片已阻止：${escapeHtml(alt || source)}</span>`;
    }
    return `<img src="${escapeAttribute(source)}" alt="${escapeAttribute(alt)}">`;
  });
  output = output.replace(/\[([^\]]+)\]\(([^)\s]+)\)/g, (_, label, target) => {
    const external = /^(https?:)?\/\//i.test(target) || /^mailto:/i.test(target);
    if (external) {
      return `<a href="#" data-external-link="${escapeAttribute(target)}" rel="noreferrer">${label}</a>`;
    }
    return `<a href="#" data-document-link="${escapeAttribute(target)}">${label}</a>`;
  });
  return output;
}

/**
 * 渲染 Markdown 为受控 HTML。
 * @param {string} text 原始 Markdown 文本
 * @returns {string} 仅含安全标签的 HTML
 */
export function renderMarkdown(text) {
  const lines = String(text ?? "").replace(/\r\n?/g, "\n").split("\n");
  const html = [];
  let index = 0;
  let paragraph = [];

  const flushParagraph = () => {
    if (paragraph.length > 0) {
      html.push(`<p>${renderInline(paragraph.join(" "))}</p>`);
      paragraph = [];
    }
  };

  while (index < lines.length) {
    const line = lines[index];

    // 围栏代码块：内容原样转义，不做行内解析。
    const fence = /^\s*(```|~~~)\s*([^\s`]*)\s*$/.exec(line);
    if (fence) {
      flushParagraph();
      const marker = fence[1];
      const language = fence[2];
      const body = [];
      index++;
      while (index < lines.length && !new RegExp(`^\\s*${marker}\\s*$`).test(lines[index])) {
        body.push(lines[index]);
        index++;
      }
      index++;
      const className = language ? ` class="language-${escapeAttribute(language)}"` : "";
      html.push(`<pre><code${className}>${escapeHtml(body.join("\n"))}</code></pre>`);
      continue;
    }

    // 标题。
    const heading = /^(#{1,6})\s+(.*)$/.exec(line);
    if (heading) {
      flushParagraph();
      const level = heading[1].length;
      html.push(`<h${level}>${renderInline(heading[2].trim())}</h${level}>`);
      index++;
      continue;
    }

    // 分隔线。
    if (/^\s*([-*_])\s*(\1\s*){2,}$/.test(line)) {
      flushParagraph();
      html.push("<hr>");
      index++;
      continue;
    }

    // 引用块。
    if (/^\s*>\s?/.test(line)) {
      flushParagraph();
      const quoted = [];
      while (index < lines.length && /^\s*>\s?/.test(lines[index])) {
        quoted.push(lines[index].replace(/^\s*>\s?/, ""));
        index++;
      }
      html.push(`<blockquote>${renderMarkdown(quoted.join("\n"))}</blockquote>`);
      continue;
    }

    // 表格：表头行 + 分隔行。
    if (/\|/.test(line) && index + 1 < lines.length && /^\s*\|?[\s:|-]+\|[\s:|-]*$/.test(lines[index + 1])) {
      flushParagraph();
      const parseRow = (row) => row.replace(/^\s*\|/, "").replace(/\|\s*$/, "").split("|").map((cell) => cell.trim());
      const header = parseRow(line);
      index += 2;
      const rows = [];
      while (index < lines.length && /\|/.test(lines[index]) && lines[index].trim() !== "") {
        rows.push(parseRow(lines[index]));
        index++;
      }
      const head = header.map((cell) => `<th>${renderInline(cell)}</th>`).join("");
      const body = rows.map((row) => `<tr>${row.map((cell) => `<td>${renderInline(cell)}</td>`).join("")}</tr>`).join("");
      html.push(`<table><thead><tr>${head}</tr></thead><tbody>${body}</tbody></table>`);
      continue;
    }

    // 列表（有序、无序、任务列表）。
    const listItem = /^(\s*)([-*+]|\d+[.)])\s+(.*)$/.exec(line);
    if (listItem) {
      flushParagraph();
      const ordered = /\d/.test(listItem[2]);
      const items = [];
      while (index < lines.length) {
        const current = /^(\s*)([-*+]|\d+[.)])\s+(.*)$/.exec(lines[index]);
        if (!current || /\d/.test(current[2]) !== ordered) break;
        let content = current[3];
        let marker = "";
        const task = /^\[([ xX])\]\s+(.*)$/.exec(content);
        if (task) {
          const checked = task[1].toLowerCase() === "x";
          marker = `<input type="checkbox" disabled${checked ? " checked" : ""}> `;
          content = task[2];
        }
        items.push(`<li>${marker}${renderInline(content)}</li>`);
        index++;
      }
      html.push(ordered ? `<ol>${items.join("")}</ol>` : `<ul>${items.join("")}</ul>`);
      continue;
    }

    if (line.trim() === "") {
      flushParagraph();
      index++;
      continue;
    }

    paragraph.push(line.trim());
    index++;
  }

  flushParagraph();
  return html.join("\n");
}
