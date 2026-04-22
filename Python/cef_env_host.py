from __future__ import annotations

import ctypes
import json
import logging
import struct
import sys
import threading
import time
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Any, Protocol


SW_SHOW = 5
SW_HIDE = 0
USER32 = ctypes.windll.user32

SUPPORTED_PLATFORM = "win32"
SUPPORTED_PYTHON = (3, 9)
CEF_REQUIRED_VERSION = "66.1"


try:
    from cefpython3 import cefpython as cef  # type: ignore[import-not-found]
except Exception as exc:  # pragma: no cover - exercised only when dependency is missing
    cef = None
    CEF_IMPORT_ERROR = exc
else:
    CEF_IMPORT_ERROR = None


@dataclass
class BrowserSnapshot:
    env_id: int
    browser_flag: str
    state: int = 0
    state_text: str = "未创建"
    url: str = ""
    title: str = ""
    is_loading: bool = False
    is_visible: bool = False
    cache_path: str = ""
    cookie_path: str = ""
    last_error: str = ""
    proxy_config: dict[str, Any] | None = None
    last_activated_at: float = 0.0

    def copy(self) -> "BrowserSnapshot":
        return BrowserSnapshot(**asdict(self))


class BrowserEventSink(Protocol):
    def on_title_change(self, env_id: int, title: str) -> None:
        ...

    def on_url_change(self, env_id: int, url: str) -> None:
        ...

    def on_loading_state(self, env_id: int, is_loading: bool) -> None:
        ...

    def on_browser_closed(self, env_id: int) -> None:
        ...

    def on_browser_error(self, env_id: int, message: str) -> None:
        ...


@dataclass
class _BrowserSession:
    env_id: int
    browser_flag: str
    parent_hwnd: int = 0
    cache_path: str = ""
    cookie_path: str = ""
    proxy_config: dict[str, Any] | None = None
    browser: Any = None
    handler: Any = None
    created: bool = False
    closing: bool = False
    bounds: tuple[int, int, int, int] = (0, 0, 1, 1)
    snapshot: BrowserSnapshot = field(default_factory=lambda: BrowserSnapshot(env_id=0, browser_flag=""))


class _CookieVisitor:
    def __init__(self) -> None:
        self.cookies: list[dict[str, Any]] = []
        self.done = threading.Event()

    def Visit(self, cookie: Any, count: int, total: int, delete_cookie_out: Any) -> bool:  # noqa: N802
        self.cookies.append(
            {
                "name": _safe_call(cookie, "GetName", default=""),
                "value": _safe_call(cookie, "GetValue", default=""),
                "domain": _safe_call(cookie, "GetDomain", default=""),
                "path": _safe_call(cookie, "GetPath", default=""),
                "secure": bool(_safe_call(cookie, "GetSecure", default=False)),
                "httponly": bool(_safe_call(cookie, "GetHttpOnly", default=False)),
                "has_expires": bool(_safe_call(cookie, "HasExpires", default=False)),
                "creation": _cookie_time_repr(_safe_call(cookie, "GetCreation", default=None)),
                "last_access": _cookie_time_repr(_safe_call(cookie, "GetLastAccess", default=None)),
                "expires": _cookie_time_repr(_safe_call(cookie, "GetExpires", default=None)),
            }
        )
        if count + 1 >= total:
            self.done.set()
        return True


class _BrowserClientHandler:
    def __init__(self, manager: "CefEnvHostManager", env_id: int) -> None:
        self.manager = manager
        self.env_id = env_id

    def OnAddressChange(self, browser: Any, frame: Any, url: str) -> None:  # noqa: N802
        if frame is not None and hasattr(frame, "IsMain") and not frame.IsMain():
            return
        self.manager._handle_address_change(self.env_id, url)

    def OnTitleChange(self, browser: Any, title: str) -> None:  # noqa: N802
        self.manager._handle_title_change(self.env_id, title)

    def OnLoadingStateChange(  # noqa: N802
        self,
        browser: Any,
        is_loading: bool,
        can_go_back: bool,
        can_go_forward: bool,
    ) -> None:
        self.manager._handle_loading_state(self.env_id, is_loading)

    def OnLoadError(  # noqa: N802
        self,
        browser: Any,
        frame: Any,
        error_code: int,
        error_text: str,
        failed_url: str,
    ) -> None:
        if frame is not None and hasattr(frame, "IsMain") and not frame.IsMain():
            return
        self.manager._handle_browser_error(self.env_id, f"{error_text} ({failed_url})")

    def OnBeforeClose(self, browser: Any) -> None:  # noqa: N802
        self.manager._handle_before_close(self.env_id)


class CefEnvHostManager:
    def __init__(self, event_sink: BrowserEventSink, base_dir: Path | None = None) -> None:
        self.event_sink = event_sink
        self.base_dir = Path(base_dir) if base_dir is not None else Path.cwd()
        self._sessions: dict[int, _BrowserSession] = {}
        self._initialized = False
        self._main_hwnd = 0
        self._lock = threading.RLock()
        self._logger = logging.getLogger("cef_env_host")
        self._logger.setLevel(logging.INFO)
        self._configure_logging()

    def initialize(self, main_hwnd: int) -> None:
        with self._lock:
            if self._initialized:
                return
            _ensure_supported_runtime()
            if cef is None:
                raise RuntimeError(format_runtime_error_message())
            self._main_hwnd = int(main_hwnd)
            logs_dir = self.base_dir / "logs"
            logs_dir.mkdir(parents=True, exist_ok=True)
            settings = {
                "multi_threaded_message_loop": True,
                "persist_session_cookies": True,
                "persist_user_preferences": True,
                "log_file": str(logs_dir / "cefpython.log"),
                "log_severity": getattr(cef, "LOGSEVERITY_INFO", 1),
            }
            switches = {
                "disable-features": "RendererCodeIntegrity",
            }
            sys.excepthook = cef.ExceptHook
            cef.Initialize(settings=settings, switches=switches)
            self._initialized = True
            self._logger.info("CEF initialized for hwnd=%s", self._main_hwnd)

    def shutdown(self) -> None:
        with self._lock:
            if not self._initialized or cef is None:
                return
            self.close_all()
            cef.Shutdown()
            self._initialized = False
            self._logger.info("CEF shutdown complete")

    def ensure_browser(
        self,
        env_id: int,
        parent_hwnd: int,
        start_url: str,
        cache_path: str,
        cookie_path: str,
        browser_flag: str,
        proxy_config: dict[str, Any] | None = None,
    ) -> None:
        self._require_initialized()
        hwnd = int(parent_hwnd)
        if hwnd <= 0:
            raise RuntimeError("浏览器宿主句柄无效。")
        with self._lock:
            session = self._sessions.get(env_id)
            if session and session.browser is not None and not session.closing:
                session.parent_hwnd = hwnd
                session.proxy_config = proxy_config
                session.snapshot.proxy_config = proxy_config
                session.snapshot.cache_path = cache_path
                session.snapshot.cookie_path = cookie_path
                session.snapshot.last_activated_at = time.time()
                self.resize(env_id, 0, 0, *_get_client_size(hwnd))
                self.show(env_id)
                return
        self._call_on_ui_thread(
            self._ensure_browser_on_ui_thread,
            env_id,
            hwnd,
            start_url,
            cache_path,
            cookie_path,
            browser_flag,
            proxy_config,
        )
        self._logger.info("Browser created for env_id=%s url=%s", env_id, start_url)

    def show(self, env_id: int) -> None:
        with self._lock:
            session = self._sessions.get(env_id)
            if session is None:
                return
            session.snapshot.is_visible = True
            session.snapshot.last_activated_at = time.time()
            hwnd = session.parent_hwnd
        USER32.ShowWindow(hwnd, SW_SHOW)
        self._call_on_ui_thread(self._call_browser_visibility, self._sessions.get(env_id).browser if env_id in self._sessions else None, False)

    def hide(self, env_id: int) -> None:
        with self._lock:
            session = self._sessions.get(env_id)
            if session is None:
                return
            session.snapshot.is_visible = False
            hwnd = session.parent_hwnd
        USER32.ShowWindow(hwnd, SW_HIDE)
        self._call_on_ui_thread(self._call_browser_visibility, self._sessions.get(env_id).browser if env_id in self._sessions else None, True)

    def resize(self, env_id: int, x: int, y: int, width: int, height: int) -> None:
        with self._lock:
            session = self._sessions.get(env_id)
            if session is None:
                return
            width = max(1, int(width))
            height = max(1, int(height))
            session.bounds = (x, y, width, height)
            browser = session.browser
        self._call_on_ui_thread(_resize_browser, browser, width, height)

    def navigate(self, env_id: int, url: str) -> None:
        with self._lock:
            session = self._sessions.get(env_id)
            if session is None or session.browser is None:
                raise RuntimeError("浏览器尚未创建，无法导航。")
            session.snapshot.url = url
            session.snapshot.last_activated_at = time.time()
            browser = session.browser
        self._post_on_ui_thread(self._navigate_on_ui_thread, browser, url)

    def reload(self, env_id: int) -> None:
        with self._lock:
            session = self._sessions.get(env_id)
            if session is None or session.browser is None:
                raise RuntimeError("浏览器尚未创建，无法刷新。")
            browser = session.browser
        if hasattr(browser, "ReloadIgnoreCache"):
            self._post_on_ui_thread(browser.ReloadIgnoreCache)
        else:
            self._post_on_ui_thread(browser.Reload)

    def close(self, env_id: int) -> None:
        with self._lock:
            session = self._sessions.get(env_id)
            if session is None:
                return
            session.closing = True
            session.snapshot.state = 4
            session.snapshot.state_text = "关闭中"
            session.snapshot.is_visible = False
            USER32.ShowWindow(session.parent_hwnd, SW_HIDE)
            browser = session.browser
            try:
                if browser is None:
                    self._finalize_session(env_id, state=5)
                    return
                if hasattr(browser, "CloseBrowser"):
                    self._call_on_ui_thread(browser.CloseBrowser, True)
                elif hasattr(browser, "GetHost"):
                    host = browser.GetHost()
                    if host is not None and hasattr(host, "CloseBrowser"):
                        self._call_on_ui_thread(host.CloseBrowser, True)
                    else:
                        self._finalize_session(env_id, state=5)
                else:
                    self._finalize_session(env_id, state=5)
            except Exception as exc:
                self._logger.exception("Failed to close browser env_id=%s", env_id)
                self._finalize_session(env_id, state=6, last_error=str(exc))
                raise RuntimeError(f"关闭浏览器失败：{exc}") from exc
            else:
                if env_id in self._sessions and self._sessions[env_id].browser is not None:
                    self._finalize_session(env_id, state=5)

    def close_all(self) -> None:
        for env_id in list(self._sessions):
            try:
                self.close(env_id)
            except Exception:
                self._logger.exception("close_all failed for env_id=%s", env_id)

    def has_browser(self, env_id: int) -> bool:
        with self._lock:
            session = self._sessions.get(env_id)
            return bool(session and session.browser is not None and not session.closing)

    def get_snapshot(self, env_id: int) -> BrowserSnapshot:
        with self._lock:
            session = self._sessions.get(env_id)
            if session is None:
                return BrowserSnapshot(env_id=env_id, browser_flag=f"env_{env_id}")
            return session.snapshot.copy()

    def export_cookies(self, env_id: int, target_path: str | None = None, timeout: float = 5.0) -> Path:
        with self._lock:
            session = self._sessions.get(env_id)
            if session is None:
                raise RuntimeError("浏览器尚未启动，无法导出 Cookie。")
            cookie_manager = cef.CookieManager.GetGlobalManager()
        if cookie_manager is None or not hasattr(cookie_manager, "VisitAllCookies"):
            raise RuntimeError("当前 CEF 运行时不支持导出 Cookie。")
        visitor = _CookieVisitor()
        cookie_manager.VisitAllCookies(visitor)
        visitor.done.wait(timeout)
        export_path = Path(target_path or session.cookie_path)
        export_path.parent.mkdir(parents=True, exist_ok=True)
        export_path.write_text(
            json.dumps(
                {
                    "env_id": env_id,
                    "browser_flag": session.browser_flag,
                    "exported_at": time.strftime("%Y-%m-%d %H:%M:%S"),
                    "cookies": visitor.cookies,
                },
                ensure_ascii=False,
                indent=2,
            ),
            encoding="utf-8",
        )
        return export_path

    def _configure_logging(self) -> None:
        if self._logger.handlers:
            return
        logs_dir = self.base_dir / "logs"
        logs_dir.mkdir(parents=True, exist_ok=True)
        handler = logging.FileHandler(logs_dir / "cef_env_host.log", encoding="utf-8")
        formatter = logging.Formatter("%(asctime)s [%(levelname)s] %(message)s")
        handler.setFormatter(formatter)
        self._logger.addHandler(handler)

    def _require_initialized(self) -> None:
        if not self._initialized or cef is None:
            raise RuntimeError(format_runtime_error_message())

    def _call_on_ui_thread(self, func: Any, *args: Any, timeout: float = 10.0) -> Any:
        self._require_initialized()
        if cef.IsThread(cef.TID_UI):
            return func(*args)

        done = threading.Event()
        result: dict[str, Any] = {}

        def runner() -> None:
            try:
                result["value"] = func(*args)
            except Exception as exc:
                result["error"] = exc
            finally:
                done.set()

        cef.PostTask(cef.TID_UI, runner)
        if not done.wait(timeout):
            raise RuntimeError("等待 CEF UI 线程执行超时。")
        if "error" in result:
            raise result["error"]
        return result.get("value")

    def _post_on_ui_thread(self, func: Any, *args: Any) -> None:
        self._require_initialized()
        if cef.IsThread(cef.TID_UI):
            func(*args)
            return
        cef.PostTask(cef.TID_UI, func, *args)

    def _navigate_on_ui_thread(self, browser: Any, url: str) -> None:
        assert cef is not None
        assert cef.IsThread(cef.TID_UI)
        if browser is None:
            return
        frame = browser.GetMainFrame() if hasattr(browser, "GetMainFrame") else None
        if frame is not None and hasattr(frame, "LoadUrl"):
            frame.LoadUrl(url)
            return
        if hasattr(browser, "LoadUrl"):
            browser.LoadUrl(url)

    def _ensure_browser_on_ui_thread(
        self,
        env_id: int,
        hwnd: int,
        start_url: str,
        cache_path: str,
        cookie_path: str,
        browser_flag: str,
        proxy_config: dict[str, Any] | None,
    ) -> None:
        assert cef is not None
        assert cef.IsThread(cef.TID_UI)
        cache_dir = Path(cache_path)
        cache_dir.mkdir(parents=True, exist_ok=True)
        cookie_file = Path(cookie_path)
        cookie_file.parent.mkdir(parents=True, exist_ok=True)
        width, height = _get_client_size(hwnd)
        window_info = cef.WindowInfo()
        window_info.SetAsChild(hwnd, [0, 0, width, height])
        browser = cef.CreateBrowserSync(
            window_info=window_info,
            url=start_url,
            settings={},
        )
        handler = _BrowserClientHandler(self, env_id)
        browser.SetClientHandler(handler)

        snapshot = BrowserSnapshot(
            env_id=env_id,
            browser_flag=browser_flag,
            state=2,
            state_text="运行中",
            url=start_url,
            title=start_url,
            is_loading=False,
            is_visible=True,
            cache_path=str(cache_dir),
            cookie_path=str(cookie_file),
            proxy_config=proxy_config,
            last_activated_at=time.time(),
            last_error="cefpython3 66.1 不支持 RequestContext；当前原型使用全局 Cookie/缓存上下文。",
        )
        with self._lock:
            self._sessions[env_id] = _BrowserSession(
                env_id=env_id,
                browser_flag=browser_flag,
                parent_hwnd=hwnd,
                cache_path=str(cache_dir),
                cookie_path=str(cookie_file),
                proxy_config=proxy_config,
                browser=browser,
                handler=handler,
                created=True,
                closing=False,
                bounds=(0, 0, width, height),
                snapshot=snapshot,
            )
        self._apply_proxy_placeholder_notice(env_id, proxy_config)
        _resize_browser(browser, width, height)
        self._call_browser_visibility(browser, False)

    def _handle_title_change(self, env_id: int, title: str) -> None:
        with self._lock:
            session = self._sessions.get(env_id)
            if session is None:
                return
            session.snapshot.title = title
        self.event_sink.on_title_change(env_id, title)

    def _handle_address_change(self, env_id: int, url: str) -> None:
        with self._lock:
            session = self._sessions.get(env_id)
            if session is None:
                return
            session.snapshot.url = url
        self.event_sink.on_url_change(env_id, url)

    def _handle_loading_state(self, env_id: int, is_loading: bool) -> None:
        with self._lock:
            session = self._sessions.get(env_id)
            if session is None:
                return
            session.snapshot.is_loading = is_loading
            if session.snapshot.state not in {5, 6}:
                session.snapshot.state = 2
                session.snapshot.state_text = "运行中" if not is_loading else "加载中"
        self.event_sink.on_loading_state(env_id, is_loading)

    def _handle_before_close(self, env_id: int) -> None:
        self._finalize_session(env_id, state=5)

    def _handle_browser_error(self, env_id: int, message: str) -> None:
        self._logger.error("Browser error env_id=%s: %s", env_id, message)
        self._finalize_session(env_id, state=6, last_error=message)
        self.event_sink.on_browser_error(env_id, message)

    def _finalize_session(self, env_id: int, state: int, last_error: str = "") -> None:
        with self._lock:
            session = self._sessions.pop(env_id, None)
            if session is None:
                return
            snapshot = session.snapshot
            snapshot.state = state
            snapshot.state_text = {
                4: "关闭中",
                5: "已关闭",
                6: "异常",
            }.get(state, snapshot.state_text)
            snapshot.is_visible = False
            snapshot.is_loading = False
            if last_error:
                snapshot.last_error = last_error
            if session.browser is not None:
                try:
                    host = session.browser.GetHost() if hasattr(session.browser, "GetHost") else None
                    if host is not None and hasattr(host, "ParentWindowWillClose"):
                        host.ParentWindowWillClose()
                except Exception:
                    self._logger.exception("ParentWindowWillClose failed for env_id=%s", env_id)
            session.browser = None
            session.handler = None
        self.event_sink.on_browser_closed(env_id)

    def _call_browser_visibility(self, browser: Any, hidden: bool) -> None:
        if browser is None:
            return
        try:
            if hasattr(browser, "WasHidden"):
                browser.WasHidden(hidden)
            if not hidden and hasattr(browser, "SetFocus"):
                browser.SetFocus(True)
        except Exception:
            self._logger.exception("Browser visibility call failed")

    def _apply_proxy_placeholder_notice(self, env_id: int, proxy_config: dict[str, Any] | None) -> None:
        if not proxy_config:
            return
        note = "cefpython3 当前以单进程原型方式接入，逐实例独立代理只保留接口和状态，不保证同进程内真正生效。"
        with self._lock:
            session = self._sessions.get(env_id)
            if session is None:
                return
            session.snapshot.last_error = note
        self.event_sink.on_browser_error(env_id, note)


def format_runtime_error_message() -> str:
    parts = [
        "Python 版 CEF 原型仅支持 Windows x64 + Python 3.10 x64。",
        f"当前解释器: {sys.version.split()[0]} ({'64' if struct.calcsize('P') * 8 == 64 else '32'} 位)。",
    ]
    if sys.platform != SUPPORTED_PLATFORM:
        parts.append("当前平台不是 Windows。")
    if CEF_IMPORT_ERROR is not None:
        parts.append(
            f"未检测到 cefpython3。请使用 Python 3.9 x64 执行 `python -m pip install cefpython3=={CEF_REQUIRED_VERSION}`。"
        )
    return "\n".join(parts)


def _ensure_supported_runtime() -> None:
    if sys.platform != SUPPORTED_PLATFORM:
        raise RuntimeError(format_runtime_error_message())
    if struct.calcsize("P") * 8 != 64:
        raise RuntimeError(format_runtime_error_message())
    if sys.version_info[:2] != SUPPORTED_PYTHON:
        raise RuntimeError(format_runtime_error_message())


def _safe_call(obj: Any, method_name: str, default: Any = None) -> Any:
    if obj is None or not hasattr(obj, method_name):
        return default
    try:
        return getattr(obj, method_name)()
    except Exception:
        return default


def _cookie_time_repr(value: Any) -> str | None:
    if value is None:
        return None
    if hasattr(value, "ToDoubleT"):
        try:
            return str(value.ToDoubleT())
        except Exception:
            return None
    return str(value)


def _get_client_size(hwnd: int) -> tuple[int, int]:
    rect = ctypes.wintypes.RECT()
    if not USER32.GetClientRect(hwnd, ctypes.byref(rect)):
        return (1, 1)
    return (max(1, rect.right - rect.left), max(1, rect.bottom - rect.top))


def _resize_browser(browser: Any, width: int, height: int) -> None:
    if browser is None:
        return
    try:
        if hasattr(browser, "GetWindowHandle"):
            hwnd = browser.GetWindowHandle()
            if hwnd:
                USER32.MoveWindow(hwnd, 0, 0, width, height, True)
        elif hasattr(browser, "SetBounds"):
            browser.SetBounds(0, 0, width, height)
        if hasattr(browser, "NotifyMoveOrResizeStarted"):
            browser.NotifyMoveOrResizeStarted()
        elif hasattr(browser, "GetHost"):
            host = browser.GetHost()
            if host is not None and hasattr(host, "NotifyMoveOrResizeStarted"):
                host.NotifyMoveOrResizeStarted()
    except Exception:
        logging.getLogger("cef_env_host").exception("Browser resize failed")
