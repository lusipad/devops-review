# Windows 发布包使用说明

发布包名称：

```text
devops-review-0.1.0-win-x64.zip
```

它包含 self-contained Bridge、可加载的 unpacked 扩展、安装/卸载脚本、配置示例、文档和包内 SHA-256 校验文件。同目录的 `.zip.sha256` 文件用于下载后校验整个 ZIP。目标机器不需要安装 .NET Runtime，但仍需要 Git、Edge/Chrome 和已登录的 Codex CLI。

## 安装

1. 解压 ZIP，保留目录结构。
2. 在解压目录打开 PowerShell：

   ```powershell
   pwsh -File .\scripts\install-package.ps1 -Browser Both
   ```

3. 编辑 `%LOCALAPPDATA%\DevOpsReview\config.json`，完整字段见 [配置说明](configuration.md)。
4. 打开 `edge://extensions` 或 `chrome://extensions`，启用开发人员模式。
5. 点击“加载解压缩的扩展”，选择发布包中的 `extension` 目录。
6. 确认扩展 ID 为 `kldpfliioeaahafemncagclpehbnblig`。
7. 打开扩展设置页，填写 Collection 之前的 Azure DevOps Server 根地址并授权。
8. 重新加载 PR Files 页面，选择右侧/source 代码并点击“问 Codex”。

## 校验发布包

解压前校验整个 ZIP：

```powershell
$expected = (Get-Content .\devops-review-0.1.0-win-x64.zip.sha256).Split(' ')[0]
$actual = (Get-FileHash .\devops-review-0.1.0-win-x64.zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $expected) { throw '发布包 SHA-256 不匹配' }
```

在发布包根目录运行：

```powershell
Get-Content .\SHA256SUMS.txt
Get-FileHash .\bridge\DevOpsReview.Bridge.exe -Algorithm SHA256
```

输出 hash 应与 `SHA256SUMS.txt` 对应条目相同。

## 升级

关闭所有扩展侧栏后重新运行新包的 `install-package.ps1`。安装器会替换程序文件和 Native Messaging manifest，但不会覆盖现有 `config.json`、SQLite 会话或 worktree。

## 卸载

默认保留配置、会话和 worktree：

```powershell
pwsh -File .\scripts\uninstall.ps1
```

连同本地数据删除：

```powershell
pwsh -File .\scripts\uninstall.ps1 -RemoveData
```

`-RemoveData` 会删除 `%LOCALAPPDATA%\DevOpsReview`，不可通过卸载脚本恢复。
