## 本次更新

- 根治 DeepSeek「Invalid input at thinking」：opencode 等 Anthropic 协议客户端经代理调用 DeepSeek 时，代理现在把 `/messages` 请求**原样直连 DeepSeek 官方 Anthropic 端点**（`api.deepseek.com/anthropic`），thinking 块与工具调用历史由 DeepSeek 原生处理，不再做有损的 OpenAI 协议转换，思考能力完整保留。
- 配置客户端时，DeepSeek 模型优先使用 Anthropic 协议，配合上面的直连生效。
- 上游拒绝时不再只给一句笼统提示，而是带上游返回的具体错误原因（同时写入代理日志），方便定位。
- 代理安全阈值可手动设置：设置里新增「代理安全阈值（%）」，默认 3%——账号额度剩余低于该比例才停止供代理使用，0–20% 可调。
- 上游改为直连：代理到 CommandCode 的请求不再经过本机系统代理（127.0.0.1:10090），避免本地 VPN / 代理导致的连接重置、502 和额外延迟。
- 上下文按官方模型清单写入：本地代理的 /v1/models 现在带上官方 `context_length`，配置客户端时按每个模型的真实上限写入。改完在代理面板重新点一次「配置客户端」生效。
- 单账号不再被误冷却：只有账号池里还有别的账号时才冷却 15 秒换号，避免唯一账号被冷却后整段时间不可用。

![账号列表与用量](https://github.com/xiaoer2021/commandcode-manager/releases/download/v1.6.21/overview.png)
