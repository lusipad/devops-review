# Azure DevOps Codex Review 理解报告

## 为什么做

目标是在 Azure DevOps Server PR 页面直接选择代码并向 Codex 提问，同时让回答建立在与 PR 精确版本一致的完整本地仓库上。浏览器只负责交互，不持有仓库权限、Codex 权限或可发布的评论正文。

## 心智模型

把系统看成一道有两次校验的单向闸门：

```text
浏览器选区（不可信定位）
  -> Bridge 用 ADO REST 重新确认 PR/SHA
  -> Git 固定 detached worktree
  -> Codex 只读分析
  -> Bridge 净化并保留最终答案
  -> 用户显式点击后才写回 PR
```

最重要的不变量是：浏览器不能决定 commit、工作目录、Codex sandbox 或发布正文。

## 改了什么

1. MV3 扩展识别配置过的 Azure DevOps Server PR URL，从 Monaco 隐藏 textarea 的选区偏移计算真实行号，并把问题放入侧栏。
2. Native Messaging Bridge 校验精确 extension origin、仓库映射、路径和行号，再通过 ADO REST 获取当前 iteration 与 source/target SHA。
3. Bridge 从配置的本地 clone fetch，创建按 `repositoryId:PR:sourceSha` 隔离的 detached worktree，并用 SQLite 保存可续接的 Codex thread ID。
4. Codex App Server 固定使用 UTF-8 stdio、`read-only` sandbox 和 `approvalPolicy=never`。commentary 只作进度，`final_answer` 才进入答案。
5. Bridge 在完成时移除本机 worktree 绝对路径。浏览器只能引用 Bridge 内存中的完成结果发出 publish 请求，不能替换评论正文。
6. 安装脚本发布 self-contained `win-x64` Bridge，并在当前用户的 Chrome/Edge NativeMessagingHosts 注册表中注册精确 manifest。

## 它依赖什么

- Azure DevOps Server REST 返回的 iteration、source commit 和 target commit 是版本事实。
- 本地 clone 的 `origin` 必须对应配置的 Azure DevOps 仓库；Bridge 会 fetch 并验证 commit 对象存在。
- Chrome/Edge 用扩展 ID 和 Native Messaging manifest 的 `allowed_origins` 做第一层调用方校验，Bridge 再做一次精确 origin 校验。
- Windows 维护的 `winsqlite3` 提供 SQLite 本机实现；项目不再携带命中 CVE-2025-6965 的 `e_sqlite3` 库。
- Codex CLI 必须已登录，并继续支持当前 experimental App Server schema。

## 最可能坏在哪里

- Azure DevOps 补丁改变 Monaco 或 PR URL/DOM；应重跑隔离浏览器选区测试。
- Codex CLI 升级改变 App Server schema或 message phase；应重新生成 schema并跑 fake + real smoke。
- PR source SHA 更新；旧 thread/worktree 不能复用，必须产生新的 session key。
- 选择 target/deleted 侧代码；当前版本明确只支持右侧/source 视图。
- 本地 clone 指向错误 origin。当前实现验证仓库根和服务端 commit，但尚未把 remote URL 与映射 URL 做等价性证明，部署时仍需人工确认配置。

## 理解门禁

请用一行回答，例如：`1B 2C 3A 4B 5C 6: A->B->C`。第 6 题是关键词短答，其余为单选。

1. 浏览器提交了一个假的 source SHA。最终使用哪个 SHA？
   - A. 浏览器 SHA，因为最接近用户操作
   - B. Azure DevOps 当前 PR iteration 返回的 SHA
   - C. 本地 clone 当前分支 HEAD

2. 一次新 source commit 的请求按什么主路径执行？
   - A. 直接复用旧 thread -> 让 Codex 自己 fetch -> 自动发布
   - B. 浏览器打开 localhost HTTP -> Bridge 修改主工作区 -> 返回答案
   - C. ADO 校验 -> fetch/固定 worktree -> 新 session/thread -> 只读 turn

3. 哪项共同阻止任意网页调用 Bridge？
   - A. manifest 的精确 `allowed_origins` 加 Bridge 的 origin 校验
   - B. 只依赖扩展页面隐藏按钮
   - C. SQLite session key

4. Codex 输出 commentary 和 final answer 时，什么内容能进入待发布评论？
   - A. 两者按到达顺序全部拼接
   - B. 只有 final answer，且完成时先净化本地路径
   - C. 浏览器重新提交的 textarea 内容

5. 为什么不用 localhost Web API 作为第一版 transport？
   - A. Native Messaging 更快，但安全没有差别
   - B. Azure DevOps 不支持 HTTP
   - C. Native Messaging 避免开放 TCP 服务，并由浏览器原生绑定扩展身份

6. 短答：从网页选区到“允许发布”至少写出三个按顺序发生的安全/状态门禁。
