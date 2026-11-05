## 本次更新

- 新增 DeepSeek 平台：作为上游模型 API 接入本地代理，供 CommandCode、OpenCode 客户端通过代理调用 DeepSeek 模型。
- 修复：Command Code 客户端里「工作台代理」Provider 因模型字段不兼容而无法显示的问题（严格校验）。
- 修复：过滤掉其他平台混入的模型，只保留 DeepSeek 模型；默认模型优先 deepseek-flash（V4.1 Flash）。
- DeepSeek 账号操作按钮改为「配置客户端」，点击直接进入代理面板写入客户端。
- 修复：DeepSeek 账号因数据过期被移出代理池的问题。
- DeepSeek 账号列表精简：只显示余额与状态。
- 余额按币种分开显示，人民币（¥）与美元（$）不再混加。

![账号列表与用量](https://github.com/xiaoer2021/commandcode-manager/releases/download/v1.6.8/overview.png)
