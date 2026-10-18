// 普通单击与并发压力是不同证据，报告必须显式区分。
function settingsOpeningOptions(argv) {
 const counts=argv.filter(value=>value.startsWith('--startup-count='));
 if(counts.length>1)throw Error('启动次数参数重复');
 const startupCount=counts.length?Number(counts[0].slice('--startup-count='.length)):3;
 if(!Number.isSafeInteger(startupCount)||startupCount<1||startupCount>30)throw Error('启动次数必须为1至30');
 if(counts.length&&!argv.includes('--startup-only'))throw Error('启动次数仅用于启动专项');
 return {singleOpen:argv.includes('--single-open'),startupCount};
}
module.exports={settingsOpeningOptions};
