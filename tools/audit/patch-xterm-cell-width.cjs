// 保留上游原包；只对锁定版本生成可复现的DOM格宽扩展。
const fs = require('node:fs'), path = require('node:path');
const { createHash } = require('node:crypto');
const root = path.resolve(__dirname, '../..');
function patch(source) {
  if (createHash('sha256').update(source).digest('hex') !== '14903579ff54664cd72f8e8699e6961a6272c21863ec1c3b118cdc8af5d4a972') throw Error('上游xterm哈希不符，须重新审查补丁');
  let output = source.toString('utf8');
  const changes = [
    ['this._widthCache=new o.WidthCache(this._document,this._helperContainer),', 'this._widthCache=new o.WidthCache(this._document,this._helperContainer),this._widthCache.augitPreciseWidth=()=>this._optionsService.rawOptions.augitCellWidth>0,'],
    ['_handleOptionsChanged(){this._updateDimensions(),', '_handleOptionsChanged(){this._widthCache.clear(),this._updateDimensions(),'],
    ['i.offsetWidth/32}}},9176:', '(this.augitPreciseWidth?.()?i.getBoundingClientRect().width:i.offsetWidth)/32}}},9176:'],
    ['letterSpacing:0,', 'letterSpacing:0,augitCellWidth:0,augitCellHeight:0,'],
    ['_sanitizeAndValidateOption(e,i){switch(e){', '_sanitizeAndValidateOption(e,i){switch(e){case"augitCellWidth":case"augitCellHeight":if(typeof i!=="number"||!Number.isFinite(i)||i<0)throw new Error("终端格尺寸必须为有限非负数");break;'],
    ['this.dimensions.device.cell.height=Math.floor(this.dimensions.device.char.height*this._optionsService.rawOptions.lineHeight)', 'this.dimensions.device.cell.height=this._optionsService.rawOptions.augitCellHeight>0?this._optionsService.rawOptions.augitCellHeight*e:Math.floor(this.dimensions.device.char.height*this._optionsService.rawOptions.lineHeight)'],
    ['this.dimensions.device.cell.width=this.dimensions.device.char.width+Math.round(this._optionsService.rawOptions.letterSpacing)', 'this.dimensions.device.cell.width=this._optionsService.rawOptions.augitCellWidth>0?this._optionsService.rawOptions.augitCellWidth*e:this.dimensions.device.char.width+Math.round(this._optionsService.rawOptions.letterSpacing)'],
  ];
  for (const [before, after] of changes) {
    if (output.split(before).length !== 2) throw Error('补丁锚点不唯一');
    output = output.replace(before, after);
  }
  return Buffer.from(output, 'utf8');
}
if (require.main === module) {
  const output = patch(fs.readFileSync(path.join(root, 'tools/xterm/xterm-6.0.0/lib/xterm.js')));
  const target = path.join(root, 'web/vendor/xterm/xterm.js');
  if (process.argv.includes('--check')) {
    if (!output.equals(fs.readFileSync(target))) throw Error('运行时xterm与确定性补丁不一致');
  } else fs.writeFileSync(target, output);
  console.log(JSON.stringify({ size: output.length, sha256: createHash('sha256').update(output).digest('hex') }));
}
module.exports = { patch };
