## 本次更新

- 新增代理池只读统计 API，可供其他程序查询代理状态、账号可用额度、套餐周期、Token 汇总和缓存的日/周/月统计。
- 新增最近代理请求概况接口；不返回 API Key、提示词或请求正文，数据仅保存在运行期间的内存中。
- 提供完整 OpenAPI 3.0 文档，可从 `/v1/workspace/openapi.json` 获取或导入仓库文档。
- 所有统计接口使用现有代理 Key 鉴权，只接受 GET，不主动刷新上游数据。

![本地代理账号池](https://github.com/xiaoer2021/commandcode-manager/releases/download/v1.5.11/proxy-pool.png)
