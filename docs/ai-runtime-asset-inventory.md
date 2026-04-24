# Vidvix AI Runtime 资产清单

## 文档状态

- 当前轮次：`R6`
- 记录日期：`2026-04-24`
- 适用目标：`Offline-win-x64`
- 本地资产根目录：`Tools/AI`
- Git 策略：`Tools/AI/**` 本地保留，使用 `.gitignore` 避免大体积第三方运行时直接进入仓库历史
- 当前净保留体积：`57.4 MiB`

## 目录结构

```text
Tools/
  AI/
    Rife/
      Bin/
      Models/
      Configs/
    RealEsrgan/
      Bin/
      Models/
      Configs/
    Licenses/
    Manifests/
```

## RIFE

- 来源仓库：<https://github.com/nihui/rife-ncnn-vulkan>
- 来源 release：<https://github.com/nihui/rife-ncnn-vulkan/releases/tag/20221029>
- 获取日期：`2026-04-24`
- 版本 / 提交信息：`20221029`，release target commit `a7532fc3f9f8f008cd6eecd6f2ffe2a9698e0cf7`
- 下载资产：`rife-ncnn-vulkan-20221029-windows.zip`

保留文件：

- `Tools/AI/Rife/Bin/rife-ncnn-vulkan.exe`
- `Tools/AI/Rife/Bin/vcomp140.dll`
- `Tools/AI/Rife/Models/rife-v4.6/flownet.bin`
- `Tools/AI/Rife/Configs/rife-v4.6/flownet.param`
- `Tools/AI/Licenses/rife-ncnn-vulkan-LICENSE.txt`
- `Tools/AI/Manifests/rife.json`

用途说明：

- `rife-ncnn-vulkan.exe`：离线补帧执行入口
- `vcomp140.dll`：运行时依赖
- `flownet.bin`：`rife-v4.6` 权重文件
- `flownet.param`：`rife-v4.6` 图结构 / 参数定义
- `LICENSE`：MIT 许可证留档
- `manifest`：记录来源、保留项与删减范围，供 `R7` 直接引用

删除文件类别：

- 未纳入首发的历史模型目录：`rife`、`rife-anime`、`rife-HD`、`rife-UHD`、`rife-v2`、`rife-v2.3`、`rife-v2.4`、`rife-v3.0`、`rife-v3.1`、`rife-v4`
- 上游 `README` 副本
- 压缩包、临时下载目录、临时解压目录

## Real-ESRGAN

- 来源仓库：<https://github.com/xinntao/Real-ESRGAN>
- 运行时实现仓库：<https://github.com/xinntao/Real-ESRGAN-ncnn-vulkan>
- 来源 release：<https://github.com/xinntao/Real-ESRGAN/releases/tag/v0.2.5.0>
- 获取日期：`2026-04-24`
- 版本 / 提交信息：`v0.2.5.0`，release target branch `master`
- 下载资产：`realesrgan-ncnn-vulkan-20220424-windows.zip`

保留文件：

- `Tools/AI/RealEsrgan/Bin/realesrgan-ncnn-vulkan.exe`
- `Tools/AI/RealEsrgan/Bin/vcomp140.dll`
- `Tools/AI/RealEsrgan/Models/realesrgan-x4plus.bin`
- `Tools/AI/RealEsrgan/Configs/realesrgan-x4plus.param`
- `Tools/AI/RealEsrgan/Models/realesr-animevideov3-x2.bin`
- `Tools/AI/RealEsrgan/Configs/realesr-animevideov3-x2.param`
- `Tools/AI/RealEsrgan/Models/realesr-animevideov3-x4.bin`
- `Tools/AI/RealEsrgan/Configs/realesr-animevideov3-x4.param`
- `Tools/AI/Licenses/realesrgan-ncnn-vulkan-LICENSE.txt`
- `Tools/AI/Licenses/Real-ESRGAN-LICENSE.txt`
- `Tools/AI/Manifests/realesrgan.json`

用途说明：

- `realesrgan-ncnn-vulkan.exe`：离线增强执行入口
- `vcomp140.dll`：运行时依赖
- `realesrgan-x4plus.*`：`Standard` 档首发模型
- `realesr-animevideov3-x2.*` / `x4.*`：`Anime` 档原生 `2x / 4x` 模型
- `LICENSE`：同时保留 runtime MIT 许可证与主模型仓库 BSD-3-Clause 许可证
- `manifest`：记录来源、保留项与删减范围，供 `R7` 直接引用

删除文件类别：

- 示例媒体：`input.jpg`、`input2.jpg`、`onepiece_demo.mp4`
- 未使用模型：`realesr-animevideov3-x3.*`、`realesrgan-x4plus-anime.*`
- 未使用调试库：`vcomp140d.dll`
- 上游 `README_windows.md`
- 压缩包、临时下载目录、临时解压目录

## R7 直接引用路径

- `RIFE` 可执行文件：`Tools/AI/Rife/Bin/rife-ncnn-vulkan.exe`
- `RIFE` 模型文件：`Tools/AI/Rife/Models/rife-v4.6/flownet.bin`
- `RIFE` 配置文件：`Tools/AI/Rife/Configs/rife-v4.6/flownet.param`
- `Real-ESRGAN` 可执行文件：`Tools/AI/RealEsrgan/Bin/realesrgan-ncnn-vulkan.exe`
- `Real-ESRGAN Standard` 模型 / 配置：`Tools/AI/RealEsrgan/Models/realesrgan-x4plus.bin` / `Tools/AI/RealEsrgan/Configs/realesrgan-x4plus.param`
- `Real-ESRGAN Anime 2x` 模型 / 配置：`Tools/AI/RealEsrgan/Models/realesr-animevideov3-x2.bin` / `Tools/AI/RealEsrgan/Configs/realesr-animevideov3-x2.param`
- `Real-ESRGAN Anime 4x` 模型 / 配置：`Tools/AI/RealEsrgan/Models/realesr-animevideov3-x4.bin` / `Tools/AI/RealEsrgan/Configs/realesr-animevideov3-x4.param`

## 清洁度结论

- `Tools/AI` 只保留首发运行所需的 `exe`、运行库、模型、参数配置、许可证和来源清单
- 未保留上游 `.git`、压缩包残留、测试数据、示例素材、截图、CI 配置、issue 模板和重复 README
- 资产同步入口统一收口到 `scripts/sync-ai-runtime-assets.ps1` 与 `scripts/ai-runtime-lock.json`
