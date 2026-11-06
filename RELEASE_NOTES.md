## 本次更新

- 上游改为直连：代理到 CommandCode 的请求不再经过本机系统代理（127.0.0.1:10090），避免本地 VPN / 代理导致的连接重置、502 和额外延迟。
- 上下文按官方模型清单写入：本地代理的 /v1/models 现在带上官方 `context_length`，配置客户端时按每个模型的真实上限写入（DeepSeek V4.1 Flash = 1,000,000；200k 的模型不会再被写成 1M）。改完在代理面板重新点一次「配置客户端」生效。
- 单账号不再被误冷却：只有账号池里还有别的账号时才冷却 15 秒换号，避免唯一账号被冷却后整段时间不可用。
- 502 记录里带上具体异常信息（如 HttpRequestException 的原文），方便判断是超时、断网还是上游中断。

![账号列表与用量](https://github.com/xiaoer2021/commandcode-manager/releases/download/v1.6.16/overview.png)
