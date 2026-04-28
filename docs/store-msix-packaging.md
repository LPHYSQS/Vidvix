# Vidvix Store MSIX 打包与交接手册

## 文档目的

这份文档记录 Vidvix 当前已经跑通的 Microsoft Store 打包链路，供后续版本升级、重新打包、Partner Center 上传、本地侧载验证和 AI Agent 继任排障使用。

这次修复后，项目已经不再走“手写 `AppxManifest.xml` + `makeappx.exe`”的旧方案，而是改回 Windows App SDK 官方推荐的 single-project MSIX 流程。

## 当前链路的核心结论

1. Store 包必须使用官方 `dotnet publish + Store-win-x64.pubxml` 生成。
2. 上传到 Partner Center 的主文件必须是 `.msixupload`，不是 `_Test` 目录里的 `.msix`。
3. 本地安装验证使用 `_Test` 目录里的 `.msix`，但必须先用临时测试证书重新签名并信任证书。
4. 安装界面显示空白图标时，优先检查 `Assets` 里的 MSIX 资源图本身是不是模板占位图，不要先怀疑 Store 打包命令。
5. 不要重新启用 `WindowsAppSdkDeploymentManagerInitialize`，不要把项目全局切成永久 `WindowsPackageType=MSIX`。

## 相关文件

- 打包清单：[Package.appxmanifest](/E:/净云流枫工作室/Vidvix/Package.appxmanifest)
- Store 发布配置：[Store-win-x64.pubxml](/E:/净云流枫工作室/Vidvix/Properties/PublishProfiles/Store-win-x64.pubxml)
- 项目主文件：[Vidvix.csproj](/E:/净云流枫工作室/Vidvix/Vidvix.csproj)
- 生成 MSIX 图标资源脚本：[generate-msix-assets.ps1](/E:/净云流枫工作室/Vidvix/scripts/generate-msix-assets.ps1)
- 本地测试签名脚本：[sign-store-test-package.ps1](/E:/净云流枫工作室/Vidvix/scripts/sign-store-test-package.ps1)

## 现在的正确打包流程

### 1. 发版前准备

先确认以下几件事：

1. 版本号已经同步更新。
2. 主图标 `Assets\logo.png` 是正式图标，不是临时图或模板图。
3. `Package.appxmanifest` 的 `Identity Version` 与 `Vidvix.csproj` 里的 `Version`、`AssemblyVersion`、`FileVersion` 保持一致。
4. 不要手工去改 `_Test` 目录里的内容，那些都是构建产物。

当前需要同步关注的版本字段：

- [Vidvix.csproj](/E:/净云流枫工作室/Vidvix/Vidvix.csproj) 里的 `<Version>`
- [Vidvix.csproj](/E:/净云流枫工作室/Vidvix/Vidvix.csproj) 里的 `<AssemblyVersion>`
- [Vidvix.csproj](/E:/净云流枫工作室/Vidvix/Vidvix.csproj) 里的 `<FileVersion>`
- [Package.appxmanifest](/E:/净云流枫工作室/Vidvix/Package.appxmanifest) 里的 `<Identity Version="...">`

如果只改了 `csproj` 版本号，没有同步改 `Package.appxmanifest`，后续很容易出现包名版本和包身份版本不同步，给排查制造干扰。

### 2. 如果图标改过，先重建整套 MSIX 资源

如果更换了品牌图标，或者怀疑安装界面、开始菜单、商店页图标不对，先运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\generate-msix-assets.ps1
```

这个脚本会以 `Assets\logo.png` 为母版，重新生成以下资源：

- `StoreLogo.png` 和 `StoreLogo.scale-*`
- `Square44x44Logo.png`、`Square44x44Logo.scale-*`、`Square44x44Logo.targetsize-*`
- `Square150x150Logo.png` 和 `Square150x150Logo.scale-*`
- `Wide310x150Logo.png` 和 `Wide310x150Logo.scale-*`
- `SplashScreen.png` 和 `SplashScreen.scale-*`
- `LockScreenLogo.png` 和 `LockScreenLogo.scale-*`

建议：

- 每次更换主图标后都先跑一次这个脚本，再打包。
- 即使没有换图，怀疑图标异常时也可以先重跑一次，成本很低。

### 3. 生成可上传到 Partner Center 的 Store 包

执行：

```powershell
dotnet publish .\Vidvix.csproj -p:PublishProfile=Properties\PublishProfiles\Store-win-x64.pubxml
```

成功后会生成三类关键产物：

- Partner Center 上传文件：
  `artifacts\store-submission\Vidvix-v<版本>-store-x64.msixupload`
- 本地安装测试目录：
  `artifacts\store-submission\Vidvix-v<版本>-store-x64_Test\`
- Store 发布镜像目录：
  `artifacts\store-build\publish\win-x64\`

真正上传时使用：

`artifacts\store-submission\Vidvix-v<版本>-store-x64.msixupload`

不要上传 `_Test` 目录里的 `.msix`。

### 4. 本地安装验证

先生成并签名测试包：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\sign-store-test-package.ps1 -TestPackageDirectory .\artifacts\store-submission\Vidvix-v<版本>-store-x64_Test
```

如果要在本机直接信任证书并安装测试包，请使用管理员 PowerShell：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\sign-store-test-package.ps1 -TestPackageDirectory .\artifacts\store-submission\Vidvix-v<版本>-store-x64_Test -InstallPackage
```

这一步会做三件事：

1. 创建自签名测试证书。
2. 用该证书重新签名 `_Test` 目录里的 `.msix`。
3. 把证书导入本机信任存储并安装包。

成功后会在 `_Test` 目录里看到对应的 `.cer` 文件。

### 5. Partner Center 上传

本地验证通过后，上传：

`artifacts\store-submission\Vidvix-v<版本>-store-x64.msixupload`

建议上传前再检查一次：

1. `_Test` 目录中的本地安装和启动已经验证通过。
2. 当前上传文件对应的是这次刚刚构建的新产物。
3. 没有把旧版手工打的 `.msix` 误当成上传包。

## 本次实际踩过的坑

### 坑 1：旧链路是“手写清单 + makeappx”，不适合作为长期维护方案

之前项目不是标准的 single-project MSIX，而是：

1. 先普通 `dotnet publish`
2. 再手写 `AppxManifest.xml`
3. 最后自己调用 `makeappx.exe` 拼出 `.msix`

这条链路的问题是：

- 图标资源很容易拼错。
- 入口模型很容易拼错。
- 依赖声明很容易缺失。
- 输出不是官方更推荐的 `.msixupload`。
- 继任者很难判断到底哪一步才是“真正的发布源”。

当前结论：

- 不要再回退到这条手工封箱链路。
- 以 [Store-win-x64.pubxml](/E:/净云流枫工作室/Vidvix/Properties/PublishProfiles/Store-win-x64.pubxml) 为唯一 Store 打包入口。

### 坑 2：项目默认是非打包应用，Store 只在指定发布配置下启用 MSIX

当前项目策略是：

- 默认：`WindowsPackageType=None`
- 仅 `Store-win-x64` 发布时：`WindowsPackageType=MSIX`

这样做的原因是：

- 日常开发和离线发布链路仍然保留非打包应用行为。
- Store 包才启用包身份。
- 可以避免把本地开发调试、离线分发和商店分发混成一条链。

不要把 `WindowsPackageType` 全局改成 `MSIX`，否则会影响非 Store 场景。

### 坑 3：`WindowsAppSdkDeploymentManagerInitialize` 不能在 Store 包里乱开

之前 Store 发布配置里启用了自动 DeploymentManager 初始化，这会让应用启动时额外进入 DeploymentManager 逻辑。

对 MSIX / Store 场景来说，这通常不是想要的行为，因为：

- Store 包本来就应该依靠包依赖图解决运行时问题。
- 启动时再做部署管理，容易引入额外异常。
- 会制造“本地 exe 能跑，打包后启动即闪退”的高风险差异。

当前结论：

- `Store-win-x64.pubxml` 中保持 `WindowsAppSdkDeploymentManagerInitialize=false`
- 不要为了“修运行时”重新把它打开

### 坑 4：离线发布校验目标不能反过来拦住 Store 打包

项目里原本就有一套离线发布校验逻辑，用来检查 `ffmpeg`、`mpv`、AI 模型和其他运行时是否在发布输出里。

这套逻辑本身没有错，但如果不区分 Store 发布与离线发布，容易出现：

- Store 打包也被按离线发布规则拦截
- 输出检查条件不匹配
- 继任者误判为“MSIX 打包坏了”

当前结论：

- Store 打包和离线发布校验要分开
- 相关目标在 `Vidvix.csproj` 里已经按 `IsStoreWinX64Publish` 做了隔离

### 坑 5：安装界面空白图标，不一定是 MSIX 清单错，更可能是资源图本身就是模板图

这是本次最关键的一坑。

当时安装界面右上角一直显示蓝底白框占位图，第一反应很容易怀疑：

- `Package.appxmanifest` 错了
- 图标没有打进包
- App Installer 没吃到正确字段

最后确认的真实原因是：

- `Assets\StoreLogo.png`
- `Assets\Square44x44Logo.png`
- `Assets\Square150x150Logo.png`
- `Assets\Wide310x150Logo.png`
- `Assets\SplashScreen.png`

这些文件当时本体就是模板占位图或由占位图缩放生成的图，所以打包本身没坏，坏的是源资源。

此外还补了一个容易遗漏的点：

- 仅有 `StoreLogo.png` 还不够
- 还需要确保 `StoreLogo.scale-*` 被生成并打进 MSIX

当前结论：

1. 安装界面占位图时，先直接打开 `Assets\StoreLogo.png` 看内容。
2. 再检查包内是否真的包含 `StoreLogo.png` 和 `StoreLogo.scale-*`。
3. 如果主图标已经换新，先运行 [generate-msix-assets.ps1](/E:/净云流枫工作室/Vidvix/scripts/generate-msix-assets.ps1) 再重新发布。

### 坑 6：本地测试包签名和证书信任是两回事

`_Test` 目录里的 `.msix` 即使已经构建出来，也不代表本机一定能直接安装。

需要分开理解：

1. 构建成功
2. 测试签名成功
3. 本机信任证书成功
4. 真正安装成功

本次实际情况是：

- 包能打出来
- 但本地直接双击安装会报证书不受信任
- 需要自签名测试证书
- 还需要管理员权限把证书导入 `LocalMachine\TrustedPeople` 和 `LocalMachine\Root`

当前结论：

- 本地侧载测试请统一走 [sign-store-test-package.ps1](/E:/净云流枫工作室/Vidvix/scripts/sign-store-test-package.ps1)
- 普通用户 PowerShell 可用于“签名输出”
- 真正“信任并安装”要用管理员 PowerShell

### 坑 7：根目录旧 `.msix` 容易误导排查

之前 `artifacts\store-submission` 根目录里存在过一个旧的独立 `.msix`，它不是这次官方 single-project MSIX 流程的正确主产物。

这会造成两个问题：

1. 双击错包，看到旧结果，却以为是新构建的行为。
2. 上传错包，把旧链路产物误传到 Partner Center。

当前结论：

- Store 上传只认 `.msixupload`
- 本地安装测试只认 `_Test` 目录里的 `.msix`
- 排查时先看文件时间和路径，确认自己打开的是哪一份

### 坑 8：Demucs 大文件超过 2 GB 会触发 `MakeAppx` 警告

当前包里包含：

`Tools\Demucs\Packages\demucs-runtime-win-x64-gpu-cuda.zip`

这个文件超过 2 GB，会触发 `MakeAppx` warning。它目前不阻塞打包，但会带来这些影响：

- 上传更慢
- 包验证更慢
- 安装更慢
- 后续审核风险更高

当前结论：

- 这不是本次阻塞问题的根因
- 但后续如果继续扩大发版体积，应优先考虑拆分、裁剪或按需交付

## 版本升级时的建议流程

以后每次准备发新版本，建议严格按下面顺序执行：

1. 更新 `Vidvix.csproj` 中的 `Version`、`AssemblyVersion`、`FileVersion`
2. 更新 `Package.appxmanifest` 中的 `Identity Version`
3. 如果主图标变了，执行 `generate-msix-assets.ps1`
4. 执行 `dotnet publish .\Vidvix.csproj -p:PublishProfile=Properties\PublishProfiles\Store-win-x64.pubxml`
5. 用 `sign-store-test-package.ps1 -InstallPackage` 做本地安装验证
6. 本地启动、冒烟测试、检查关键功能
7. 确认无误后上传 `.msixupload` 到 Partner Center

## 继任者不要改坏的几个点

1. 不要恢复旧的手写 `makeappx.exe` 打包脚本作为主链路。
2. 不要把 `WindowsPackageType` 全局改为 `MSIX`。
3. 不要重新启用 `WindowsAppSdkDeploymentManagerInitialize`。
4. 不要只替换 `logo.png` 却忘了重新生成 MSIX 图标资源。
5. 不要把 `_Test` 目录里的 `.msix` 当成 Partner Center 上传包。
6. 不要只改 `csproj` 版本号却忘了同步 `Package.appxmanifest` 版本。
7. 不要看到安装界面空白图标就立刻怀疑 manifest，先看资源图本体。

## 快速命令清单

### 生成整套 MSIX 图标资源

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\generate-msix-assets.ps1
```

### 生成 Store 上传包

```powershell
dotnet publish .\Vidvix.csproj -p:PublishProfile=Properties\PublishProfiles\Store-win-x64.pubxml
```

### 给 `_Test` 包签测试证书

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\sign-store-test-package.ps1 -TestPackageDirectory .\artifacts\store-submission\Vidvix-v<版本>-store-x64_Test
```

### 管理员模式下信任证书并安装测试包

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\sign-store-test-package.ps1 -TestPackageDirectory .\artifacts\store-submission\Vidvix-v<版本>-store-x64_Test -InstallPackage
```

## 这次修复完成后的基线

当前基线是：

- 项目已切回官方 Windows App SDK single-project MSIX 路线
- 本地测试包可以通过临时证书完成安装验证
- Store 图标资源链已经修正
- Store 正确上传文件为 `.msixupload`

后续如果再遇到 Store 问题，排查优先顺序建议是：

1. 先看是不是打开了错误产物
2. 再看版本号是否同步
3. 再看 `Assets` 图标源文件是不是正确
4. 再看 `Package.appxmanifest`
5. 最后才去怀疑构建命令本身
