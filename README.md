# MultiAccountCommerce

基于 `emoji_window.dll` 的电商多账号浏览器示例仓库，当前包含三套实现：

- `Python/`：Python 版多环境 CEF 原型
- `Csharp/`：C# 版界面草图，目录内同时提供源码与可直接运行的 EXE
- `E/`：易语言版本源码与对应 DLL

项目目标是把同一套“电商多账号浏览器”界面方案落地到不同技术栈，并逐步补齐真实浏览器运行能力。

## 界面预览

### 截图 1

![界面截图1](img/1.png)

### 截图 2

![界面截图2](img/2.png)

## 目录结构

```text
MultiAccountCommerce/
├─ Csharp/
│  ├─ Program.cs
│  ├─ EcommerceWorkspaceSketchApp.cs
│  ├─ EmojiWindowNative.cs
│  ├─ EmojiWindowEcommerceWorkspaceSketchDemo.csproj
│  ├─ EmojiWindowEcommerceWorkspaceSketchDemo.exe
│  ├─ emoji_window.dll
│  └─ favicon.ico
├─ Python/
│  ├─ demo_env_workspace_sketch.py
│  ├─ cef_env_host.py
│  ├─ requirements.txt
│  ├─ README.md
│  ├─ emoji_window.dll
│  ├─ favicon.ico
│  ├─ cache/
│  ├─ cookies/
│  └─ logs/
├─ E/
│  ├─ 电商多账号浏览器UI.e
│  └─ emoji_window.dll
└─ img/
   ├─ 1.png
   └─ 2.png
```

## 运行说明

### Python 版本

目录：`Python/`

要求：

- Windows x64
- Python 3.9 x64
- `cefpython3==66.1`
- `emoji_window.dll` 与脚本放在同一目录

运行：

```bash
cd Python
py -3.9 -m pip install -r requirements.txt
py -3.9 demo_env_workspace_sketch.py
```

说明：

- 当前 Python 原型会在启动时强制检查 Python 版本，非 Python 3.9 x64 会直接拒绝运行
- 当前脚本会优先从当前目录读取 `emoji_window.dll` 和 `favicon.ico`
- `cache/` 为每个环境的独立缓存目录，`cookies/` 为 Cookie 导出目录，`logs/` 为运行日志目录
- 更详细的说明见 [Python/README.md](Python/README.md)

### C# 版本

目录：`Csharp/`

要求：

- Windows
- .NET Framework 4.8
- `emoji_window.dll` 与 EXE 放在同一目录

直接运行：

```text
Csharp/EmojiWindowEcommerceWorkspaceSketchDemo.exe
```

如果需要重新编译源码：

```bash
cd Csharp
msbuild EmojiWindowEcommerceWorkspaceSketchDemo.csproj /p:Configuration=Debug /p:Platform=x64
```

说明：

- 目录内已包含现成 EXE，可直接运行
- 目录内也保留了完整源码，便于继续开发

### 易语言版本

目录：`E/`

说明：

- `电商多账号浏览器UI.e` 为易语言版本源码
- `emoji_window.dll` 已一并放入目录
- 使用易语言打开工程后，保证 DLL 与程序在同目录即可调试或运行

## 当前功能

当前 Python 原型已包含：

- 左侧树形环境切换
- Amazon / TikTok / 独立站分组示例
- 右侧环境信息区与工具栏
- 真实 CEF 浏览器工作区
- 每环境独立浏览器实例
- 每环境独立缓存目录
- 地址栏回车跳转
- 刷新、停止、后台保活
- Cookie 导出
- 明暗主题切换

当前版本仍属于开发态原型：

- 环境级代理配置只完成接口与状态贯通，不保证同进程内每环境独立代理真正生效
- 当前不包含 EXE 打包
- 当前仍以多环境浏览器宿主结构与交互原型为主，业务能力仍需继续扩展

欢迎加入 QQ 交流群：`523446917`
如果这个项目对你有帮助，别忘了点个 Star。

## 依赖说明

本仓库运行依赖核心 DLL：

- `emoji_window.dll`
  开源地址：<https://github.com/mosheng20205/emoji-ui-dll>

Python 版还依赖：

- `cefpython3==66.1`

为了保证复制后即可运行，Python、C#、易语言目录内都分别放置了各自需要的 DLL 文件。

## 开源版与商业版区别

本仓库当前提供的是开源版示例，主要用于展示多账号浏览器的基础界面结构、环境切换、分组管理、代理管理和浏览器宿主能力。部分高级业务能力仅在商业版中提供。

| 功能模块 | 开源版 | 商业版 |
| --- | --- | --- |
| 环境切换 | 支持基础环境树、分组和环境切换 | 支持更完整的环境生命周期和批量管理能力 |
| 分组管理 | 支持基础分组维护 | 支持更完整的分组策略、批量配置和业务扩展 |
| 代理管理 | 支持 HTTP、HTTP 账号密码、无账号密码 SOCKS5 | 支持 HTTP、HTTP 账号密码、SOCKS5 账号密码等更完整代理能力 |
| 浏览器指纹 | 不支持浏览器指纹配置 | 支持浏览器指纹配置与隔离能力 |
| RPA 自动化 | 仅保留菜单入口和商业版说明占位 | 支持任务编排、批量执行、自动化操作等 RPA 能力 |
| 插件中心 | 仅保留菜单入口和商业版说明占位 | 支持插件安装、启停、配置和扩展能力 |
| 团队协作 | 仅保留菜单入口和商业版说明占位 | 支持成员、角色、权限、环境共享等团队协作能力 |
| 售后与升级 | 社区自助使用 | 提供商业授权、部署支持和持续升级服务 |

开源版中的 `RPA自动化`、`插件中心`、`团队协作` 菜单会显示商业版功能说明，不包含对应业务实现。如需使用这些功能，请购买商业版或联系作者获取授权。
