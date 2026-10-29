# 多账号工作台

一个面向 Windows 的多平台 API 账号管理工具，把账号、用量、Token 统计和本地代理集中到一个工作台。

支持 **Command Code** 和 **OpenCode Go / Zen**。适合持有多个账号、需要关注额度变化，或希望通过统一 API 地址使用账号池的用户。

[下载安装](https://github.com/xiaoer2021/commandcode-manager/releases/latest) · [界面与操作图](docs/screenshots/v1.5.1/README.md) · [本地代理使用说明](docs/LOCAL_PROXY.md) · [代理池统计 API 与 OpenAPI 文档](docs/PROXY_USAGE_API.md)

[![多账号工作台总览](docs/screenshots/v1.5.1/overview.png)](https://raw.githubusercontent.com/xiaoer2021/commandcode-manager/main/docs/screenshots/v1.5.1/overview.png)

## 可以做什么

| 功能 | 说明 |
| --- | --- |
| 多平台账号 | 添加官方账号、粘贴 API Key 或导入本机登录；按平台筛选，修改别名和启用对应平台账号 |
| 额度与提醒 | 查看月度、每周、5 小时用量及重置时间，按设定间隔刷新，达到阈值后桌面提醒 |
| Token 统计 | 查看输入、输出和累计 Token，按日、周、月查询，支持日期范围与 CSV 导出 |
| 列表与卡片 | 一键切换账号展示形式，支持搜索、分页、自动或手动调整每页数量 |
| 账号右键菜单 | 打开应用、启用账号、修改别名、查看详情、刷新用量和导出单个账号 |
| 账号导入导出 | 使用密码保护的账号包迁移账号，导入后继续使用 |
| 本地 API 代理 | 使用统一地址和代理 Key；在请求开始前遇到明确限额时尝试其他兼容账号 |
| 自动账号池 | 可用账号自动加入，额度不足或受限账号退出，恢复后重新参与 |
| 多 Key 管理 | 为不同设备生成独立 Key，支持复制、禁用和删除 |
| 代理池统计 API | 向其他程序提供只读的账号额度、Token 历史、代理状态和请求概况，并附完整 OpenAPI 文档 |
| 快速使用 | 将代理地址与模型配置到 Command Code / OpenCode，减少手动配置 |
| 托盘运行 | 关闭主窗口后继续监控，通过托盘刷新用量或启用账号 |

平台返回的数据能力不同，未提供的字段不会估算。OpenCode 本地历史按服务商汇总，不代表单个登录账号的独立账单；Go 的续费日期可在账号详情中手动记录。

## 产品界面

### Token 用量统计

右键账号选择“用量统计”，按每天、每周、每月查看，选择日期范围后查询或导出明细。

[![Token 用量统计](docs/screenshots/v1.5.1/token-history.png)](https://raw.githubusercontent.com/xiaoer2021/commandcode-manager/main/docs/screenshots/v1.5.1/token-history.png)

### 本地代理与账号池

在顶部打开“本地代理”，选择可用账号并开启，可复制统一 API 地址和代理 Key 供其他软件或同网络设备使用。

[![本地代理账号池](docs/screenshots/v1.5.1/proxy-pool.png)](https://raw.githubusercontent.com/xiaoer2021/commandcode-manager/main/docs/screenshots/v1.5.1/proxy-pool.png)

<details>
<summary>查看卡片视图、添加账号和账号右键菜单</summary>

**卡片视图**：点击“刷新用量”右侧的视图按钮切换。

[![账号卡片视图](docs/screenshots/v1.5.1/cards.png)](https://raw.githubusercontent.com/xiaoer2021/commandcode-manager/main/docs/screenshots/v1.5.1/cards.png)

**添加账号**：选择平台与服务，按界面提示登录、粘贴 API Key 或导入本机账号。

[![添加账号](docs/screenshots/v1.5.1/add-account.png)](https://raw.githubusercontent.com/xiaoer2021/commandcode-manager/main/docs/screenshots/v1.5.1/add-account.png)

**右键菜单**：在账号上点击鼠标右键，直接操作该账号。

[![账号右键菜单](docs/screenshots/v1.5.1/account-menu.png)](https://raw.githubusercontent.com/xiaoer2021/commandcode-manager/main/docs/screenshots/v1.5.1/account-menu.png)

</details>

<details>
<summary>查看代理 Key 管理与快速使用</summary>

**代理 Key 管理**：为不同设备命名生成 Key，分别控制启用状态。

[![代理 Key 管理](docs/screenshots/v1.5.1/proxy-keys.png)](https://raw.githubusercontent.com/xiaoer2021/commandcode-manager/main/docs/screenshots/v1.5.1/proxy-keys.png)

**快速使用**：开启代理并保存后选择客户端。Command Code 重开后需选择“工作台代理”模型组；客户端已运行时，会先询问是否重启。

[![客户端快速使用](docs/screenshots/v1.5.1/quick-use.png)](https://raw.githubusercontent.com/xiaoer2021/commandcode-manager/main/docs/screenshots/v1.5.1/quick-use.png)

</details>

## 下载与安装

适用于 **Windows 10 / 11 x64**。

1. 打开 [最新版本下载页](https://github.com/xiaoer2021/commandcode-manager/releases/latest)，展开 **Assets**。
2. 下载名称包含 **Setup** 的 `.exe`，运行安装向导；或下载包含 **Portable** 的 `.zip`，完整解压后运行“多账号工作台.exe”。
3. 首次运行若提示缺少 WebView2，请完成运行环境安装。
4. 打开“添加账号”，选择平台并添加账号。已有账号也可通过“账号工具”导入。

工作台会自动检测新版本，发现更新后提示，由用户选择下载和安装。关闭窗口后继续托盘运行；需要完全退出时，右键托盘图标选择“彻底退出”。

## 数据保存与账号迁移

账号、备注、设置和用量缓存保存在当前 Windows 用户的 `%APPDATA%\commandcode-account-manager` 目录，凭据使用 Windows DPAPI 加密。官方网页登录的密码不会保存在工作台。

更换电脑时，通过“账号工具 → 导出账号”生成密码保护的账号包，在另一台电脑导入。直接复制本机加密数据文件不能代替账号导出。覆盖安装和卸载默认保留个人数据。

## 本地代理如何使用

1. 添加支持 API 的账号并刷新用量。
2. 打开“本地代理”，勾选可用账号，或开启“自动加入账号池”。
3. 开启并保存，复制面板中的 API 地址和代理 Key 到调用软件；也可通过“快速使用”配置受支持客户端。
4. 保持工作台运行，其他设备使用时需处于可连通网络。

代理默认选择正常局域网网卡，不修改系统代理、VPN、路由或 DNS。自动换号保留原模型和平台；已经开始输出的回复不会重放，所有兼容账号都不可用时仍会返回错误。详细配置见 [本地代理说明](docs/LOCAL_PROXY.md)。

## 自己开发与运行

### 技术组成

| 部分 | 技术 |
| --- | --- |
| 用户界面 | React、TypeScript、Ant Design、Vite |
| 桌面程序 | C#、.NET Framework 4.8、WebView2 |
| 数据保护 | Windows DPAPI |
| 安装包 | Inno Setup |

### 准备环境

- Windows 10 / 11 x64、Git、Node.js 24。
- .NET Framework 4.8 与 WebView2 Runtime。
- 仅生成安装包时需要 Inno Setup 6。

### 首次构建并启动

在 PowerShell 中执行：

```powershell
$ErrorActionPreference = 'Stop'
git clone https://github.com/xiaoer2021/commandcode-manager.git
cd commandcode-manager
powershell -NoProfile -ExecutionPolicy Bypass -File modern/restore-sdk.ps1
if ($LASTEXITCODE -ne 0) { throw 'WebView2 SDK 准备失败' }
npm --prefix modern/ui ci
if ($LASTEXITCODE -ne 0) { throw '界面依赖安装失败' }
npm --prefix modern/ui run build
if ($LASTEXITCODE -ne 0) { throw '界面构建失败' }
powershell -NoProfile -ExecutionPolicy Bypass -File modern/build.ps1
if ($LASTEXITCODE -ne 0) { throw '桌面构建失败' }
& '.\desktop\CommandCode Manager.exe'
```

### 修改代码后重新构建

在项目根目录执行，构建完成后重新打开本地开发程序：

```powershell
npm --prefix modern/ui run build
if ($LASTEXITCODE -ne 0) { throw '界面构建失败' }
powershell -NoProfile -ExecutionPolicy Bypass -File modern/build.ps1
if ($LASTEXITCODE -ne 0) { throw '桌面构建失败' }
& '.\desktop\CommandCode Manager.exe'
```

开发程序默认使用当前 Windows 用户的数据目录。查看界面或验证布局时，可使用隔离的模拟模式：

```powershell
& '.\desktop\CommandCode Manager.exe' --verify
```

### 主要代码位置

| 路径 | 修改内容 |
| --- | --- |
| `modern/ui/src/` | 页面、样式、交互与组件 |
| `modern/Host.cs` | 桌面窗口、托盘及界面与后台通信 |
| `modern/LocalProxy.cs` | 本地代理、账号调度与请求转发 |
| `modern/ProxyClientSetup.cs` | 快速使用的客户端配置 |
| `modern/OpenCode.cs` | OpenCode 接入与本地统计 |
| `native/Manager.cs` | 账号、用量、提醒和本地数据 |
| `modern/tests/` | 主要功能验证 |
| `installer/` | 安装包、打包与公开文件检查 |
| `docs/` | 使用说明与产品截图 |

当前桌面程序使用 `modern/` 与 `native/`。根目录的 Electron 入口及 `ui/`、`lib/` 保留为早期代码，不用于当前桌面包。

### 生成安装包

安装 Inno Setup 6 后，在项目根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File installer/build.ps1
```

脚本会准备前端依赖和 WebView2 SDK，构建桌面程序，生成安装 `.exe`、便携 `.zip` 和 `SHA256SUMS.txt`，输出到 `release/`。

### 发布自己的修改

修改 `version.json`，补充 `RELEASE_NOTES.md` 和对应功能截图，再提交代码。仓库已配置 GitHub Actions：推送主分支执行构建与检查，推送与版本号匹配的 `v` 标签后创建 Release。

发布前执行公开文件检查，账号、凭据、缓存和本地产物不应进入源码仓库：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File installer/audit-source.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File installer/check-release-images.ps1
```
