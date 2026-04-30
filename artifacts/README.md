# Artifacts 目录说明

`artifacts` 只用于保存构建、验证、冒烟测试、打包和提交 Microsoft Store 时产生的临时产物。

本目录不应长期保留大体积发版文件。每次发布完成后，可以清理版本相关的 `.msix`、`.msixupload`、测试签名副本、本地发布镜像、日志和运行验证缓存，只保留下列目录结构与说明文件：

- `logs`
- `split-audio-offline-smoke`
- `store-build`
- `store-runtime-path-validation`
- `store-submission`

保留原则：

1. 保留目录骨架，便于下次构建、验证和发版时继续复用。
2. 保留人工参考资料和 README，例如 `store-submission/老师傅的留言.txt`。
3. 删除本次发布生成的超大包、解压后的运行时副本和一次性缓存。
4. `Tools\` 根目录中的源依赖不要清理；只清理 `artifacts`、`bin`、`obj` 里复制出来的构建产物。
5. 后续重新运行脚本或发布命令时，这些目录会自动重新填充。
