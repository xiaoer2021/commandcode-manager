## 本次更新

- 修复 Command Code 账号经代理调用时出现的「Invalid discriminator value. Expected 'user' | 'assistant' at messages.N.role」（HTTP 400）：代理转发前自动规范化 messages——`role=system/developer` 的消息并入顶层 `system`；`role=tool` 的消息合并成一条 `user` 消息里的多个 `tool_result` 块；其它非法 role 兜底为 `user`。
- 修复 Command Code 账号的「Invalid input at thinking」：`/messages` 不能显式传 `thinking:{"type":"enabled"}`，代理转发前自动移除（官方默认即思考，能力完整保留；`disabled` 保留）。
- 上游拒绝时带上游返回的具体错误原因（同时写入代理日志），方便定位。
- 代理安全阈值可手动设置：设置里新增「代理安全阈值（%）」，默认 3%——账号额度剩余低于该比例才停止供代理使用，0–20% 可调。
- 上游改为直连：代理到 CommandCode 的请求不再经过本机系统代理（127.0.0.1:10090），避免本地 VPN / 代理导致的连接重置、502 和额外延迟。
- 上下文按官方模型清单写入：本地代理的 /v1/models 现在带上官方 `context_length`，配置客户端时按每个模型的真实上限写入。改完在代理面板重新点一次「配置客户端」生效。
- 单账号不再被误冷却：只有账号池里还有别的账号时才冷却 15 秒换号，避免唯一账号被冷却后整段时间不可用。

![账号列表与用量](https://github.com/xiaoer2021/commandcode-manager/releases/download/v1.6.23/overview.png)
