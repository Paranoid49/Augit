const {test}=require('node:test');
const assert=require('node:assert/strict');
const {recorderArguments,recordingDeadline,finishEventRecording}=require('./refresh-recorder-arguments.cjs');
test('完整设置流程可显式延长，默认及最大时长仍有界',()=>{
 for(const duration of [12,25]){const a=recorderArguments([0,0,100,80],'x',true,true,duration);assert.equal(a[a.indexOf('-t')+1],String(duration));}
 for(const duration of [0,26,NaN,1.5])assert.throws(()=>recorderArguments([0,0,100,80],'x',true,true,duration));
});
test('默认保留GDI无损和原始坐标',()=>{
 const a=recorderArguments([-20,5,100,85],'evidence.mkv');
 assert.equal(a[a.indexOf('-f')+1],'gdigrab');
 assert.equal(a[a.indexOf('-offset_x')+1],'-20');
 assert.equal(a[a.indexOf('-video_size')+1],'120x80');
 assert.equal(a[a.indexOf('-c:v')+1],'ffv1');
 assert.equal(a[a.indexOf('-fps_mode')+1],'passthrough');
 assert.equal(a[a.indexOf('-enc_time_base')+1],'1:1000000');
});
test('桌面复制保留BGRA且不生成重复帧或缩放',()=>{
 const a=recorderArguments([100,200,1920,1541],'evidence.mkv',true);
 assert.equal(a[a.indexOf('-i')+1],'ddagrab=output_idx=0:draw_mouse=0:framerate=60:dup_frames=0:output_fmt=bgra:offset_x=100:offset_y=200:video_size=1820x1341,hwdownload,format=bgra');
 assert.ok(a.includes('-n'));assert.equal(a[a.indexOf('-c:v')+1],'ffv1');
 assert.equal(a[a.indexOf('-enc_time_base')+1],'1:1000000');
 assert.ok(!a.includes('-nostdin'));
});
test('仅向存活的事件采集进程发送正常结束命令',()=>{
 for(const desktop of [false,true])for(const exitCode of [null,0]) {
  const commands=[];
  finishEventRecording({exitCode,stdin:{write(value){commands.push(value);}}},desktop);
  assert.deepEqual(commands,desktop&&exitCode===null?['q']:[]);
 }
});
test('静止重复帧必须显式选择并保留无损像素',()=>{
 const args=recorderArguments([0,0,100,80],'sample.mkv',true,true);
 assert.ok(args[args.indexOf('-i')+1].includes('dup_frames=1:output_fmt=bgra'));
 assert.equal(args[args.indexOf('-c:v')+1],'ffv1');
});
test('拒绝无效区域及未经支持的负显示器原点',()=>{
 for(const r of [[0,0,0,20],[0,0,NaN,20],[.5,0,20,20]])assert.throws(()=>recorderArguments(r,'x'));
 assert.throws(()=>recorderArguments([-1,0,20,20],'x',true));
});
test('事件源超过墙钟限制时仅停止仍存活的本次进程',()=>{
 for(const exitCode of [null,0]) {
  let callback,killed=0,cleared=false;
  const deadline=recordingDeadline({exitCode,kill(){killed++;}},(cb,ms)=>{assert.equal(ms,30000);callback=cb;return 17;},id=>{assert.equal(id,17);cleared=true;});
  assert.equal(deadline.timedOut,false);callback();
  assert.equal(deadline.timedOut,true);assert.equal(killed,exitCode===null?1:0);
  deadline.dispose();assert.equal(cleared,true);
 }
});
