# -*- coding: utf-8 -*-
from __future__ import annotations

import ctypes
import struct
from ctypes import wintypes
from dataclasses import dataclass
from pathlib import Path


HWND = wintypes.HWND
UINT32 = wintypes.UINT
BOOL = wintypes.BOOL

SW_SHOW = 5
SW_HIDE = 0
WM_SETREDRAW = 0x000B
RDW_INVALIDATE = 0x0001
RDW_ERASE = 0x0004
RDW_ALLCHILDREN = 0x0080
RDW_FRAME = 0x0400
RDW_UPDATENOW = 0x0100
CALLBACK_NODE_SELECTED = 1

USER32 = ctypes.windll.user32

WINDOW_WIDTH = 1480
WINDOW_HEIGHT = 920
TITLE_BAR_HEIGHT = 32
CONTENT_TOP_OFFSET = 6
OUTER = 16
LEFT_W = 320
GAP = 12
TOP_ACTION_H = 56
INFO_H = 52
TOOLBAR_H = 48


def argb(a: int, r: int, g: int, b: int) -> int:
    return ((a & 255) << 24) | ((r & 255) << 16) | ((g & 255) << 8) | (b & 255)


def clamp(value: int, low: int, high: int) -> int:
    return max(low, min(high, value))


def shift_color(color: int, delta: int) -> int:
    a = (color >> 24) & 255
    r = clamp(((color >> 16) & 255) + delta, 0, 255)
    g = clamp(((color >> 8) & 255) + delta, 0, 255)
    b = clamp((color & 255) + delta, 0, 255)
    return argb(a, r, g, b)


def mix_color(base: int, target: int, ratio: float) -> int:
    ratio = max(0.0, min(1.0, ratio))
    a = (base >> 24) & 255
    br, bg, bb = (base >> 16) & 255, (base >> 8) & 255, base & 255
    tr, tg, tb = (target >> 16) & 255, (target >> 8) & 255, target & 255
    r = int(br * (1.0 - ratio) + tr * ratio)
    g = int(bg * (1.0 - ratio) + tg * ratio)
    b = int(bb * (1.0 - ratio) + tb * ratio)
    return argb(a, r, g, b)


def color_brightness(color: int) -> int:
    r = (color >> 16) & 255
    g = (color >> 8) & 255
    b = color & 255
    return int(r * 0.299 + g * 0.587 + b * 0.114)


def utf8_buffer(text: str) -> tuple[ctypes.c_void_p, int, object | None]:
    raw = text.encode("utf-8")
    if not raw:
        return ctypes.c_void_p(), 0, None
    buf = (ctypes.c_ubyte * len(raw))(*raw)
    return ctypes.cast(buf, ctypes.c_void_p), len(raw), buf


def bytes_buffer(data: bytes) -> tuple[ctypes.c_void_p, int, object | None]:
    if not data:
        return ctypes.c_void_p(), 0, None
    buf = (ctypes.c_ubyte * len(data)).from_buffer_copy(data)
    return ctypes.cast(buf, ctypes.c_void_p), len(data), buf


def repo_root() -> Path:
    return Path(__file__).resolve().parents[3]


def dll_path() -> Path:
    primary = repo_root() / "bin" / "x64" / "Release" / "emoji_window.dll"
    fallback = Path(__file__).resolve().parent / "emoji_window.dll"
    return primary if primary.is_file() else fallback


def icon_path() -> Path:
    candidates = [
        Path(__file__).resolve().parent / "favicon.ico",
        repo_root() / "examples" / "Csharp" / "EmojiWindowEcommerceMultiAccountDemo" / "favicon.ico",
        repo_root() / "examples" / "Python" / "谷歌.ico",
    ]
    for item in candidates:
        if item.is_file():
            return item
    return candidates[0]


FONT_YAHEI_RAW = "Microsoft YaHei UI".encode("utf-8")
FONT_YAHEI_BUF = (ctypes.c_ubyte * len(FONT_YAHEI_RAW))(*FONT_YAHEI_RAW)
FONT_YAHEI_PTR = ctypes.cast(FONT_YAHEI_BUF, ctypes.c_void_p)
FONT_YAHEI_LEN = len(FONT_YAHEI_RAW)

FONT_SEGOE_RAW = "Segoe UI".encode("utf-8")
FONT_SEGOE_BUF = (ctypes.c_ubyte * len(FONT_SEGOE_RAW))(*FONT_SEGOE_RAW)
FONT_SEGOE_PTR = ctypes.cast(FONT_SEGOE_BUF, ctypes.c_void_p)
FONT_SEGOE_LEN = len(FONT_SEGOE_RAW)


@dataclass
class EnvironmentRecord:
    node_id: int
    group_name: str
    name: str
    domain: str
    proxy: str
    status: str
    score: int


class RedrawScope:
    def __init__(self, hwnd: HWND) -> None:
        self.handle = int(ctypes.cast(hwnd, ctypes.c_void_p).value or 0)
        USER32.SendMessageW(self.handle, WM_SETREDRAW, 0, 0)

    def __enter__(self) -> "RedrawScope":
        return self

    def __exit__(self, exc_type, exc, tb) -> None:
        USER32.SendMessageW(self.handle, WM_SETREDRAW, 1, 0)
        USER32.RedrawWindow(
            self.handle,
            None,
            None,
            RDW_INVALIDATE | RDW_ERASE | RDW_FRAME | RDW_ALLCHILDREN | RDW_UPDATENOW,
        )


class NativeApi:
    def __init__(self) -> None:
        if struct.calcsize("P") * 8 != 64:
            raise OSError("Please use 64-bit Python.")
        path = dll_path()
        if not path.is_file():
            raise FileNotFoundError(path)
        self.dll = ctypes.WinDLL(str(path))
        self._setup()

    def _setup(self) -> None:
        dll = self.dll
        self.ButtonClickCallback = ctypes.WINFUNCTYPE(None, ctypes.c_int, HWND)
        self.WindowResizeCallback = ctypes.WINFUNCTYPE(None, HWND, ctypes.c_int, ctypes.c_int)
        self.TreeViewCallback = ctypes.WINFUNCTYPE(None, ctypes.c_int, ctypes.c_void_p)

        dll.create_window_bytes_ex.argtypes = [
            ctypes.c_void_p,
            ctypes.c_int,
            ctypes.c_int,
            ctypes.c_int,
            ctypes.c_int,
            ctypes.c_int,
            UINT32,
            UINT32,
        ]
        dll.create_window_bytes_ex.restype = HWND
        dll.ShowEmojiWindow.argtypes = [HWND, ctypes.c_int]
        dll.set_message_loop_main_window.argtypes = [HWND]
        dll.run_message_loop.argtypes = []
        dll.set_window_icon_bytes.argtypes = [HWND, ctypes.c_void_p, ctypes.c_int]
        dll.set_window_title.argtypes = [HWND, ctypes.c_void_p, ctypes.c_int]
        dll.set_window_titlebar_color.argtypes = [HWND, UINT32]
        dll.SetTitleBarTextColor.argtypes = [HWND, UINT32]
        dll.SetWindowBackgroundColor.argtypes = [HWND, UINT32]
        dll.SetWindowResizeCallback.argtypes = [self.WindowResizeCallback]
        dll.SetDarkMode.argtypes = [BOOL]
        dll.IsDarkMode.argtypes = []
        dll.IsDarkMode.restype = BOOL

        dll.CreatePanel.argtypes = [HWND, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, UINT32]
        dll.CreatePanel.restype = HWND
        dll.SetPanelBackgroundColor.argtypes = [HWND, UINT32]

        dll.CreateLabel.argtypes = [
            HWND,
            ctypes.c_int,
            ctypes.c_int,
            ctypes.c_int,
            ctypes.c_int,
            ctypes.c_void_p,
            ctypes.c_int,
            UINT32,
            UINT32,
            ctypes.c_void_p,
            ctypes.c_int,
            ctypes.c_int,
            ctypes.c_int,
            ctypes.c_int,
            ctypes.c_int,
            ctypes.c_int,
            ctypes.c_int,
        ]
        dll.CreateLabel.restype = HWND
        dll.SetLabelText.argtypes = [HWND, ctypes.c_void_p, ctypes.c_int]
        dll.SetLabelBounds.argtypes = [HWND, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int]
        dll.SetLabelColor.argtypes = [HWND, UINT32, UINT32]

        dll.create_emoji_button_bytes.argtypes = [
            HWND,
            ctypes.c_void_p,
            ctypes.c_int,
            ctypes.c_void_p,
            ctypes.c_int,
            ctypes.c_int,
            ctypes.c_int,
            ctypes.c_int,
            ctypes.c_int,
            UINT32,
        ]
        dll.create_emoji_button_bytes.restype = ctypes.c_int
        dll.set_button_click_callback.argtypes = [self.ButtonClickCallback]
        dll.SetButtonBounds.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int]
        dll.SetButtonRound.argtypes = [ctypes.c_int, ctypes.c_int]
        dll.SetButtonStyle.argtypes = [ctypes.c_int, ctypes.c_int]
        dll.SetButtonSize.argtypes = [ctypes.c_int, ctypes.c_int]
        dll.SetButtonBackgroundColor.argtypes = [ctypes.c_int, UINT32]
        dll.SetButtonBorderColor.argtypes = [ctypes.c_int, UINT32]
        dll.SetButtonTextColor.argtypes = [ctypes.c_int, UINT32]
        dll.SetButtonHoverColors.argtypes = [ctypes.c_int, UINT32, UINT32, UINT32]
        dll.ShowButton.argtypes = [ctypes.c_int, ctypes.c_int]

        dll.CreateTreeView.argtypes = [HWND, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, UINT32, UINT32, ctypes.c_void_p]
        dll.CreateTreeView.restype = HWND
        dll.ClearTree.argtypes = [HWND]
        dll.ClearTree.restype = BOOL
        dll.AddRootNode.argtypes = [HWND, ctypes.c_void_p, ctypes.c_int, ctypes.c_void_p, ctypes.c_int]
        dll.AddRootNode.restype = ctypes.c_int
        dll.AddChildNode.argtypes = [HWND, ctypes.c_int, ctypes.c_void_p, ctypes.c_int, ctypes.c_void_p, ctypes.c_int]
        dll.AddChildNode.restype = ctypes.c_int
        dll.RemoveNode.argtypes = [HWND, ctypes.c_int]
        dll.RemoveNode.restype = BOOL
        dll.SetSelectedNode.argtypes = [HWND, ctypes.c_int]
        dll.SetSelectedNode.restype = BOOL
        dll.ExpandNode.argtypes = [HWND, ctypes.c_int]
        dll.ExpandNode.restype = BOOL
        dll.ExpandAll.argtypes = [HWND]
        dll.ExpandAll.restype = BOOL
        dll.SetTreeViewCallback.argtypes = [HWND, ctypes.c_int, self.TreeViewCallback]
        dll.SetTreeViewCallback.restype = BOOL
        dll.SetTreeViewSidebarMode.argtypes = [HWND, BOOL]
        dll.SetTreeViewSidebarMode.restype = BOOL
        dll.SetTreeViewRowHeight.argtypes = [HWND, ctypes.c_float]
        dll.SetTreeViewRowHeight.restype = BOOL
        dll.SetTreeViewItemSpacing.argtypes = [HWND, ctypes.c_float]
        dll.SetTreeViewItemSpacing.restype = BOOL
        dll.SetTreeViewTextColor.argtypes = [HWND, UINT32]
        dll.SetTreeViewTextColor.restype = BOOL
        dll.SetTreeViewBackgroundColor.argtypes = [HWND, UINT32]
        dll.SetTreeViewBackgroundColor.restype = BOOL
        dll.SetTreeViewSelectedBgColor.argtypes = [HWND, UINT32]
        dll.SetTreeViewSelectedBgColor.restype = BOOL
        dll.SetTreeViewSelectedForeColor.argtypes = [HWND, UINT32]
        dll.SetTreeViewSelectedForeColor.restype = BOOL
        dll.SetTreeViewHoverBgColor.argtypes = [HWND, UINT32]
        dll.SetTreeViewHoverBgColor.restype = BOOL
        dll.SetTreeViewFont.argtypes = [HWND, ctypes.c_void_p, ctypes.c_int, ctypes.c_float, ctypes.c_int, BOOL]
        dll.SetTreeViewFont.restype = BOOL
        dll.SetNodeForeColor.argtypes = [HWND, ctypes.c_int, UINT32]
        dll.SetNodeForeColor.restype = BOOL


class EnvironmentWorkspaceSketchApp:
    GROUP_SEED = {
        "Amazon 店群": [
            ("美区-环境01", "amazon.com", "US-Proxy-01", "运行中", 92),
            ("美区-环境02", "amazon.com", "US-Proxy-02", "空闲中", 88),
        ],
        "TikTok 店群": [
            ("TK-环境01", "seller-us.tiktok.com", "US-Proxy-09", "运行中", 95),
        ],
        "独立站": [
            ("独立站-环境01", "shop.example.com", "JP-Proxy-03", "待启动", 84),
        ],
    }

    MODULES = {
        "代理管理": "🌐",
        "分组管理": "🗂️",
        "RPA自动化": "🤖",
        "插件中心": "🧩",
        "团队协作": "👥",
        "系统设置": "⚙️",
    }

    def __init__(self) -> None:
        self.native = NativeApi()
        self.dll = self.native.dll
        self.width = WINDOW_WIDTH
        self.height = WINDOW_HEIGHT

        self.button_actions: dict[int, callable] = {}
        self.node_meta: dict[int, dict[str, str]] = {}
        self.group_nodes: dict[str, int] = {}
        self.module_nodes: dict[str, int] = {}
        self.group_env_nodes: dict[str, list[int]] = {}
        self.environments: dict[int, EnvironmentRecord] = {}
        self.current_node_id: int | None = None
        self.current_env_node: int | None = None
        self.toolbar_visible = True
        self.env_counter = 1

        self.window: HWND | None = None
        self.left_panel: HWND | None = None
        self.workspace_panel: HWND | None = None
        self.left_actions_panel: HWND | None = None
        self.tree_panel: HWND | None = None
        self.info_panel: HWND | None = None
        self.toolbar_panel: HWND | None = None
        self.browser_panel: HWND | None = None
        self.browser_frame: HWND | None = None
        self.browser_canvas: HWND | None = None
        self.tree: HWND | None = None

        self.lbl_left_title: HWND | None = None
        self.lbl_info_main: HWND | None = None
        self.lbl_info_sub: HWND | None = None
        self.lbl_canvas_title: HWND | None = None
        self.lbl_canvas_desc: HWND | None = None
        self.lbl_canvas_hint: HWND | None = None

        self.btn_new_env = 0
        self.btn_delete_env = 0
        self.btn_theme = 0
        self.toolbar_buttons: list[int] = []

        self._button_click_cb = self.native.ButtonClickCallback(self.on_button_click)
        self._tree_select_cb = self.native.TreeViewCallback(self.on_tree_selected)
        self._window_resize_cb = self.native.WindowResizeCallback(self.on_window_resize)

    def run(self) -> None:
        self.create_window()
        with RedrawScope(self.window):
            self.create_controls()
            self.apply_theme()
            self.select_default_environment()
            self.layout()
        self.dll.set_button_click_callback(self._button_click_cb)
        self.dll.SetWindowResizeCallback(self._window_resize_cb)
        self.dll.ShowEmojiWindow(self.window, 1)
        self.dll.set_message_loop_main_window(self.window)
        self.dll.run_message_loop()

    def create_window(self) -> None:
        title_ptr, title_len, title_keep = utf8_buffer("电商多账号浏览器 - 草图版")
        self._title_keep = title_keep
        self.window = self.dll.create_window_bytes_ex(
            title_ptr,
            title_len,
            -1,
            -1,
            self.width,
            self.height,
            argb(255, 37, 99, 235),
            argb(255, 244, 247, 251),
        )
        if not self.window:
            raise RuntimeError("create_window_bytes_ex failed")
        self.dll.SetTitleBarTextColor(self.window, argb(255, 255, 255, 255))
        icon = icon_path()
        if icon.is_file():
            icon_ptr, icon_len, icon_keep = bytes_buffer(icon.read_bytes())
            self._icon_keep = icon_keep
            self.dll.set_window_icon_bytes(self.window, icon_ptr, icon_len)

    def create_controls(self) -> None:
        self.left_panel = self.dll.CreatePanel(self.window, 0, 0, 100, 100, argb(255, 255, 255, 255))
        self.workspace_panel = self.dll.CreatePanel(self.window, 0, 0, 100, 100, argb(255, 244, 247, 251))
        self.left_actions_panel = self.dll.CreatePanel(self.left_panel, 0, 0, 100, TOP_ACTION_H, argb(255, 255, 255, 255))
        self.tree_panel = self.dll.CreatePanel(self.left_panel, 0, 0, 100, 100, argb(255, 255, 255, 255))
        self.info_panel = self.dll.CreatePanel(self.workspace_panel, 0, 0, 100, INFO_H, argb(255, 255, 255, 255))
        self.toolbar_panel = self.dll.CreatePanel(self.workspace_panel, 0, 0, 100, TOOLBAR_H, argb(255, 255, 255, 255))
        self.browser_panel = self.dll.CreatePanel(self.workspace_panel, 0, 0, 100, 100, argb(255, 241, 245, 250))
        self.browser_frame = self.dll.CreatePanel(self.browser_panel, 0, 0, 100, 100, argb(255, 255, 255, 255))
        self.browser_canvas = self.dll.CreatePanel(self.browser_frame, 0, 0, 100, 100, argb(255, 249, 251, 253))

        self.lbl_left_title = self.label(self.left_panel, "环境面板", 12, True)
        self.lbl_info_main = self.label(self.info_panel, "", 13, True)
        self.lbl_info_sub = self.label(self.info_panel, "", 11, False)
        self.lbl_canvas_title = self.label(self.browser_frame, "", 18, True)
        self.lbl_canvas_desc = self.label(self.browser_frame, "", 11, False)
        self.lbl_canvas_hint = self.label(self.browser_canvas, "", 18, True)

        self.btn_new_env = self.button(self.left_panel, "新建环境", argb(255, 37, 99, 235), self.on_new_environment)
        self.btn_delete_env = self.button(self.left_panel, "删除环境", argb(255, 239, 68, 68), self.on_delete_environment)
        self.btn_theme = self.button(self.left_panel, "🌓", argb(255, 245, 158, 11), self.toggle_theme)

        toolbar_specs = [
            ("启动浏览器", argb(255, 34, 197, 94), self.action_placeholder),
            ("停止", argb(255, 245, 158, 11), self.action_placeholder),
            ("刷新", argb(255, 59, 130, 246), self.action_placeholder),
            ("打开后台", argb(255, 15, 118, 110), self.action_placeholder),
            ("同步Cookie", argb(255, 124, 58, 237), self.action_placeholder),
            ("切换代理", argb(255, 8, 145, 178), self.action_placeholder),
            ("更多操作", argb(255, 100, 116, 139), self.action_placeholder),
        ]
        for text, bg, action in toolbar_specs:
            self.toolbar_buttons.append(self.button(self.toolbar_panel, text, bg, action))

        tree_bg = argb(255, 255, 255, 255)
        tree_fg = argb(255, 31, 41, 55)
        self.tree = self.dll.CreateTreeView(self.tree_panel, 0, 0, 100, 100, tree_bg, tree_fg, ctypes.c_void_p())
        self.dll.ClearTree(self.tree)
        self.dll.SetTreeViewSidebarMode(self.tree, 1)
        self.dll.SetTreeViewRowHeight(self.tree, ctypes.c_float(34.0))
        self.dll.SetTreeViewItemSpacing(self.tree, ctypes.c_float(4.0))
        self.dll.SetTreeViewFont(self.tree, FONT_YAHEI_PTR, FONT_YAHEI_LEN, ctypes.c_float(12.0), 400, 0)
        self.dll.SetTreeViewCallback(self.tree, CALLBACK_NODE_SELECTED, self._tree_select_cb)
        self.seed_tree()

    def seed_tree(self) -> None:
        switch_root = self.add_root_node("环境切换", "🧭", "switch_root")
        for group_name, envs in self.GROUP_SEED.items():
            group_id = self.add_child_node(switch_root, group_name, "📁", "group", group_name)
            self.group_nodes[group_name] = group_id
            self.group_env_nodes[group_name] = []
            for name, domain, proxy, status, score in envs:
                node_id = self.add_environment(group_id, group_name, name, domain, proxy, status, score)
                self.group_env_nodes[group_name].append(node_id)
        for module_name, module_icon in self.MODULES.items():
            node_id = self.add_root_node(module_name, module_icon, "module", module_name)
            self.module_nodes[module_name] = node_id
        self.dll.ExpandAll(self.tree)
        self.env_counter = len(self.environments) + 1

    def add_root_node(self, text: str, icon: str, kind: str, key: str | None = None) -> int:
        text_ptr, text_len, text_keep = utf8_buffer(text)
        icon_ptr, icon_len, icon_keep = utf8_buffer(icon)
        self._tree_keep = getattr(self, "_tree_keep", [])
        self._tree_keep.extend([text_keep, icon_keep])
        node_id = self.dll.AddRootNode(self.tree, text_ptr, text_len, icon_ptr, icon_len)
        self.node_meta[node_id] = {"kind": kind, "key": key or text}
        return node_id

    def add_child_node(self, parent_id: int, text: str, icon: str, kind: str, key: str | None = None) -> int:
        text_ptr, text_len, text_keep = utf8_buffer(text)
        icon_ptr, icon_len, icon_keep = utf8_buffer(icon)
        self._tree_keep = getattr(self, "_tree_keep", [])
        self._tree_keep.extend([text_keep, icon_keep])
        node_id = self.dll.AddChildNode(self.tree, parent_id, text_ptr, text_len, icon_ptr, icon_len)
        self.node_meta[node_id] = {"kind": kind, "key": key or text}
        return node_id

    def add_environment(
        self,
        group_node_id: int,
        group_name: str,
        name: str,
        domain: str,
        proxy: str,
        status: str,
        score: int,
    ) -> int:
        node_id = self.add_child_node(group_node_id, name, "●", "environment", name)
        self.environments[node_id] = EnvironmentRecord(node_id, group_name, name, domain, proxy, status, score)
        self.apply_environment_node_color(node_id, status)
        return node_id

    def select_default_environment(self) -> None:
        if not self.environments:
            return
        first_node_id = next(iter(self.environments))
        self.dll.SetSelectedNode(self.tree, first_node_id)
        self.activate_node(first_node_id)

    def on_button_click(self, button_id: int, parent: HWND) -> None:
        action = self.button_actions.get(button_id)
        if action:
            action()

    def on_tree_selected(self, node_id: int, context: ctypes.c_void_p) -> None:
        self.activate_node(node_id)

    def on_window_resize(self, hwnd: HWND, width: int, height: int) -> None:
        if int(ctypes.cast(hwnd, ctypes.c_void_p).value or 0) != int(ctypes.cast(self.window, ctypes.c_void_p).value or 0):
            return
        if width <= 0 or height <= 0:
            return
        self.width = width
        self.height = height
        with RedrawScope(self.window):
            self.layout()

    def activate_node(self, node_id: int) -> None:
        meta = self.node_meta.get(node_id)
        if not meta:
            return
        self.current_node_id = node_id
        kind = meta["kind"]
        if kind == "environment":
            self.current_env_node = node_id
            self.toolbar_visible = True
            self.render_environment(self.environments[node_id])
            return
        if kind == "group":
            self.toolbar_visible = False
            self.render_group(meta["key"])
            return
        if kind == "module":
            self.toolbar_visible = False
            self.render_module(meta["key"])
            return
        self.toolbar_visible = False
        self.render_switch_root()

    def render_environment(self, env: EnvironmentRecord) -> None:
        info_main = f"当前环境：{env.name}   分组：{env.group_name}   状态：{env.status}   评分：{env.score}分"
        info_sub = f"域名：{env.domain}   代理：{env.proxy}"
        self.set_label_text(self.lbl_info_main, info_main)
        self.set_label_text(self.lbl_info_sub, info_sub)
        self.set_label_text(self.lbl_canvas_title, "浏览器主工作区")
        self.set_label_text(self.lbl_canvas_desc, f"当前环境 {env.name} 的浏览器区域占位壳子")
        self.set_label_text(self.lbl_canvas_hint, env.name)
        self.set_window_title(f"电商多账号浏览器 - {env.name}")

    def render_group(self, group_name: str) -> None:
        count = len(self.group_env_nodes.get(group_name, []))
        self.set_label_text(self.lbl_info_main, f"当前分组：{group_name}")
        self.set_label_text(self.lbl_info_sub, f"该分组下共有 {count} 个环境，点击环境节点可快速切换")
        self.set_label_text(self.lbl_canvas_title, f"{group_name} 工作区")
        self.set_label_text(self.lbl_canvas_desc, "这里可以继续扩展为分组总览、批量操作或分组统计页面")
        self.set_label_text(self.lbl_canvas_hint, group_name)
        self.set_window_title(f"电商多账号浏览器 - {group_name}")

    def render_module(self, module_name: str) -> None:
        self.set_label_text(self.lbl_info_main, f"当前模块：{module_name}")
        self.set_label_text(self.lbl_info_sub, "该区域先保留为模块工作区占位，后续可替换为真实业务页面")
        self.set_label_text(self.lbl_canvas_title, f"{module_name} 工作区")
        self.set_label_text(self.lbl_canvas_desc, "右侧区域已按草图保留为最大化内容区")
        self.set_label_text(self.lbl_canvas_hint, module_name)
        self.set_window_title(f"电商多账号浏览器 - {module_name}")

    def render_switch_root(self) -> None:
        self.set_label_text(self.lbl_info_main, "环境切换")
        self.set_label_text(self.lbl_info_sub, "左侧展开分组并点击环境节点，即可快速切换右侧工作区")
        self.set_label_text(self.lbl_canvas_title, "浏览器主工作区")
        self.set_label_text(self.lbl_canvas_desc, "当前未选中具体环境")
        self.set_label_text(self.lbl_canvas_hint, "请选择一个环境")
        self.set_window_title("电商多账号浏览器 - 草图版")

    def on_new_environment(self) -> None:
        group_name, group_node_id = self.resolve_target_group()
        name = f"{group_name.split()[0]}-环境{self.env_counter:02d}"
        domain = "new-env.local"
        proxy = f"Proxy-{self.env_counter:02d}"
        node_id = self.add_environment(group_node_id, group_name, name, domain, proxy, "待启动", 80)
        self.group_env_nodes.setdefault(group_name, []).append(node_id)
        self.env_counter += 1
        self.dll.ExpandNode(self.tree, group_node_id)
        self.dll.SetSelectedNode(self.tree, node_id)
        self.activate_node(node_id)

    def on_delete_environment(self) -> None:
        node_id = self.current_node_id
        if node_id not in self.environments:
            self.set_label_text(self.lbl_info_sub, "请先在左侧树形框中选中一个环境节点，再执行删除。")
            return
        env = self.environments.pop(node_id)
        self.group_env_nodes[env.group_name] = [item for item in self.group_env_nodes.get(env.group_name, []) if item != node_id]
        self.node_meta.pop(node_id, None)
        self.dll.RemoveNode(self.tree, node_id)
        fallback = self.group_env_nodes.get(env.group_name, [])
        next_node = fallback[0] if fallback else next(iter(self.environments), None)
        if next_node is not None:
            self.dll.SetSelectedNode(self.tree, next_node)
            self.activate_node(next_node)
        else:
            self.current_node_id = None
            self.current_env_node = None
            self.render_switch_root()

    def resolve_target_group(self) -> tuple[str, int]:
        if self.current_node_id is not None:
            meta = self.node_meta.get(self.current_node_id, {})
            if meta.get("kind") == "group":
                group_name = meta["key"]
                return group_name, self.current_node_id
            if meta.get("kind") == "environment":
                env = self.environments[self.current_node_id]
                return env.group_name, self.group_nodes[env.group_name]
        default_group = next(iter(self.group_nodes))
        return default_group, self.group_nodes[default_group]

    def action_placeholder(self) -> None:
        if self.current_env_node is None:
            self.set_label_text(self.lbl_canvas_desc, "当前不是环境页面，工具栏操作未执行。")
            return
        env = self.environments[self.current_env_node]
        self.set_label_text(self.lbl_canvas_desc, f"已触发 {env.name} 的草图操作，占位逻辑生效。")

    def toggle_theme(self) -> None:
        dark = not bool(self.dll.IsDarkMode())
        self.dll.SetDarkMode(BOOL(dark))
        with RedrawScope(self.window):
            self.apply_theme()
            self.layout()

    def apply_theme(self) -> None:
        dark = bool(self.dll.IsDarkMode())
        bg = argb(255, 244, 247, 251) if not dark else argb(255, 20, 23, 28)
        left_bg = argb(255, 255, 255, 255) if not dark else argb(255, 26, 30, 36)
        panel_bg = argb(255, 255, 255, 255) if not dark else argb(255, 33, 38, 46)
        browser_bg = argb(255, 238, 243, 249) if not dark else argb(255, 17, 20, 26)
        canvas_bg = argb(255, 249, 251, 253) if not dark else argb(255, 12, 15, 20)
        text = argb(255, 31, 41, 55) if not dark else argb(255, 241, 245, 249)
        muted = argb(255, 100, 116, 139) if not dark else argb(255, 148, 163, 184)
        titlebar = argb(255, 37, 99, 235) if not dark else argb(255, 15, 23, 42)
        accent = argb(255, 37, 99, 235) if not dark else argb(255, 96, 165, 250)

        self.dll.set_window_titlebar_color(self.window, titlebar)
        self.dll.SetTitleBarTextColor(self.window, argb(255, 255, 255, 255))
        self.dll.SetWindowBackgroundColor(self.window, bg)

        for panel, color in (
            (self.left_panel, left_bg),
            (self.workspace_panel, bg),
            (self.left_actions_panel, left_bg),
            (self.tree_panel, left_bg),
            (self.info_panel, panel_bg),
            (self.toolbar_panel, panel_bg),
            (self.browser_panel, browser_bg),
            (self.browser_frame, panel_bg),
            (self.browser_canvas, canvas_bg),
        ):
            self.dll.SetPanelBackgroundColor(panel, color)

        for label, fg, surface in (
            (self.lbl_left_title, text, left_bg),
            (self.lbl_info_main, text, panel_bg),
            (self.lbl_info_sub, muted, panel_bg),
            (self.lbl_canvas_title, text, panel_bg),
            (self.lbl_canvas_desc, muted, panel_bg),
            (self.lbl_canvas_hint, accent, canvas_bg),
        ):
            self.dll.SetLabelColor(label, fg, surface)

        self.paint_button(self.btn_new_env, argb(255, 37, 99, 235))
        self.paint_button(self.btn_delete_env, argb(255, 239, 68, 68))
        self.paint_button(self.btn_theme, argb(255, 245, 158, 11))
        self.dll.SetButtonRound(self.btn_new_env, 0)
        self.dll.SetButtonRound(self.btn_delete_env, 0)
        self.set_button_text(self.btn_theme, "☀️" if dark else "🌙")

        toolbar_colors = [
            argb(255, 34, 197, 94),
            argb(255, 245, 158, 11),
            argb(255, 59, 130, 246),
            argb(255, 15, 118, 110),
            argb(255, 124, 58, 237),
            argb(255, 8, 145, 178),
            argb(255, 100, 116, 139),
        ]
        for button_id, color in zip(self.toolbar_buttons, toolbar_colors):
            self.paint_button(button_id, color)

        self.dll.SetTreeViewBackgroundColor(self.tree, left_bg)
        self.dll.SetTreeViewTextColor(self.tree, text)
        self.dll.SetTreeViewSelectedBgColor(self.tree, accent)
        self.dll.SetTreeViewSelectedForeColor(self.tree, argb(255, 255, 255, 255))
        self.dll.SetTreeViewHoverBgColor(self.tree, mix_color(accent, left_bg, 0.88) if not dark else shift_color(left_bg, 8))

        for node_id, env in self.environments.items():
            self.apply_environment_node_color(node_id, env.status)
        for node_id, meta in self.node_meta.items():
            if meta["kind"] in {"switch_root", "group", "module"}:
                self.dll.SetNodeForeColor(self.tree, node_id, text)

    def apply_environment_node_color(self, node_id: int, status: str) -> None:
        status_colors = {
            "运行中": argb(255, 34, 197, 94),
            "空闲中": argb(255, 100, 116, 139),
            "待启动": argb(255, 245, 158, 11),
            "异常": argb(255, 239, 68, 68),
        }
        self.dll.SetNodeForeColor(self.tree, node_id, status_colors.get(status, argb(255, 31, 41, 55)))

    def layout(self) -> None:
        left_x = OUTER
        top_y = TITLE_BAR_HEIGHT + CONTENT_TOP_OFFSET
        left_h = max(300, self.height - top_y - OUTER)
        right_x = left_x + LEFT_W + GAP
        right_w = max(420, self.width - right_x - OUTER)
        right_h = left_h

        self.move(self.left_panel, left_x, top_y, LEFT_W, left_h)
        self.move(self.workspace_panel, right_x, top_y, right_w, right_h)

        self.move(self.left_actions_panel, 0, 0, LEFT_W, TOP_ACTION_H)
        self.move(self.tree_panel, 0, TOP_ACTION_H + 6, LEFT_W, left_h - TOP_ACTION_H - 6)
        self.move(self.tree, 12, 6, LEFT_W - 24, left_h - TOP_ACTION_H - 16)

        self.dll.SetLabelBounds(self.lbl_left_title, 14, 2, 120, 16)

        btn_h = 30
        new_w = 102
        delete_w = 102
        theme_w = 42
        row_y = 18
        gap = 10
        left_pad = 14
        right_pad = 14
        new_x = left_pad
        delete_x = new_x + new_w + gap
        theme_x = LEFT_W - right_pad - theme_w
        self.dll.SetButtonBounds(self.btn_new_env, left_x + new_x, top_y + row_y, new_w, btn_h)
        self.dll.SetButtonBounds(self.btn_delete_env, left_x + delete_x, top_y + row_y, delete_w, btn_h)
        self.dll.SetButtonBounds(self.btn_theme, left_x + theme_x, top_y + row_y, theme_w, btn_h)
        self.show_button(self.btn_new_env, True)
        self.show_button(self.btn_delete_env, True)
        self.show_button(self.btn_theme, True)

        self.move(self.info_panel, 0, 0, right_w, INFO_H)
        self.dll.SetLabelBounds(self.lbl_info_main, 16, 10, right_w - 32, 18)
        self.dll.SetLabelBounds(self.lbl_info_sub, 16, 28, right_w - 32, 16)

        toolbar_y = INFO_H + 10
        toolbar_h = TOOLBAR_H if self.toolbar_visible else 1
        self.move(self.toolbar_panel, 0, toolbar_y, right_w, toolbar_h)
        self.show_panel(self.toolbar_panel, self.toolbar_visible)

        browser_y = toolbar_y + (TOOLBAR_H + 10 if self.toolbar_visible else 10)
        browser_h = max(240, right_h - browser_y)
        self.move(self.browser_panel, 0, browser_y, right_w, browser_h)
        self.move(self.browser_frame, 0, 0, right_w, browser_h)
        self.move(self.browser_canvas, 16, 74, max(240, right_w - 32), max(160, browser_h - 90))
        self.dll.SetLabelBounds(self.lbl_canvas_title, 18, 18, right_w - 36, 24)
        self.dll.SetLabelBounds(self.lbl_canvas_desc, 18, 44, right_w - 36, 18)

        canvas_w = max(240, right_w - 32)
        canvas_h = max(160, browser_h - 90)
        hint_w = min(420, canvas_w - 40)
        hint_h = 30
        hint_x = max(20, (canvas_w - hint_w) // 2)
        hint_y = max(20, (canvas_h - hint_h) // 2 - 20)
        self.dll.SetLabelBounds(self.lbl_canvas_hint, hint_x, hint_y, hint_w, hint_h)

        if self.toolbar_visible:
            self.layout_toolbar_buttons(right_x, top_y + toolbar_y)
        else:
            for button_id in self.toolbar_buttons:
                self.show_button(button_id, False)

    def layout_toolbar_buttons(self, origin_x: int, origin_y: int) -> None:
        x = 16
        y = 7
        gap = 8
        button_w = [92, 72, 72, 86, 96, 88, 88]
        for button_id, item_w in zip(self.toolbar_buttons, button_w):
            self.dll.SetButtonBounds(button_id, origin_x + x, origin_y + y, item_w, 34)
            self.show_button(button_id, True)
            x += item_w + gap

    def label(self, parent: HWND, text: str, size: int, bold: bool) -> HWND:
        text_ptr, text_len, text_keep = utf8_buffer(text)
        self._label_keep = getattr(self, "_label_keep", [])
        self._label_keep.append(text_keep)
        return self.dll.CreateLabel(
            parent,
            0,
            0,
            100,
            20,
            text_ptr,
            text_len,
            argb(255, 31, 41, 55),
            argb(255, 255, 255, 255),
            FONT_YAHEI_PTR,
            FONT_YAHEI_LEN,
            size,
            1 if bold else 0,
            0,
            0,
            0,
            0,
        )

    def button(self, parent: HWND, text: str, bg: int, action=None) -> int:
        emoji_ptr, emoji_len, emoji_keep = utf8_buffer("")
        text_ptr, text_len, text_keep = utf8_buffer(text)
        self._button_keep = getattr(self, "_button_keep", [])
        self._button_keep.extend([emoji_keep, text_keep])
        button_id = self.dll.create_emoji_button_bytes(parent, emoji_ptr, emoji_len, text_ptr, text_len, 0, 0, 100, 34, bg)
        self.paint_button(button_id, bg)
        self.dll.ShowButton(button_id, 1)
        if action is not None:
            self.button_actions[button_id] = action
        return button_id

    def paint_button(self, button_id: int, bg: int) -> None:
        text_color = argb(255, 22, 34, 56) if color_brightness(bg) >= 175 else argb(255, 255, 255, 255)
        self.dll.SetButtonStyle(button_id, 0)
        self.dll.SetButtonSize(button_id, 1)
        self.dll.SetButtonRound(button_id, 10)
        self.dll.SetButtonBackgroundColor(button_id, bg)
        self.dll.SetButtonBorderColor(button_id, shift_color(bg, -18))
        self.dll.SetButtonTextColor(button_id, text_color)
        self.dll.SetButtonHoverColors(button_id, shift_color(bg, 10), shift_color(bg, 4), text_color)

    def set_button_text(self, button_id: int, text: str) -> None:
        ptr, ln, keep = utf8_buffer(text)
        self._set_button_keep = getattr(self, "_set_button_keep", [])
        self._set_button_keep.append(keep)
        self.dll.SetButtonText(button_id, ptr, ln)

    def set_label_text(self, hwnd: HWND, text: str) -> None:
        ptr, ln, keep = utf8_buffer(text)
        self._set_label_keep = getattr(self, "_set_label_keep", [])
        self._set_label_keep.append(keep)
        self.dll.SetLabelText(hwnd, ptr, ln)

    def set_window_title(self, title: str) -> None:
        ptr, ln, keep = utf8_buffer(title)
        self._set_title_keep = keep
        self.dll.set_window_title(self.window, ptr, ln)

    def move(self, hwnd: HWND, x: int, y: int, width: int, height: int) -> None:
        USER32.MoveWindow(int(ctypes.cast(hwnd, ctypes.c_void_p).value or 0), x, y, width, height, True)

    def show_button(self, button_id: int, visible: bool) -> None:
        self.dll.ShowButton(button_id, 1 if visible else 0)

    def show_panel(self, hwnd: HWND, visible: bool) -> None:
        USER32.ShowWindow(int(ctypes.cast(hwnd, ctypes.c_void_p).value or 0), SW_SHOW if visible else SW_HIDE)


def main() -> None:
    EnvironmentWorkspaceSketchApp().run()


if __name__ == "__main__":
    main()

