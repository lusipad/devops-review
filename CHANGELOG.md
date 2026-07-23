# Changelog

## 0.2.0 — 2026-07-24

- 可直接从 GitHub Release 下载按用户安装的单文件 Windows EXE，无需预装 .NET Runtime；
- 安装后通过图形配置工具粘贴 PR/仓库地址并选择本地目录，无需手工编辑 JSON；
- 使用当前 Windows 会话协商 Kerberos/NTLM，并在保存前验证完整 Git origin、Azure DevOps 和 Codex 登录；
- GitHub Actions 在 `v*` 标签通过测试后自动发布 EXE、ZIP 及其 SHA-256 校验文件；
- 保留现有配置、会话和 worktree，升级或默认卸载不会删除用户数据。

## 0.1.0 — 2026-07-20

首个 Windows 本地单用户版本：

- 在 Azure DevOps Server PR 右侧/source Monaco 编辑器中划选代码并向 Codex 提问；
- 通过 Chrome/Edge Native Messaging 连接 self-contained .NET 8 Bridge；
- 由 Azure DevOps REST 校验 iteration 和 source/target SHA；
- 为每个 `repositoryId + pullRequestId + sourceCommit` 创建 detached worktree 并续接 Codex thread；
- Codex App Server 固定为 UTF-8、只读 sandbox 和 `approvalPolicy=never`；
- 只把 `final_answer` 作为答案，净化本地绝对路径后才允许显式发布 PR 评论；
- 支持 Windows 集成认证和环境变量 PAT；
- 提供图形配置工具，通过 PR 地址和目录选择自动生成单仓库配置并验证 Git、NTLM/Kerberos 与 Codex；
- 提供 GitHub Actions 生成的单文件 EXE 安装器、ZIP 发布包、卸载器、完整配置文档和 SHA-256 清单。
