# 代理池只读统计 API

工作台运行本地代理后，其他程序可用工作台生成并启用的代理 Key 读取账号池状态、账号额度余额、Token 汇总、已缓存的日/周/月统计，以及最近代理请求。API 只读，不会主动访问官方接口刷新数据。

## 地址与认证

地址使用代理面板显示的主机与端口。例如面板的 API 地址是 `http://192.168.1.20:17766/v1`，统计接口地址为 `http://192.168.1.20:17766/v1/workspace/...`。端口变化后以面板当前显示为准。

支持两种认证头：

- `Authorization: Bearer <工作台代理Key>`
- `x-api-key: <工作台代理Key>`

使用工作台“本地代理 → 管理 Key”创建、启用或撤销 Key。禁用或删除后，后续请求会返回 HTTP 401。不要把 Key 写入公开网页、仓库或客户端日志。统计 Key 与模型调用 Key 使用同一组代理 Key。

## 接口列表

| 方法 | 路径 | 用途 |
| --- | --- | --- |
| GET | `/v1/workspace` 或 `/v1/workspace/status` | 代理运行状态、账号池数量、请求总数 |
| GET | `/v1/workspace/accounts` | 每个登记账号的状态、订阅、额度、剩余量、Token 与计费快照 |
| GET | `/v1/workspace/usage` | 省略 `account_id` 时获取所有账号 Token 汇总；指定后获取已缓存的时间序列 |
| GET | `/v1/workspace/requests?limit=40` | 最近最多 40 条代理请求脱敏记录 |
| GET | `/v1/workspace/openapi.json` | 获取 OpenAPI 3.0 JSON，可导入 API 调试与集成工具 |

统计路径和模型 API 共用代理 Key、局域网监听范围及端口。它们只允许 GET；没有单独的 Key 权限隔离，因此不要把同一个 Key 发给不可信的第三方。接口不接受浏览器跨域请求。

## 示例

```bash
curl -H "Authorization: Bearer $WORKSPACE_PROXY_KEY" "http://192.168.1.20:17766/v1/workspace/status"
```

```bash
curl -H "x-api-key: $WORKSPACE_PROXY_KEY" "http://192.168.1.20:17766/v1/workspace/accounts?pool_only=true"
```

读取某账号最近一周日统计（`page=0` 是最近一页；历史只读本机缓存）：

```bash
curl -H "Authorization: Bearer $WORKSPACE_PROXY_KEY" "http://192.168.1.20:17766/v1/workspace/usage?account_id=账号ID&grain=day&page=0"
```

将 OpenAPI 文档导入程序：

```bash
curl -H "Authorization: Bearer $WORKSPACE_PROXY_KEY" "http://192.168.1.20:17766/v1/workspace/openapi.json" -o proxy-usage.openapi.json
```

## 数据字段和口径

- `availability`：`ready`、`invalid`、`unknown`、`unsupported`、`exhausted`、`low_quota`、`paused` 或 `not_in_pool`。它描述代理是否会把新请求路由到该账号；凭据有效不代表额度够用。
- `balance.remaining`：官方账号数据中的可用余额。Command Code 为工作台读取的官方额度余额（credits）；OpenCode 按服务商返回值提供，`unit` 为 `provider_reported`。OpenCode Go 通常通过百分比额度窗口返回剩余比例，不一定提供货币余额。
- `quotas.monthly/five_hour/weekly`：`used`、`limit`、`remaining`、百分比和 `reset_at`。官方未提供或尚未获取的值为 `null`，不会猜测补齐。`unit` 为 `credits` 或 `percent`。
- `tokens`：总量、输入、输出及本计费周期 Token。`scope=account` 表示 Command Code 账号级统计；`scope=provider` 表示 OpenCode 本机数据按服务商汇总，不能解释成该登录账号独占用量。
- `usage` 时间序列：Command Code 读取工作台已保存的历史缓存，不触发刷新；OpenCode 从其本机数据库读取服务商级记录。返回 `complete`、`has_cache`、`updated_at` 告知数据完整度和时间。
- `requests`：最多 40 条、仅内存保存的代理请求元数据，包含账号别名、模型、路径、HTTP 状态、耗时和可解析的输入/输出 Token。不会保存提示词或请求正文；程序退出后记录清空。总请求计数也只统计本次运行。
- 所有时间戳为 ISO 8601。额度及 Token 字段可能为 `null`，调用方应按未知值处理。

## 错误码

| HTTP | 情况 |
| --- | --- |
| 400 | 查询参数错误；例如不支持的 `grain`、无效分页 |
| 401 | Key 缺失、错误、已禁用或已删除 |
| 403 | 请求地址与监听网卡不匹配，或请求来自网页 Origin |
| 404 | 未知接口或账号 ID |
| 503 | 本地历史暂不可读或工作台统计接口尚未就绪 |

完整 schema 可从正在运行的代理获取，也可直接查看仓库中的 [OpenAPI 文件](proxy-usage.openapi.json)。
