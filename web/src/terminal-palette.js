// DefaultColorSchemesManager的ANSI顺序，经ColoredOutputTypeRegistry映射；不使用xterm默认色。
const keys = ['black','red','green','yellow','blue','magenta','cyan','white',
  'brightBlack','brightRed','brightGreen','brightYellow','brightBlue','brightMagenta','brightCyan','brightWhite'];
const light = ['000000','CE0505','067D17','B28C00','063FDB','B309B3','028E8E','929292',
  '656565','FF1616','16B42C','ECC32C','0060AF','E617E6','15C1C1','C9C9C9'];
const dark = ['000000','F0524F','5C962C','A68A0D','3993D4','A771BF','00A3A3','808080',
  '595959','FF4050','4FC414','E5BF00','1FB0FF','ED7EED','00E5E5','FFFFFF'];

export function terminalAnsiPalette(theme) {
  const values = theme === 'dark' ? dark : light;
  return Object.fromEntries(keys.map((key,index)=>[key,`#${values[index]}`]));
}

// 终端沿用编辑器方案的选区键，不使用工具窗列表的选中蓝。
// 安装版Light覆盖Default活动色；Islands Dark继承Darcula，失焦色由父方案提供。
export function terminalSelectionPalette(theme) {
  return theme === 'dark'
    ? {selectionBackground:'#214283', selectionInactiveBackground:'#4C4F56'}
    : {selectionBackground:'#A6D2FF', selectionInactiveBackground:'#B5B7BD'};
}
