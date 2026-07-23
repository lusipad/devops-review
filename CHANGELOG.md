# Changelog

## 0.1.0 — 2026-07-20

首个 Windows 本地单用户版本：

- 在 Azure DevOps Server PR 右侧/source Monaco 编辑器中划选代码并向 Codex 提问；
- 通过 Chrome/Edge Native Messaging 连接 self-contained .NET 8 Bridge；
- 由 Azure DevOps REST 校验 iteration 和 source/target SHA；
- 为每个 `repositoryId + pullRequestId + sourceCommit` 创建 detached worktree 并续接 Codex thread；
- Codex App Server 固定为 UTF-8、只读 sandbox 和 `approvalPolicy=never`；
- 只把 `final_answer` 作为答案，净化本地绝对路径后才允许显式发布 PR 评论；
- 支持 Windows 集成认证和环境变量 PAT；
- 提供 GitHub Actions 生成的单文件 EXE 安装器、ZIP 发布包、卸载器、完整配置文档和 SHA-256 清单。
