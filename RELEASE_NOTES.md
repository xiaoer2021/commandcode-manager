## 本次更新

- 认证失败的账号会将“启用账号”按钮改为“重新认证”，可直接原位更新凭据，无需删除并重新添加。
- 认证恢复入口支持 Command Code 浏览器授权或新 API Key；OpenCode Go 可更新 API Key。
- 新凭据验证通过后才会替换；失败时保留旧凭据、账号备注及本地 Token 历史。
- 普通账号仍显示原有的平台启用操作。

![本地代理账号池](https://github.com/xiaoer2021/commandcode-manager/releases/download/v1.5.8/proxy-pool.png)
