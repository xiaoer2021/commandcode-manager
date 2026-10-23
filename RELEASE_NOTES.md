## 本次更新

- 代理按模型的上游能力选择接口；Anthropic Messages、OpenAI Chat Completions 与 OpenAI Responses 三种对话协议可以自动互转。
- 补齐跨协议文本、工具调用和流式响应适配；原生支持的协议仍直接转发，其他 `/v1/` 接口保留原样透传。
- 模型列表会显示代理可接入的对话协议，额度切换和请求记录继续沿用原有账号池。

![本地代理账号池](https://github.com/xiaoer2021/commandcode-manager/releases/download/v1.5.3/proxy-pool.png)

跨协议转换覆盖常用文本和工具调用字段。某些平台专属扩展字段或无对应格式的多模态内容只能通过上游原生协议使用；接口可用性仍由平台账号权限和模型能力决定。已开始输出的请求不会跨账号重放。
