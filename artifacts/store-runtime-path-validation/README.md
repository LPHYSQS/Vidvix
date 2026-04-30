# Store Runtime Path Validation 目录说明

这个目录用于保存 Store 运行时路径验证相关的本地产物。

对应验证项目：

`tests\StoreRuntimePathValidation\StoreRuntimePathValidation.csproj`

验证命令：

```powershell
dotnet run --project .\tests\StoreRuntimePathValidation\StoreRuntimePathValidation.csproj -c Release
```

这里的输出属于临时验证缓存，可以在验证完成后清理，只保留目录和说明文件。下次重新执行验证命令后，`local\Debug\`、`local\Release\` 等输出会自动重新生成。
