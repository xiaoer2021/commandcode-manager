# CommandCode 管理器
Windows 10/11 x64 多账号用量管理器，使用 React + TypeScript、.NET Framework 和系统 WebView2。前端为 React + TypeScript，桌面窗口、托盘和账号服务为 C#；GitHub 的语言标签按源码字节数统计，C# 为主要语言。支持官方网页登录授权、API Key、账号切换、余额与额度监控、日/周/月 Token 统计、本地历史缓存、CSV 导出及密码保护的账号迁移。
## 安装与数据
从 [GitHub Releases](https://github.com/xiaoer2021/commandcode-manager/releases/latest) 下载安装包，安装后从桌面快捷方式打开。也可下载便携 ZIP，解压完整文件夹后运行。本仓库公开，可直接下载。
账号、备注、设置和用量缓存保存在当前 Windows 用户的应用数据目录，凭据通过 Windows DPAPI 保护。账号密码只在官方网页登录页面输入，不保存在管理器中。安装包、源码和 Releases 不包含本机账号或缓存。
覆盖安装和卸载默认保留个人数据。关闭窗口继续托盘监控，安装前请从托盘“彻底退出”。底部“版本号 · 查看更新”打开最新发布页。
## 本地构建
需要 Node.js 24、Windows .NET Framework 4.8、Inno Setup 6。运行 installer/build.ps1，脚本安装前端依赖、恢复指定版本 WebView2 SDK、构建并生成安装包和便携包。WebView2 Bootstrapper 下载后验证微软签名。
modern/ui/ 是界面源码，modern/Host.cs 是桌面主程序，native/Manager.cs 是账号与 API 服务，modern/addons/ 是官方客户端用量面板组件。其余 Electron 目录保留为旧版源码，当前发布不使用旧版构建。
## 发布更新
1. 修改代码，将 version.json 中版本改为下一版，例如 1.0.1。
2. 提交并推送 main 分支，GitHub Actions 自动检查并生成构建产物。
3. 创建并推送对应标签，例如 v1.0.1；标签必须与 version.json 一致。
4. GitHub Actions 自动生成 Release，附带安装包、便携包和 SHA256SUMS.txt。
用户点击“查看更新”，下载新版安装包覆盖安装。普通代码推送不会直接发布给用户；当前采用手动下载安装，未启用静默替换。当前 Releases 已公开，下载无需登录。
## 验证
modern/tests/portable-features.ps1 验证加密迁移、凭据分类、日期分页与 CSV；modern/tests/history-cache.ps1 验证缓存隔离和增量查询。固定程序 --verify 使用模拟账号验证界面，不修改真实账号。
## 接口说明
使用 CommandCode 官方 alpha 接口获取身份、额度、订阅和请求汇总，接口可能随官方产品变化。Token 数据来自官方汇总，并按周期边界差值统计，不通过费用估算。
## v1.0.1 界面与操作
登录窗口使用官方 SVG 标识。导入账号显示实时步骤，重复账号在本地快速识别并作为普通提示处理。账号列表窗口默认每页 5 条，最大化默认 6 条，可在居中的分页器旁选择数量；菜单根据窗口可用空间显示选项。窗口大小调整结束后提供顶部恢复默认提示，顶部边缘支持拖拽，列表利用剩余高度。