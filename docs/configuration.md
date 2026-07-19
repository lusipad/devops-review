# 配置说明

Bridge 默认读取：

```text
%LOCALAPPDATA%\DevOpsReview\config.json
```

安装脚本只在文件不存在时复制示例，不会覆盖已有配置。修改后关闭所有扩展侧栏再重新打开，使 Native Host 重启并重新读取配置。

## 完整示例

```json
{
  "dataDirectory": "C:\\Users\\alice\\AppData\\Local\\DevOpsReview",
  "worktreeRoot": "D:\\DevOpsReviewWorktrees",
  "codexExecutable": "C:\\Users\\alice\\AppData\\Roaming\\npm\\codex.cmd",
  "allowedExtensionOrigins": [
    "chrome-extension://kldpfliioeaahafemncagclpehbnblig/"
  ],
  "repositories": [
    {
      "serverUrl": "http://devops.company.internal:8080/tfs",
      "collection": "DefaultCollection",
      "project": "Orders",
      "repository": "Orders.Api",
      "localPath": "D:\\Source\\Orders.Api",
      "authMode": "Windows",
      "apiVersion": "7.0"
    }
  ]
}
```

## 顶层字段

| 字段 | 必填 | 说明 |
| --- | --- | --- |
| `dataDirectory` | 是 | 绝对路径。保存 SQLite 会话元数据；不保存源码、问题、回答或凭据。 |
| `worktreeRoot` | 是 | 绝对路径。每个 PR source SHA 的 detached worktree 根目录。不得与任何 `localPath` 相同、互为父目录或子目录。 |
| `codexExecutable` | 是 | `codex` 或绝对可执行路径。浏览器启动的进程找不到 PATH 时应改为 `Get-Command codex` 返回的路径。 |
| `allowedExtensionOrigins` | 是 | 精确扩展 origin。官方包固定为 `chrome-extension://kldpfliioeaahafemncagclpehbnblig/`，不要使用通配符。 |
| `repositories` | 是 | 至少一个仓库映射；可以配置多个。 |

## 仓库映射

PR 地址如果是：

```text
http://host:8080/tfs/DefaultCollection/Orders/_git/Orders.Api/pullrequest/42
```

对应配置为：

- `serverUrl`: `http://host:8080/tfs`，即 Collection 之前的根地址；不能包含用户名、密码、查询参数或 fragment。
- `collection`: `DefaultCollection`
- `project`: `Orders`
- `repository`: `Orders.Api`
- `localPath`: 已 clone 仓库的根目录，不是 worktree 或其父目录。
- `apiVersion`: Azure DevOps Server 2022 默认使用 `7.0`。

Bridge 会从 Azure DevOps REST 重新取得 PR iteration、source SHA 和 target SHA；浏览器不能指定这些权威值。本地仓库必须能从 `origin` fetch 到这些 commit。部署前应人工确认 `git remote get-url origin` 对应同一个 Azure DevOps 仓库。

## 身份认证

### Windows 集成认证（推荐）

```json
"authMode": "Windows"
```

Bridge 和 Git 使用当前 Windows 用户。先在普通 PowerShell 中验证：

```powershell
git -c http.emptyAuth=true -C D:\Source\Orders.Api fetch origin
```

### PAT

PAT 不能写入 JSON。配置只保存环境变量名：

```json
{
  "authMode": "Pat",
  "patEnvironmentVariable": "DEVOPS_REVIEW_ORDERS_PAT"
}
```

通过 Windows“编辑账户的环境变量”创建 `DEVOPS_REVIEW_ORDERS_PAT`，然后完全退出并重新启动浏览器。不要把 PAT 放进命令行历史、Git 仓库、日志或截图。

## 多仓库

在 `repositories` 中增加对象即可。`serverUrl + collection + project + repository` 组合必须唯一：

```json
"repositories": [
  {
    "serverUrl": "https://ado-a.example/tfs",
    "collection": "DefaultCollection",
    "project": "Orders",
    "repository": "Orders.Api",
    "localPath": "D:\\Source\\Orders.Api",
    "authMode": "Windows",
    "apiVersion": "7.0"
  },
  {
    "serverUrl": "https://ado-b.example/tfs",
    "collection": "Engineering",
    "project": "Payments",
    "repository": "Gateway",
    "localPath": "D:\\Source\\Gateway",
    "authMode": "Windows",
    "apiVersion": "7.0"
  }
]
```

扩展设置页也要分别授权每个 Azure DevOps Server origin。当前 UI 保存一个服务器地址；多 Server 场景可重复进入设置页切换，或后续扩展为地址列表。

## 常见问题

- **侧栏显示无法连接 Bridge**：确认运行过安装脚本、扩展 ID 正确，并检查 Chrome/Edge NativeMessagingHosts 注册表值。
- **`repository_not_allowed`**：页面 URL 的 server/collection/project/repository 与配置不匹配。
- **`local_repository_missing`**：`localPath` 不存在或浏览器启动的当前用户无访问权限。
- **`git_failed`**：在同一 Windows 用户下手工运行 fetch，检查 `origin`、代理、证书和集成认证。
- **Codex 启动失败**：运行 `codex login status`，并把 `codexExecutable` 改为绝对路径。
- **修改配置未生效**：关闭所有扩展侧栏，使 Native Host 退出，再重新打开。
