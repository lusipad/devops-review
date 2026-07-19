# Azure DevOps Codex Review

面向 Azure DevOps Server 的 Windows 本地单用户代码评审助手。它让你在 Pull Request 文件页面选择代码、点击“问 Codex”，然后由 Codex 在与 PR source commit 完全一致的本地完整仓库中分析并流式回答。

本项目使用已有的 ChatGPT/Codex 登录，不调用 OpenAI Platform API，也不需要 API Key。

## 工作方式

```text
Azure DevOps Server PR 页面
  -> Chrome / Edge MV3 扩展（选区、文件、行号、问题）
  -> Native Messaging
  -> .NET 8 Review Bridge
       -> Azure DevOps REST：校验 PR、iteration、source/target commit
       -> Git：创建 detached PR worktree
       -> SQLite：保存 session key、worktree、Codex thread ID
       -> Codex App Server：read-only + approval never + stdio JSONL
  -> 浏览器侧栏流式回答
  -> 用户显式点击后发布为 PR 评论
```

网页数据只用于定位。source/target commit 来自 Azure DevOps；代码事实来自本地 worktree。浏览器不能指定命令、工作目录、Codex 权限或最终发布内容。

Codex App Server 是官方面向自定义富客户端的接口，支持认证、持续线程和流式 agent 事件；本项目使用默认 stdio transport。当前 CLI 仍将 `app-server` 标记为 experimental，因此每次升级 Codex 后都应重新生成协议 schema 并运行测试。[Codex App Server 文档](https://learn.chatgpt.com/docs/app-server.md)

## 当前实现

- Azure DevOps Server 2022 REST API 7.0；每个仓库可以单独配置 API 版本。
- Windows 集成认证，或从指定环境变量读取 PAT。
- 一个 `repositoryId + pullRequestId + sourceCommit` 对应一个 worktree 和 Codex thread。
- 同一 PR/commit 连续追问复用 thread；source commit 变化后自动隔离。
- Native Messaging 固定扩展 ID：`kldpfliioeaahafemncagclpehbnblig`。
- 扩展只申请用户在设置页明确授权的 Azure DevOps Server origin。
- 默认只读，不提供修改代码、执行审批或自动发布评论的能力。
- 发布评论必须由用户点击；Bridge 使用自身保留的完整回答，浏览器不能替换正文。
- App Server 的 commentary 只更新进度，只有 `final_answer` 进入答案和待发布评论。
- 完成时会把 Codex 生成的本机 worktree 绝对路径改写为仓库相对 `path:line`，避免泄露本地用户名和目录。

Native Messaging 使用四字节本机字节序长度前缀和 UTF-8 JSON，Bridge 的 stdout 只写协议帧，日志只写 stderr。Chrome/Edge 要求 manifest 中列出准确的 `allowed_origins`，不允许通配符。[Chrome Native Messaging 文档](https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging)、[Edge Native Messaging 文档](https://learn.microsoft.com/zh-cn/microsoft-edge/extensions/developer-guide/native-messaging)

## 前置条件

- Windows 10/11
- Git
- Chrome 或 Edge
- `codex` CLI，并已执行 `codex login`
- 本地可访问的 Azure DevOps Server 仓库 clone
- 开发构建需要 .NET SDK 10.0.200；安装后的 Bridge 是自包含 `win-x64` 程序

确认 Codex 登录：

```powershell
codex login status
```

## 配置

复制 [config.example.json](config.example.json) 到：

```text
%LOCALAPPDATA%\DevOpsReview\config.json
```

至少修改：

- `dataDirectory`
- `worktreeRoot`
- `serverUrl`：Collection 之前的 Azure DevOps Server 根地址
- `collection`、`project`、`repository`
- `localPath`：已经 clone 的 Git 仓库根目录

`worktreeRoot` 不能与源仓库目录重叠。生产环境优先使用 Windows 集成认证。使用 PAT 时不要把 PAT 写进配置：设置 `authMode: "Pat"` 和 `patEnvironmentVariable`，再通过用户环境变量提供凭据。

所有字段、多仓库、Windows/PAT 认证和排障说明见 [配置说明](docs/configuration.md)。

## 构建与安装

```powershell
dotnet restore DevOpsReview.slnx
dotnet test DevOpsReview.slnx --no-restore
cd extension
npm test
cd ..
pwsh -File .\scripts\install.ps1 -Browser Both
```

安装脚本会：

1. 发布自包含 `win-x64` Bridge；
2. 写入精确 `allowed_origins` 的 Native Messaging manifest；
3. 在当前用户的 Chrome/Edge NativeMessagingHosts 注册表路径注册；
4. 首次安装时复制配置示例，但不会覆盖已有配置。

随后打开 `chrome://extensions` 或 `edge://extensions`：

1. 打开开发者模式；
2. 选择“加载已解压的扩展程序”；
3. 选择本仓库的 `extension` 目录；
4. 确认扩展 ID 是 `kldpfliioeaahafemncagclpehbnblig`；
5. 在扩展设置页授权 Azure DevOps Server 根地址。

## 生成完整 Windows 发布包

```powershell
pwsh -File .\scripts\package-release.ps1
```

输出为 `artifacts\devops-review-0.1.0-win-x64.zip` 和对应的 `.zip.sha256`，包含 self-contained Bridge、unpacked 扩展、安装/卸载脚本、配置示例、文档和 SHA-256 校验文件。目标机操作见 [发布包说明](docs/release-package.md)。

## 使用

1. 打开 Azure DevOps Server PR 的 Files 页面。
2. 选择一段右侧/source 代码。
3. 点击选区旁的“问 Codex”。
4. 在侧栏输入问题并开始分析。
5. 需要时点击“发布到 PR”；Bridge 会尽量发布为行内评论，无法安全定位 iteration change 时退化为普通 PR 评论。

## 验证

```powershell
# .NET 单元和集成测试
dotnet test DevOpsReview.slnx

# 扩展解析和权限边界测试
cd extension
npm test

# 使用当前 ChatGPT/Codex 登录做真实 App Server 协议冒烟
cd ..
dotnet run --project tools\DevOpsReview.Smoke -- .

# 生成扩展交付包
pwsh -File .\scripts\package-extension.ps1
```

当前机器已完成两层真实验证：App Server 协议烟测返回 `READY`；隔离 Edge 配置在真实 Azure DevOps Server PR #4 上成功捕获 Monaco 选区 `/src/tax.js:4-5`、连接已安装 Native Host、流式显示最终答案并在完成后启用发布按钮。测试没有点击发布。首次协议烟测首个回答 delta 约 8.35 秒、总耗时约 8.46 秒；这个值只作为环境基线，不是产品 SLA。

## 安全边界

- App Server 仅作为 Bridge 的 stdio 子进程，不开放 WebSocket 或公网端口。
- `sandbox = read-only`，`approvalPolicy = never`。
- 文件路径必须是仓库相对路径；拒绝绝对路径、反斜杠和 `..`。
- Git 通过 argv 启动，不经过 shell，且设置 `GIT_TERMINAL_PROMPT=0`。
- Git 子进程 stdin 始终关闭，不能继承浏览器长期打开的 Native Messaging 输入管道。
- Windows 集成认证的 Git 请求启用 `http.emptyAuth=true`，允许服务器发起 Negotiate/NTLM challenge；不会把凭据写入命令行。
- worktree 路径只从服务端 repository ID、PR ID 和 40 位 commit SHA 派生。
- 扩展 origin 必须精确匹配配置和 Native Messaging manifest。
- 不在 SQLite 中保存源文件、选中文本、问题、回答或 Codex 凭据。
- Bridge 与 Codex App Server 的 stdin/stdout/stderr 显式使用 UTF-8，不依赖 Windows 当前代码页。
- 仓库内容和网页内容都按不可信数据处理。

详细决策、验收标准和协议见 [实施计划](docs/implementation-plan.md)、[Bridge 协议](docs/protocol.md) 与 [理解报告](docs/understanding-report.md)。

## 已知边界

- 当前选区解析支持 Azure DevOps PR 的右侧/source 文件视图；删除行/target 侧需要后续加入显式 side/iteration 定位。
- 当前实现已适配 Azure DevOps Server 2022 的 Monaco 隐藏 textarea 选区模型；页面 DOM 仍可能随 Server 补丁变化，其他目标 Server 必须执行浏览器兼容测试。
- App Server 是 experimental surface；Codex 升级需要重新验证生成 schema。
- App Service、团队共享身份、向量数据库和自动全量 PR 审查不在本地单用户版本范围内。
