// 字体资源描述符不属于界面角色；先屏蔽注释、字符串，再剔除独立字体声明。
function roleWeights(css) {
  const declarations=css.replace(/\/\*[\s\S]*?\*\/|"(?:\\[\s\S]|[^"\\])*"|'(?:\\[\s\S]|[^'\\])*'/g,match=>' '.repeat(match.length))
    .replace(/@font-face\s*\{[^{}]*\}/gi,'');
  return new Set([...declarations.matchAll(/font-weight\s*:\s*([0-9]+|normal|bold|bolder|lighter)\b/gi)].map(match=>match[1].toLowerCase()));
}
module.exports={roleWeights};
