// 功能：视频裁剪导入结果状态枚举（区分成功、可提示拒绝与真正失败）
// 模块：裁剪模块
// 说明：可复用，供裁剪 ViewModel 按不同结果应用对应的界面状态。
namespace Vidvix.Core.Models;

public enum VideoTrimImportOutcome
{
    Success = 0,
    Rejected = 1,
    Failed = 2
}
