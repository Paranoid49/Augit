// 桌面复制仅用于已核实原点为(0,0)的单显示器，记录真实采集路径。
const assert=require('node:assert/strict');
function recorderArguments(rect,video,desktopDuplication=false,repeatStaticFrames=false,durationSeconds=12) {
 assert.ok(Number.isInteger(durationSeconds)&&durationSeconds>=1&&durationSeconds<=25,'录像时长须为1至25秒');
 assert.ok(Array.isArray(rect)&&rect.length===4&&rect.every(Number.isSafeInteger),'采样矩形须为整数');
 const [left,top,right,bottom]=rect;
 assert.ok(right>left&&bottom>top,'采样区域不能为空');
 if(desktopDuplication)assert.ok(left>=0&&top>=0,'桌面复制本批仅支持已核实的零原点显示器');
 const input=desktopDuplication
  ? ['-f','lavfi','-i',`ddagrab=output_idx=0:draw_mouse=0:framerate=60:dup_frames=${repeatStaticFrames?1:0}:output_fmt=bgra:offset_x=${left}:offset_y=${top}:video_size=${right-left}x${bottom-top},hwdownload,format=bgra`]
  : ['-f','gdigrab','-draw_mouse','0','-framerate','60','-offset_x',String(left),'-offset_y',String(top),'-video_size',`${right-left}x${bottom-top}`,'-i','desktop'];
 // GDI也保留微秒编码时基，避免按60Hz量化后不同输入帧得到同一时间戳。
 return ['-hide_banner','-debug_ts',...(desktopDuplication?[]:['-nostdin']),'-n',...input,'-t',String(durationSeconds),'-c:v','ffv1','-level','3','-pix_fmt','bgr0','-enc_time_base','1:1000000','-fps_mode','passthrough','-stats_period','0.1','-progress','pipe:1',video];
}
// 事件采集在静止桌面可能等不到第12秒；场景结束后正常封装，不伪造刷新帧。
function finishEventRecording(recorder,desktopDuplication) {
 if(desktopDuplication&&recorder.exitCode===null)recorder.stdin.write('q');
}
function recordingDeadline(recorder,setTimer=setTimeout,clearTimer=clearTimeout) {
 let timedOut=false;
 const timer=setTimer(()=>{timedOut=true;if(recorder.exitCode===null)recorder.kill();},30000);
 return {get timedOut(){return timedOut;},dispose(){clearTimer(timer);}};
}
module.exports={recorderArguments,recordingDeadline,finishEventRecording};
