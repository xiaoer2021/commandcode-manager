## 本次更新

- 修复 OpenCode / Anthropic 协议（/v1/messages）经代理调用 DeepSeek 时的 HTTP 400：DeepSeek V4 思考模式不接受 `tool_choice`，现已自动剥离；并把 Anthropic 的 thinking 块映射为 DeepSeek 的 `reasoning_content`，避免工具调用历史被拒。
- 上游拒绝时不再只给一句笼统提示，而是带上游返回的具体错误原因（同时写入代理日志），方便定位。
- 代理安全阈值可手动设置：设置里新增「代理安全阈值（%）」，默认 3%——账号额度剩余低于该比例才停止供代理使用，0–20% 可调。
- 上游改为直连：代理到 CommandCode 的请求不再经过本机系统代理（127.0.0.1:10090），避免本地 VPN / 代理导致的连接重置、502 和额外延迟。
- 上下文按官方模型清单写入：本地代理的 /v1/models 现在带上官方 `context_length`，配置客户端时按每个模型的真实上限写入（DeepSeek V4.1 Flash = 1,000,000；200k 的模型不会再被写成 1M）。改完在代理面板重新点一次「配置客户端」生效。
- 单账号不再被误冷却：只有账号池里还有别的账号时才冷却 15 秒换号，避免唯一账号被冷却后整段时间不可用。

![账号列表与用量](https://github.com/xiaoer2021/commandcode-manager/releases/download/v1.6.18/overview.png)
