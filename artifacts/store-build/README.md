# Store Build 目录说明

这个目录用于保存 Store 发布时的本地镜像输出。

当前发布配置会把镜像内容输出到：

`artifacts\store-build\publish\win-x64\`

对应发布配置：

`Properties\PublishProfiles\Store-win-x64.pubxml`

常用发布命令：

```powershell
dotnet publish .\Vidvix.csproj -p:PublishProfile=Properties\PublishProfiles\Store-win-x64.pubxml
```

说明：

1. 这里保存的是本地发布镜像，用于打包和验证。
2. 真正要上传到 Microsoft Partner Center 的 `.msixupload` 会生成到 `artifacts\store-submission\`。
3. 这个目录可以在每次发布后清空，只保留目录结构即可。
4. 重新执行上面的发布命令后，内容会自动重新生成。
