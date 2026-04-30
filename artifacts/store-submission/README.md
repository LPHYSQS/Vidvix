# Store Submission 目录说明

这个目录用于承接 Microsoft Store 提交相关产物。

发布命令：

```powershell
dotnet publish .\Vidvix.csproj -p:PublishProfile=Properties\PublishProfiles\Store-win-x64.pubxml
```

如果需要走脚本化打包流程，也可以使用：

```powershell
.\scripts\package-store-msix.ps1
```

发布后通常会在这里生成：

- `Vidvix-v<版本>-store-x64.msixupload`
- `Vidvix-v<版本>-store-x64_Test\`

说明：

1. 上传到 Microsoft Partner Center 时，使用本目录下最新生成的 `.msixupload`。
2. 本地安装验证时，使用 `_Test` 目录里的 `.msix`。
3. 如需给 `_Test` 目录中的测试包补签名或安装，可执行 `.\scripts\sign-store-test-package.ps1`。
4. 发版结束后，版本相关大文件和 `_Test` 目录可以清理，不必长期保留。
5. 本目录中的 `老师傅的留言.txt` 是人工参考资料，请保留。
