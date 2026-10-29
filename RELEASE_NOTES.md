## 本次更新

- 新增账号凭据原位更新：在账号详情中重新授权或更换 API Key，通过验证后更新凭据，不需要删除并重新添加账号。
- 更新失败时保留原凭据、账号记录、备注和本地 Token 历史；Command Code 会核对账号身份，避免把其他账号的凭据写入原记录。
- OpenCode Go 更新凭据时会通过官方用量接口检查；过期或撤销的凭据需要先在对应官方平台重新授权或生成新 Key。
- 保留 Command Code 登录回调稳定性修复及更清晰的认证错误说明。

![本地代理账号池](https://github.com/xiaoer2021/commandcode-manager/releases/download/v1.5.7/proxy-pool.png)
