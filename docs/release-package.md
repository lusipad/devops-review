# Windows 发布包使用说明

推荐下载单文件安装器：

```text
DevOpsReview-Setup-0.2.1.exe
```

需要便携式文件或手工部署时使用 ZIP：

```text
devops-review-0.2.1-win-x64.zip
```

两种方式都包含 self-contained Bridge、可加载的 unpacked 扩展、配置示例和文档。同目录的 `.sha256` 文件用于下载后校验。目标机器不需要安装 .NET Runtime，但仍需要 Git、Edge/Chrome 和已登录的 Codex CLI。

## 使用安装器（推荐）

1. 下载 `DevOpsReview-Setup-0.2.1.exe` 和对应的 `.exe.sha256`。
2. 校验安装器：

   ```powershell
   $expected = (Get-Content .\DevOpsReview-Setup-0.2.1.exe.sha256).Split(' ')[0]
   $actual = (Get-FileHash .\DevOpsReview-Setup-0.2.1.exe -Algorithm SHA256).Hash.ToLowerInvariant()
   if ($actual -ne $expected) { throw '安装器 SHA-256 不匹配' }
   ```

3. 双击安装器。它按当前用户安装，不需要管理员权限，并为所选浏览器注册 Native Messaging Host。
4. 安装结束后，在自动打开的配置工具中粘贴一个 Azure DevOps PR/仓库地址并选择本地 clone 目录。
5. 点击“测试连接”；工具会验证 Git origin、当前 Windows 身份、Azure DevOps 和 Codex 登录。
6. 点击“保存并完成”，再点击“打开扩展目录”。
7. 打开 `edge://extensions` 或 `chrome://extensions`，启用开发人员模式。
8. 点击“加载解压缩的扩展”，选择 `%LOCALAPPDATA%\Programs\DevOpsReview\app\extension`。
9. 确认扩展 ID 为 `kldpfliioeaahafemncagclpehbnblig`。
10. 打开扩展设置页，填写 Collection 之前的 Azure DevOps Server 根地址并授权。
11. 重新加载 PR Files 页面，选择右侧/source 代码并点击“问 Codex”。

侧栏会显示扩展、PR 页面/选区、本地 Bridge 和配置的独立状态。若 Native Messaging Host 未注册或 Bridge 断开，可在侧栏看到具体错误并点击“重新检测”；“扩展设置”可用于重新授权 Azure DevOps 地址。

浏览器安全模型不允许普通安装器静默加载 unpacked 扩展，因此第 7—9 步只需人工完成一次。

## 使用 ZIP

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
$expected = (Get-Content .\devops-review-0.2.1-win-x64.zip.sha256).Split(' ')[0]
$actual = (Get-FileHash .\devops-review-0.2.1-win-x64.zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $expected) { throw '发布包 SHA-256 不匹配' }
```

在发布包根目录运行：

```powershell
Get-Content .\SHA256SUMS.txt
Get-FileHash .\bridge\DevOpsReview.Bridge.exe -Algorithm SHA256
```

输出 hash 应与 `SHA256SUMS.txt` 对应条目相同。

## 升级

关闭所有扩展侧栏后运行新版 EXE 安装器，或重新运行新 ZIP 中的 `install-package.ps1`。两种方式都会替换程序文件和 Native Messaging manifest，但不会覆盖现有 `config.json`、SQLite 会话或 worktree。

## 卸载

EXE 安装版可在 Windows“已安装的应用”中卸载。默认保留 `%LOCALAPPDATA%\DevOpsReview` 下的配置、会话和 worktree。

ZIP 安装版可运行：

```powershell
pwsh -File .\scripts\uninstall.ps1
```

连同本地数据删除：

```powershell
pwsh -File .\scripts\uninstall.ps1 -RemoveData
```

`-RemoveData` 会删除 `%LOCALAPPDATA%\DevOpsReview`，不可通过卸载脚本恢复。
