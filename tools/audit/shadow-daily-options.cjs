// 日常阴影验收不接受渲染、尺寸或场景覆盖，沿用用户实际配置。
function validateDailyShadowOptions(args) {
 const daily=args.includes('--daily-launcher');
 if(daily) for(const flag of ['--software-rendering','--disable-direct-composition','--disable-native-occlusion','--uniform-background','--goto-line','--reference-frame','--position-window','--bundled-web'])
  if(args.includes(flag))throw Error('日常入口不接受覆盖：'+flag);
 return daily;
}
module.exports={validateDailyShadowOptions};
