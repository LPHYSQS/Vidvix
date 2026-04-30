# Split Audio Offline Smoke 目录说明

这个目录用于保存离线音频分离冒烟测试的临时输出。

对应测试项目：

`tests\SplitAudioOfflineSmoke\SplitAudioOfflineSmoke.csproj`

常用执行命令：

```powershell
.\scripts\test-split-audio-offline.ps1
```

说明：

1. 默认输出会落在本目录下的 `local\` 子目录，按配置和目标框架继续分层。
2. 这里的内容主要是测试构建产物、解压后的运行时副本和一次性样本输出，体积很大但都可再生。
3. 冒烟测试完成后，可以清空这些内容，只保留目录和说明文件。
4. 下次重新执行脚本或重新构建测试项目时，输出会自动重新生成。
