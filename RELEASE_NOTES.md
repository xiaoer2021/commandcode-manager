## 本次更新

- 修复 Command Code 浏览器授权回调地址，授权完成后更稳定地返回工作台。
- HTTP 401 状态改为“认证失败”，并提示可能是凭据失效或服务端验证接口变化，不再直接断言账号已失效。
- 代理账号池同步调整认证失败提示，避免误导为额度耗尽。

![本地代理账号池](https://github.com/xiaoer2021/commandcode-manager/releases/download/v1.5.6/proxy-pool.png)
