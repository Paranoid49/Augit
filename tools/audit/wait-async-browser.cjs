// 复现当前浏览器驱动的Promise假值误报，并验证审计等待器真实超时。
const assert=require('node:assert/strict');
const {waitAsyncCondition}=require('./wait-async-condition.cjs');
(async()=>{
  const {chromium}=require(process.argv[2]);
  const browser=await chromium.launch({headless:true,channel:'msedge'});
  try{
    const page=await browser.newPage();
    let legacyResult,legacyTimedOut=false;
    try{
      const handle=await page.waitForFunction(async()=>{await new Promise(resolve=>setTimeout(resolve,30));return false;},null,{timeout:300,polling:10});
      try{legacyResult=await handle.jsonValue();}finally{await handle.dispose();}
    }catch(error){if(error.name!=='TimeoutError')throw error;legacyTimedOut=true;}
    await assert.rejects(waitAsyncCondition(()=>page.evaluate(async()=>false),{timeout:100,polling:10}),/等待超时/);
    await page.evaluate(()=>{window.auditPolls=0;});
    await waitAsyncCondition(()=>page.evaluate(async()=>{await new Promise(resolve=>setTimeout(resolve,10));return ++window.auditPolls===3;}));
    assert.equal(await page.evaluate(()=>window.auditPolls),3);
    console.log(JSON.stringify({legacyTimedOut,legacyResult,correctedFalseTimesOut:true,correctedTruePolls:3}));
  }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
