## 本次更新

- 扩展本地 API 网关：不再将请求限定为 Chat Completions、Responses、Messages 三条路由，可透传 `/v1/` 下由上游处理的 API 路径和 GET、POST、PUT、PATCH、DELETE 请求。
- 保留查询参数、JSON 与 multipart 请求体、SSE 响应及常见 SDK 协议头，适配更多调用工具和接口形态。
- 自动归一化 Claude SDK 常见的重复 `/v1/v1/...` 路径，并继续按模型 ID 选择账号和执行额度切换。
- 更新本地代理使用说明，明确固定平台上游、协议透传边界和不支持的请求类型。

![本地代理账号池](https://github.com/xiaoer2021/commandcode-manager/releases/download/v1.5.2/proxy-pool.png)

## 使用说明

这是 Command Code 与 OpenCode Go / Zen 的 API 网关，不是通用 HTTP / SOCKS 代理，也不负责在不同厂商之间转换协议。接口能否调用仍取决于对应平台、模型和上游 API 能力；Gemini 原生 `/v1beta/` 路由及无法关联模型 ID 的接口暂不支持。已开始输出的请求不会跨账号重放。
