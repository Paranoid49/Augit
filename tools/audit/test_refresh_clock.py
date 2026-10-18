"""时钟关联遇到截断、量化异常、倒退或漂移时必须拒绝。"""
import importlib.util
import unittest
from pathlib import Path

spec=importlib.util.spec_from_file_location('refresh_clock',Path(__file__).with_name('correlate-refresh-clock.py'))
module=importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def log_for(times):
    rows=[]
    for time in times:
        pts=round(time*60/1000000)
        rows.extend([f'demuxer -> pkt_pts:{1700000000000000+time}',
            f'demuxer+tsfixup -> pkt_pts:{time}',f'filter_raw -> pts:{time}',
            f'encoder <- type:video frame_pts:{pts} time_base:1/60',
            f'muxer <- pts:{round(pts/60*1000)} pts_time:{round(pts/60,3)}'])
    return chr(10).join(rows)


class ClockTests(unittest.TestCase):
    def test_桌面复制相对时间不得冒充Gdi墙钟(self):
        module.require_gdi_clock({'method':'ffmpeg-gdigrab-desktop-ffv1'})
        for source in [{},{'method':'ffmpeg-ddagrab-desktop-ffv1'}]:
            with self.assertRaises(ValueError):module.require_gdi_clock(source)

    def test_保留输入时钟而不是量化后录像时间(self):
        self.assertEqual(module.frame_clocks(log_for([0,24098]),[0,.017]),[1700000000000,1700000000024.098])

    def test_拒绝截断日志与不匹配录像(self):
        log=log_for([0,24098])
        for text,times in [(log,[0]),(log,[0,.05]),(log.replace('filter_raw ->','missing ->',1),[0,.017])]:
            with self.assertRaises(ValueError):module.frame_clocks(text,times)

    def test_拒绝倒退与编码异常(self):
        for log in [log_for([0,0]),log_for([24098,0]),log_for([0,24098]).replace('frame_pts:1 ','frame_pts:3 ')]:
            with self.assertRaises(ValueError):module.frame_clocks(log,[0,.017])

    def test_阶段重叠与响应之后分别记录(self):
        events=[{'sample':0,'bridgePhases':[{'method':'window/modal-uncover',
            'startedUtcMilliseconds':100,'endedUtcMilliseconds':106,'roundTripMilliseconds':6.2,
            'startAfterMutation':0,'endAfterMutation':6.2}]}]
        result=module.correlate([103,140],events,[{'frame':0},{'frame':1}])
        self.assertEqual(len(result[0]['overlappingBridgePhases']),1)
        self.assertEqual(result[1]['overlappingBridgePhases'],[])
        self.assertEqual(result[1]['nearestPhase']['millisecondsAfterResponse'],34)
        events[0]['bridgePhases'][0]['endedUtcMilliseconds']=150
        with self.assertRaises(ValueError):module.correlate([103,140],events,[{'frame':0}])

    def test_桥接之间跳钟也拒绝(self):
        phase={'method':'window/modal-cover','startedUtcMilliseconds':100,
            'endedUtcMilliseconds':106,'roundTripMilliseconds':6,'startAfterMutation':0,'endAfterMutation':6}
        second={**phase,'method':'window/modal-uncover','startedUtcMilliseconds':150,
            'endedUtcMilliseconds':156,'startAfterMutation':10,'endAfterMutation':16}
        with self.assertRaises(ValueError):module.correlate([103],[{'sample':0,'bridgePhases':[phase,second]}],[{'frame':0}])

    def test_输入尾部未编码帧不错误当成输出帧(self):
        log=log_for([0,24098])+chr(10)+'demuxer -> pkt_pts:1700000000050000'+chr(10)+'demuxer+tsfixup -> pkt_pts:50000'
        self.assertEqual(len(module.frame_clocks(log,[0,.017])),2)

    def test_只接受明确录制截止处的滤镜尾帧(self):
        log=log_for([0,24098])+chr(10)+'demuxer -> pkt_pts:1700000000050000'+chr(10)+'demuxer+tsfixup -> pkt_pts:50000'+chr(10)+'filter_raw -> pts:50000'
        self.assertEqual(len(module.frame_clocks(log,[0,.017],.05)),2)
        for duration in [None,.1]:
            with self.assertRaises(ValueError):module.frame_clocks(log,[0,.017],duration)


if __name__=='__main__':unittest.main()
