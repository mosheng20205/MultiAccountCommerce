# Python 3.10 CEF 原型运行说明

## 目标运行时

这套 Python 版多环境浏览器原型固定支持：

- Windows x64
- Python 3.9 x64
- `cefpython3==66.1`
- `emoji_window.dll`

不支持：

- Python 3.14
- 32 位 Python
- 非 Windows 平台

## 安装步骤

1. 安装 Python 3.10 x64
2. 进入当前目录
3. 安装依赖

```bash
python -m pip install -r requirements.txt
```

如果一台机器装了多个 Python，建议显式使用 3.9：

```bash
py -3.9 -m pip install -r requirements.txt
py -3.9 demo_env_workspace_sketch.py
```

## 目录结构

运行时目录约定如下：

```text
Python/
├─ demo_env_workspace_sketch.py
├─ cef_env_host.py
├─ requirements.txt
├─ emoji_window.dll
├─ favicon.ico
├─ cache/
├─ cookies/
└─ logs/
```

其中：

- `cache/`：每个环境的独立 CEF 缓存目录
- `cookies/`：Cookie 导出文件目录
- `logs/`：CEF 与宿主模块日志目录

## 启动

```bash
cd Python
py -3.9 demo_env_workspace_sketch.py
```

## 当前功能

- 左侧多环境树切换
- 每环境独立浏览器实例
- 每环境独立缓存目录
- 地址栏回车跳转
- 刷新、停止、后台保活
- 切换环境时显隐浏览器实例
- Cookie 导出到环境文件

## 已知限制

- `cefpython3` 当前为单进程原型接入，环境级代理配置只完成接口与状态贯通，不保证同进程内每环境独立代理真正生效。
- 当前以开发态脚本运行，不包含 EXE 打包。
- 如果本机 Python 不是 3.9 x64，脚本会在启动时直接拒绝运行。
