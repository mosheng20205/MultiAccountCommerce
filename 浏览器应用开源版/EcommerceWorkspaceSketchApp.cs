using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using EmojiWindowDemo;
using FBroSharp;
using FBroSharp.DataType;
using FBroSharp.Lib;
using FBroSharp.Value;

namespace EmojiWindowEcommerceWorkspaceSketchDemo
{
    internal sealed class EcommerceWorkspaceSketchApp
    {
        private sealed class EnvironmentRecord
        {
            public int EnvId { get; set; }
            public int NodeId { get; set; }
            public string GroupName { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Domain { get; set; } = string.Empty;
            public string Proxy { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public int Score { get; set; }
            public string StartUrl { get; set; } = string.Empty;
            public string CachePath { get; set; } = string.Empty;
            public string BrowserFlag { get; set; } = string.Empty;
            public string CookiePath { get; set; } = string.Empty;
            public IntPtr HostPanel { get; set; }
            public IntPtr AddressPanel { get; set; }
            public IntPtr AddressEdit { get; set; }
            public IntPtr BrowserView { get; set; }
            public int BrowserState { get; set; }
            public bool KeepAlive { get; set; }
            public bool Visible { get; set; }
            public string LastUrl { get; set; } = string.Empty;
            public string LastTitle { get; set; } = string.Empty;
            public EnvironmentBrowserEvent BrowserEventHandler { get; set; }
        }

        private sealed class NodeMeta
        {
            public string Kind { get; set; } = string.Empty;
            public string Key { get; set; } = string.Empty;
            public int? EnvId { get; set; }
        }

        private static class NativeExtras
        {
            private const uint WmSetRedraw = 0x000B;
            private const uint RdwInvalidate = 0x0001;
            private const uint RdwErase = 0x0004;
            private const uint RdwAllChildren = 0x0080;
            private const uint RdwFrame = 0x0400;
            private const uint RdwUpdateNow = 0x0100;

            [DllImport("emoji_window.dll", CallingConvention = CallingConvention.StdCall)]
            public static extern int ClearTree(IntPtr hTreeView);

            [DllImport("emoji_window.dll", CallingConvention = CallingConvention.StdCall)]
            public static extern int RemoveNode(IntPtr hTreeView, int nodeId);

            [DllImport("emoji_window.dll", CallingConvention = CallingConvention.StdCall)]
            public static extern int ExpandNode(IntPtr hTreeView, int nodeId);

            [DllImport("emoji_window.dll", CallingConvention = CallingConvention.StdCall)]
            public static extern int SetNodeForeColor(IntPtr hTreeView, int nodeId, uint color);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool MoveWindow(IntPtr hwnd, int x, int y, int width, int height, [MarshalAs(UnmanagedType.Bool)] bool repaint);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool ShowWindow(IntPtr hwnd, int cmdShow);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool DestroyWindow(IntPtr hwnd);

            [DllImport("user32.dll")]
            public static extern uint GetDpiForWindow(IntPtr hwnd);

            [DllImport("user32.dll", SetLastError = true)]
            private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

            public static void SetRedraw(IntPtr hwnd, bool enabled)
            {
                if (hwnd == IntPtr.Zero)
                {
                    return;
                }

                SendMessage(hwnd, WmSetRedraw, enabled ? new IntPtr(1) : IntPtr.Zero, IntPtr.Zero);
            }

            public static void RefreshWindow(IntPtr hwnd)
            {
                if (hwnd == IntPtr.Zero)
                {
                    return;
                }

                RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero, RdwInvalidate | RdwErase | RdwFrame | RdwAllChildren | RdwUpdateNow);
            }
        }

        private sealed class RedrawScope : IDisposable
        {
            private readonly IntPtr _hwnd;

            public RedrawScope(IntPtr hwnd)
            {
                _hwnd = hwnd;
                NativeExtras.SetRedraw(_hwnd, false);
            }

            public void Dispose()
            {
                NativeExtras.SetRedraw(_hwnd, true);
                NativeExtras.RefreshWindow(_hwnd);
            }
        }

        private const int SwHide = 0;
        private const int SwShow = 5;
        private const int CallbackNodeSelected = 1;
        private const int VkReturn = 13;
        private const int WindowWidth = 1480;
        private const int WindowHeight = 920;
        private const int TitleBarHeight = 30;
        private const int ContentTopOffset = 14;
        private const int Outer = 16;
        private const int LeftWidth = 320;
        private const int Gap = 12;
        private const int TopActionHeight = 56;
        private const int InfoHeight = 52;
        private const int ToolbarHeight = 48;
        private const int LeftTitleFontSize = 12;
        private const int InfoMainFontSize = 13;
        private const int InfoSubFontSize = 11;
        private const int EditFontSize = 12;
        private const float TreeRowHeight = 34.0f;
        private const float TreeItemSpacing = 4.0f;
        private const float TreeFontSize = 12.0f;

        private readonly byte[] _fontYaHei = EmojiWindowNative.ToUtf8("Microsoft YaHei UI");
        private readonly byte[] _fontSegoe = EmojiWindowNative.ToUtf8("Segoe UI");
        private readonly Dictionary<int, Action> _buttonActions = new Dictionary<int, Action>();
        private readonly Dictionary<int, NodeMeta> _nodeMeta = new Dictionary<int, NodeMeta>();
        private readonly Dictionary<string, int> _groupNodes = new Dictionary<string, int>();
        private readonly Dictionary<string, List<int>> _groupEnvironmentIds = new Dictionary<string, List<int>>();
        private readonly Dictionary<string, int> _moduleNodes = new Dictionary<string, int>();
        private readonly Dictionary<int, EnvironmentRecord> _environments = new Dictionary<int, EnvironmentRecord>();
        private readonly Dictionary<int, int> _nodeToEnvId = new Dictionary<int, int>();
        private readonly Dictionary<IntPtr, int> _editToEnvId = new Dictionary<IntPtr, int>();
        private readonly List<int> _toolbarButtons = new List<int>();
        private readonly int[] _toolbarWidths = { 92, 72, 72, 86, 96, 88, 88 };
        private readonly uint[] _toolbarColors =
        {
            Argb(255, 34, 197, 94),
            Argb(255, 245, 158, 11),
            Argb(255, 59, 130, 246),
            Argb(255, 15, 118, 110),
            Argb(255, 124, 58, 237),
            Argb(255, 8, 145, 178),
            Argb(255, 100, 116, 139),
        };

        private readonly Dictionary<string, (string Name, string Domain, string Proxy, string Status, int Score)[]> _groupSeed =
            new Dictionary<string, (string Name, string Domain, string Proxy, string Status, int Score)[]>
            {
                ["Amazon 店群"] = new[]
                {
                    ("美区-环境01", "amazon.com", "US-Proxy-01", "运行中", 92),
                    ("美区-环境02", "amazon.com", "US-Proxy-02", "空闲中", 88),
                },
                ["TikTok 店群"] = new[]
                {
                    ("TK-环境01", "seller-us.tiktok.com", "US-Proxy-09", "运行中", 95),
                },
                ["独立站"] = new[]
                {
                    ("独立站-环境01", "shop.example.com", "JP-Proxy-03", "待启动", 84),
                },
            };

        private readonly Dictionary<string, string> _moduleIcons = new Dictionary<string, string>
        {
            ["代理管理"] = "🌐",
            ["分组管理"] = "🗂️",
            ["RPA自动化"] = "🤖",
            ["插件中心"] = "🧩",
            ["团队协作"] = "👥",
            ["系统设置"] = "⚙️",
        };

        private readonly EmojiWindowNative.ButtonClickCallback _buttonClickCallback;
        private readonly EmojiWindowNative.TreeNodeCallback _treeNodeCallback;
        private readonly EmojiWindowNative.WindowResizeCallback _windowResizeCallback;
        private readonly EmojiWindowNative.EditBoxKeyCallback _editKeyCallback;

        private IntPtr _window;
        private IntPtr _leftPanel;
        private IntPtr _leftActionsPanel;
        private IntPtr _treePanel;
        private IntPtr _workspacePanel;
        private IntPtr _infoPanel;
        private IntPtr _toolbarPanel;
        private IntPtr _browserPanel;
        private IntPtr _browserFrame;
        private IntPtr _browserCanvas;
        private IntPtr _tree;
        private IntPtr _lblLeftTitle;
        private IntPtr _lblInfoMain;
        private IntPtr _lblInfoSub;

        private int _btnNewEnv;
        private int _btnDeleteEnv;
        private int _btnTheme;
        private int _width = WindowWidth;
        private int _height = WindowHeight;
        private uint _dpi = 96;
        private float _dpiScale = 1.0f;
        private int? _currentNodeId;
        private int? _currentEnvId;
        private int? _currentVisibleEnvId;
        private bool _toolbarVisible = true;
        private int _envCounter = 1;
        private int _nextEnvId = 1001;
        private int _browserCanvasWidth;
        private int _browserCanvasHeight;

        public EcommerceWorkspaceSketchApp()
        {
            _buttonClickCallback = OnButtonClick;
            _treeNodeCallback = OnTreeSelected;
            _windowResizeCallback = OnResize;
            _editKeyCallback = OnAddressKey;
        }

        private void RefreshDpiScale()
        {
            if (_window == IntPtr.Zero)
            {
                _dpi = 96;
                _dpiScale = 1.0f;
                return;
            }

            uint dpi = NativeExtras.GetDpiForWindow(_window);
            if (dpi == 0)
            {
                dpi = 96;
            }

            _dpi = dpi;
            _dpiScale = _dpi / 96.0f;
        }

        private int Scale(int value)
        {
            return Math.Max(1, (int)Math.Round(value * _dpiScale));
        }

        private float Scale(float value)
        {
            return value * _dpiScale;
        }

        private int GetRuntimeTitleBarHeight()
        {
            int runtime = _window != IntPtr.Zero ? EmojiWindowNative.GetCustomTitleBarHeight(_window) : 0;
            int fallback = Scale(TitleBarHeight);
            return Math.Max(fallback, runtime);
        }

        public void Run()
        {
            CreateWindow();
            using (new RedrawScope(_window))
            {
                CreateControls();
                ApplyTheme();
                SelectDefaultEnvironment();
                Layout();
            }

            EmojiWindowNative.set_button_click_callback(_buttonClickCallback);
            EmojiWindowNative.SetWindowResizeCallback(_windowResizeCallback);
            EmojiWindowNative.ShowEmojiWindow(_window, 1);
            EmojiWindowNative.set_message_loop_main_window(_window);
            EmojiWindowNative.run_message_loop();
        }

        private void CreateWindow()
        {
            using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero))
            {
                _dpi = Math.Max(96u, (uint)Math.Round(graphics.DpiX));
                _dpiScale = _dpi / 96.0f;
            }

            _width = Scale(WindowWidth);
            _height = Scale(WindowHeight);
            byte[] title = U("电商多账号浏览器 - 草图版");
            _window = EmojiWindowNative.create_window_bytes_ex(title, title.Length, -1, -1, _width, _height, Argb(255, 37, 99, 235), Argb(255, 244, 247, 251));
            if (_window == IntPtr.Zero)
            {
                throw new InvalidOperationException("create_window_bytes_ex failed.");
            }

            RefreshDpiScale();
            _width = Scale(WindowWidth);
            _height = Scale(WindowHeight);
            EmojiWindowNative.SetWindowBounds(_window, -1, -1, _width, _height);

            EmojiWindowNative.SetTitleBarTextColor(_window, Argb(255, 255, 255, 255));

            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "favicon.ico");
            if (!File.Exists(iconPath))
            {
                iconPath = @"T:\易语言源码\API创建窗口\emoji_window_cpp\examples\Csharp\EmojiWindowEcommerceMultiAccountDemo\favicon.ico";
            }
            if (!File.Exists(iconPath))
            {
                iconPath = @"T:\易语言源码\API创建窗口\emoji_window_cpp\examples\Python\谷歌.ico";
            }
            if (File.Exists(iconPath))
            {
                byte[] icon = File.ReadAllBytes(iconPath);
                EmojiWindowNative.set_window_icon_bytes(_window, icon, icon.Length);
            }
        }

        private void CreateControls()
        {
            _leftPanel = EmojiWindowNative.CreatePanel(_window, 0, 0, 100, 100, Argb(255, 255, 255, 255));
            _leftActionsPanel = EmojiWindowNative.CreatePanel(_leftPanel, 0, 0, 100, Scale(TopActionHeight), Argb(255, 255, 255, 255));
            _treePanel = EmojiWindowNative.CreatePanel(_leftPanel, 0, 0, 100, 100, Argb(255, 255, 255, 255));
            _workspacePanel = EmojiWindowNative.CreatePanel(_window, 0, 0, 100, 100, Argb(255, 244, 247, 251));
            _infoPanel = EmojiWindowNative.CreatePanel(_workspacePanel, 0, 0, 100, Scale(InfoHeight), Argb(255, 255, 255, 255));
            _toolbarPanel = EmojiWindowNative.CreatePanel(_workspacePanel, 0, 0, 100, Scale(ToolbarHeight), Argb(255, 255, 255, 255));
            _browserPanel = EmojiWindowNative.CreatePanel(_workspacePanel, 0, 0, 100, 100, Argb(255, 241, 245, 250));
            _browserFrame = EmojiWindowNative.CreatePanel(_browserPanel, 0, 0, 100, 100, Argb(255, 255, 255, 255));
            _browserCanvas = EmojiWindowNative.CreatePanel(_browserFrame, 0, 0, 100, 100, Argb(255, 249, 251, 253));

            _lblLeftTitle = Label(_leftPanel, "环境面板", LeftTitleFontSize, true);
            _lblInfoMain = Label(_infoPanel, string.Empty, InfoMainFontSize, true);
            _lblInfoSub = Label(_infoPanel, string.Empty, InfoSubFontSize, false);

            _btnNewEnv = Button(_leftPanel, "新建环境", Argb(255, 37, 99, 235), OnNewEnvironment);
            _btnDeleteEnv = Button(_leftPanel, "删除环境", Argb(255, 239, 68, 68), OnDeleteEnvironment);
            _btnTheme = Button(_leftPanel, "🌓", Argb(255, 245, 158, 11), ToggleTheme);

            string[] toolbarTexts = { "启动浏览器", "停止", "刷新", "打开后台", "同步Cookie", "切换代理", "更多操作" };
            Action[] toolbarActions =
            {
                OnStartBrowser,
                OnStopBrowser,
                OnRefreshBrowser,
                OnOpenBackground,
                OnSyncCookie,
                OnSwitchProxy,
                ActionPlaceholder,
            };

            for (int i = 0; i < toolbarTexts.Length; i++)
            {
                _toolbarButtons.Add(Button(_toolbarPanel, toolbarTexts[i], _toolbarColors[i], toolbarActions[i]));
            }

            _tree = EmojiWindowNative.CreateTreeView(_treePanel, 0, 0, 100, 100, Argb(255, 255, 255, 255), Argb(255, 31, 41, 55), IntPtr.Zero);
            NativeExtras.ClearTree(_tree);
            EmojiWindowNative.SetTreeViewSidebarMode(_tree, 1);
            EmojiWindowNative.SetTreeViewRowHeight(_tree, TreeRowHeight);
            EmojiWindowNative.SetTreeViewItemSpacing(_tree, TreeItemSpacing);
            EmojiWindowNative.SetTreeViewFont(_tree, _fontYaHei, _fontYaHei.Length, TreeFontSize, 400, 0);
            EmojiWindowNative.SetTreeViewCallback(_tree, CallbackNodeSelected, _treeNodeCallback);
            SeedTree();
        }

        private void SeedTree()
        {
            int switchRoot = AddRootNode("环境切换", "🧭", "switch_root", "环境切换");
            foreach (KeyValuePair<string, (string Name, string Domain, string Proxy, string Status, int Score)[]> group in _groupSeed)
            {
                int groupNodeId = AddChildNode(switchRoot, group.Key, "📁", "group", group.Key);
                _groupNodes[group.Key] = groupNodeId;
                _groupEnvironmentIds[group.Key] = new List<int>();
                foreach ((string name, string domain, string proxy, string status, int score) in group.Value)
                {
                    int envId = AddEnvironment(groupNodeId, group.Key, name, domain, proxy, status, score);
                    _groupEnvironmentIds[group.Key].Add(envId);
                }
            }

            foreach (KeyValuePair<string, string> module in _moduleIcons)
            {
                int moduleNodeId = AddRootNode(module.Key, module.Value, "module", module.Key);
                _moduleNodes[module.Key] = moduleNodeId;
            }

            EmojiWindowNative.ExpandAll(_tree);
            _envCounter = _environments.Count + 1;
        }

        private int AddRootNode(string text, string icon, string kind, string key)
        {
            byte[] textBytes = U(text);
            byte[] iconBytes = U(icon);
            int nodeId = EmojiWindowNative.AddRootNode(_tree, textBytes, textBytes.Length, iconBytes, iconBytes.Length);
            _nodeMeta[nodeId] = new NodeMeta { Kind = kind, Key = key };
            return nodeId;
        }

        private int AddChildNode(int parentId, string text, string icon, string kind, string key)
        {
            byte[] textBytes = U(text);
            byte[] iconBytes = U(icon);
            int nodeId = EmojiWindowNative.AddChildNode(_tree, parentId, textBytes, textBytes.Length, iconBytes, iconBytes.Length);
            _nodeMeta[nodeId] = new NodeMeta { Kind = kind, Key = key };
            return nodeId;
        }

        private int AddEnvironment(int groupNodeId, string groupName, string name, string domain, string proxy, string status, int score)
        {
            int nodeId = AddChildNode(groupNodeId, name, "●", "environment", name);
            int envId = _nextEnvId++;
            EnvironmentRecord env = new EnvironmentRecord
            {
                EnvId = envId,
                NodeId = nodeId,
                GroupName = groupName,
                Name = name,
                Domain = domain,
                Proxy = proxy,
                Status = status,
                Score = score,
                StartUrl = DefaultStartUrl(domain),
                CachePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache", $"env_{envId}"),
                BrowserFlag = $"env_{envId}",
                CookiePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cookies", $"env_{envId}.json"),
            };

            _environments[envId] = env;
            _nodeToEnvId[nodeId] = envId;
            _nodeMeta[nodeId].EnvId = envId;
            ApplyEnvironmentNodeColor(nodeId, status);
            return envId;
        }

        private void SelectDefaultEnvironment()
        {
            foreach (KeyValuePair<int, EnvironmentRecord> item in _environments)
            {
                EmojiWindowNative.SetSelectedNode(_tree, item.Value.NodeId);
                ActivateNode(item.Value.NodeId);
                break;
            }
        }

        private void OnButtonClick(int buttonId, IntPtr parentHwnd)
        {
            if (_buttonActions.TryGetValue(buttonId, out Action action))
            {
                action();
            }
        }

        private void OnTreeSelected(int nodeId, IntPtr context)
        {
            ActivateNode(nodeId);
        }

        private void OnResize(IntPtr hwnd, int width, int height)
        {
            if (hwnd != _window || width <= 0 || height <= 0)
            {
                return;
            }

            _width = width;
            _height = height;
            RefreshDpiScale();
            using (new RedrawScope(_window))
            {
                Layout();
            }
        }

        private void OnAddressKey(IntPtr hEdit, int keyCode, int keyDown, int shift, int ctrl, int alt)
        {
            if (keyDown != 1 || keyCode != VkReturn)
            {
                return;
            }

            if (!_editToEnvId.TryGetValue(hEdit, out int envId) || !_environments.TryGetValue(envId, out EnvironmentRecord env))
            {
                return;
            }

            string url = GetEditText(hEdit).Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            NavigateEnvironment(env, url);
        }

        private void ActivateNode(int nodeId)
        {
            if (!_nodeMeta.TryGetValue(nodeId, out NodeMeta meta))
            {
                return;
            }

            using (new RedrawScope(_window))
            {
                _currentNodeId = nodeId;
                if (meta.Kind == "environment" && meta.EnvId.HasValue)
                {
                    _toolbarVisible = true;
                    SwitchToEnvironment(meta.EnvId.Value);
                }
                else
                {
                    _currentEnvId = null;
                    HideVisibleEnvironment();
                    _currentVisibleEnvId = null;

                    if (meta.Kind == "group")
                    {
                        _toolbarVisible = false;
                        RenderGroup(meta.Key);
                    }
                    else if (meta.Kind == "module")
                    {
                        _toolbarVisible = false;
                        RenderModule(meta.Key);
                    }
                    else
                    {
                        _toolbarVisible = false;
                        RenderSwitchRoot();
                    }
                }

                Layout();
            }
        }

        private void RenderEnvironment(EnvironmentRecord env)
        {
            SetLabelText(_lblInfoMain, $"当前环境：{env.Name}   分组：{env.GroupName}   状态：{env.Status}   评分：{env.Score}分");
            SetLabelText(_lblInfoSub, $"域名：{env.Domain}   代理：{env.Proxy}");
            if (env.AddressEdit != IntPtr.Zero)
            {
                SetEditText(env.AddressEdit, EnvironmentUrlText(env));
            }
            SetWindowTitle($"电商多账号浏览器 - {env.Name}");
        }

        private void RenderGroup(string groupName)
        {
            int count = _groupEnvironmentIds.TryGetValue(groupName, out List<int> ids) ? ids.Count : 0;
            SetLabelText(_lblInfoMain, $"当前分组：{groupName}");
            SetLabelText(_lblInfoSub, $"该分组下共有 {count} 个环境，点击环境节点可快速切换");
            SetWindowTitle($"电商多账号浏览器 - {groupName}");
        }

        private void RenderModule(string moduleName)
        {
            SetLabelText(_lblInfoMain, $"当前模块：{moduleName}");
            SetLabelText(_lblInfoSub, "该区域先保留为模块工作区占位，后续可替换为真实业务页面");
            SetWindowTitle($"电商多账号浏览器 - {moduleName}");
        }

        private void RenderSwitchRoot()
        {
            SetLabelText(_lblInfoMain, "环境切换");
            SetLabelText(_lblInfoSub, "左侧展开分组并点击环境节点，即可快速切换右侧工作区");
            SetWindowTitle("电商多账号浏览器 - 草图版");
        }

        private void OnNewEnvironment()
        {
            (string groupName, int groupNodeId) = ResolveTargetGroup();
            string shortGroup = groupName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[0];
            string envName = $"{shortGroup}-环境{_envCounter:00}";
            string proxy = $"Proxy-{_envCounter:00}";
            int envId = AddEnvironment(groupNodeId, groupName, envName, "new-env.local", proxy, "待启动", 80);
            _groupEnvironmentIds[groupName].Add(envId);
            _envCounter++;

            EnvironmentRecord env = _environments[envId];
            NativeExtras.ExpandNode(_tree, groupNodeId);
            EmojiWindowNative.SetSelectedNode(_tree, env.NodeId);
            ActivateNode(env.NodeId);
        }

        private void OnDeleteEnvironment()
        {
            if (!_currentEnvId.HasValue || !_environments.TryGetValue(_currentEnvId.Value, out EnvironmentRecord env))
            {
                SetLabelText(_lblInfoSub, "请先在左侧树形框中选中一个环境节点，再执行删除。");
                return;
            }

            DestroyEnvironmentHost(env);
            _environments.Remove(env.EnvId);
            _nodeToEnvId.Remove(env.NodeId);
            _nodeMeta.Remove(env.NodeId);
            if (_groupEnvironmentIds.TryGetValue(env.GroupName, out List<int> ids))
            {
                ids.Remove(env.EnvId);
            }

            NativeExtras.RemoveNode(_tree, env.NodeId);

            int? nextEnvId = null;
            if (_groupEnvironmentIds.TryGetValue(env.GroupName, out List<int> sameGroup) && sameGroup.Count > 0)
            {
                nextEnvId = sameGroup[0];
            }
            else
            {
                foreach (KeyValuePair<string, List<int>> entry in _groupEnvironmentIds)
                {
                    if (entry.Value.Count > 0)
                    {
                        nextEnvId = entry.Value[0];
                        break;
                    }
                }
            }

            if (nextEnvId.HasValue)
            {
                EnvironmentRecord nextEnv = _environments[nextEnvId.Value];
                EmojiWindowNative.SetSelectedNode(_tree, nextEnv.NodeId);
                ActivateNode(nextEnv.NodeId);
            }
            else
            {
                _currentNodeId = null;
                _currentEnvId = null;
                _currentVisibleEnvId = null;
                RenderSwitchRoot();
            }
        }

        private void ActionPlaceholder()
        {
            if (!_currentEnvId.HasValue || !_environments.TryGetValue(_currentEnvId.Value, out EnvironmentRecord env))
            {
                SetLabelText(_lblInfoSub, "当前不是环境页面，工具栏操作未执行。");
                return;
            }

            SetLabelText(_lblInfoSub, $"域名：{env.Domain}   代理：{env.Proxy}   操作：已触发占位逻辑");
        }

        private void OnStartBrowser()
        {
            EnvironmentRecord env = CurrentEnvironment();
            if (env == null)
            {
                SetLabelText(_lblInfoSub, "当前不是环境页面，无法启动浏览器。");
                return;
            }

            EnsureEnvironmentHost(env);
            StartEnvironmentBrowser(env, EnvironmentUrlText(env));
        }

        private void OnStopBrowser()
        {
            EnvironmentRecord env = CurrentEnvironment();
            if (env == null)
            {
                SetLabelText(_lblInfoSub, "当前不是环境页面，无法停止浏览器。");
                return;
            }

            CloseEnvironmentBrowser(env);
            RenderEnvironment(env);
        }

        private void OnRefreshBrowser()
        {
            EnvironmentRecord env = CurrentEnvironment();
            if (env == null)
            {
                SetLabelText(_lblInfoSub, "当前不是环境页面，工具栏操作未执行。");
                return;
            }

            if (env.BrowserState != 2 && env.BrowserState != 3)
            {
                SetLabelText(_lblInfoSub, $"域名：{env.Domain}   代理：{env.Proxy}   状态：浏览器尚未启动");
                return;
            }

            IFBroSharpBrowser browser = GetEnvironmentBrowser(env);
            if (browser != null && browser.IsValid)
            {
                browser.Reload();
                SetLabelText(_lblInfoSub, $"域名：{env.Domain}   代理：{env.Proxy}   状态：已刷新独立缓存浏览器");
            }
            else
            {
                StartEnvironmentBrowser(env, EnvironmentUrlText(env));
            }
        }

        private void OnOpenBackground()
        {
            EnvironmentRecord env = CurrentEnvironment();
            if (env == null)
            {
                SetLabelText(_lblInfoSub, "当前不是环境页面，无法设置后台保活。");
                return;
            }

            env.KeepAlive = true;
            if (env.BrowserState == 2)
            {
                env.Status = "后台中";
            }
            ApplyEnvironmentNodeColor(env.NodeId, env.Status);
            RenderEnvironment(env);
            SetLabelText(_lblInfoSub, $"域名：{env.Domain}   代理：{env.Proxy}   状态：已设置后台保活");
        }

        private void OnSyncCookie()
        {
            EnvironmentRecord env = CurrentEnvironment();
            if (env == null)
            {
                SetLabelText(_lblInfoSub, "当前不是环境页面，无法同步 Cookie。");
                return;
            }

            SetLabelText(_lblInfoSub, $"域名：{env.Domain}   代理：{env.Proxy}   Cookie：{env.CookiePath}");
        }

        private void OnSwitchProxy()
        {
            EnvironmentRecord env = CurrentEnvironment();
            if (env == null)
            {
                SetLabelText(_lblInfoSub, "当前不是环境页面，无法切换代理。");
                return;
            }

            env.Proxy = $"{env.Proxy}-ALT";
            RenderEnvironment(env);
        }

        private void ToggleTheme()
        {
            bool dark = EmojiWindowNative.IsDarkMode() == 0;
            EmojiWindowNative.SetDarkMode(dark ? 1 : 0);
            using (new RedrawScope(_window))
            {
                ApplyTheme();
                Layout();
            }
        }

        private void ApplyTheme()
        {
            RefreshDpiScale();

            bool dark = EmojiWindowNative.IsDarkMode() != 0;
            uint bg = dark ? Argb(255, 20, 23, 28) : Argb(255, 244, 247, 251);
            uint leftBg = dark ? Argb(255, 26, 30, 36) : Argb(255, 255, 255, 255);
            uint panelBg = dark ? Argb(255, 33, 38, 46) : Argb(255, 255, 255, 255);
            uint browserBg = dark ? Argb(255, 17, 20, 26) : Argb(255, 238, 243, 249);
            uint canvasBg = dark ? Argb(255, 12, 15, 20) : Argb(255, 249, 251, 253);
            uint text = dark ? Argb(255, 241, 245, 249) : Argb(255, 31, 41, 55);
            uint muted = dark ? Argb(255, 148, 163, 184) : Argb(255, 100, 116, 139);
            uint titlebar = dark ? Argb(255, 15, 23, 42) : Argb(255, 37, 99, 235);
            uint accent = dark ? Argb(255, 96, 165, 250) : Argb(255, 37, 99, 235);

            EmojiWindowNative.set_window_titlebar_color(_window, titlebar);
            EmojiWindowNative.SetTitleBarTextColor(_window, Argb(255, 255, 255, 255));
            EmojiWindowNative.SetWindowBackgroundColor(_window, bg);

            foreach ((IntPtr panel, uint color) in new[]
            {
                (_leftPanel, leftBg),
                (_leftActionsPanel, leftBg),
                (_treePanel, leftBg),
                (_workspacePanel, bg),
                (_infoPanel, panelBg),
                (_toolbarPanel, panelBg),
                (_browserPanel, browserBg),
                (_browserFrame, panelBg),
                (_browserCanvas, canvasBg),
            })
            {
                EmojiWindowNative.SetPanelBackgroundColor(panel, color);
            }

            SetLabelColors(_lblLeftTitle, text, leftBg);
            SetLabelColors(_lblInfoMain, text, panelBg);
            SetLabelColors(_lblInfoSub, muted, panelBg);
            EmojiWindowNative.SetLabelFont(_lblLeftTitle, _fontYaHei, _fontYaHei.Length, Scale(LeftTitleFontSize), 1, 0, 0);
            EmojiWindowNative.SetLabelFont(_lblInfoMain, _fontYaHei, _fontYaHei.Length, Scale(InfoMainFontSize), 1, 0, 0);
            EmojiWindowNative.SetLabelFont(_lblInfoSub, _fontYaHei, _fontYaHei.Length, Scale(InfoSubFontSize), 0, 0, 0);

            foreach (EnvironmentRecord env in _environments.Values)
            {
                if (env.HostPanel != IntPtr.Zero)
                {
                    EmojiWindowNative.SetPanelBackgroundColor(env.HostPanel, canvasBg);
                }
                if (env.AddressPanel != IntPtr.Zero)
                {
                    EmojiWindowNative.SetPanelBackgroundColor(env.AddressPanel, panelBg);
                }
                if (env.BrowserView != IntPtr.Zero)
                {
                    EmojiWindowNative.SetPanelBackgroundColor(env.BrowserView, canvasBg);
                }
                if (env.AddressEdit != IntPtr.Zero)
                {
                    EmojiWindowNative.SetEditBoxColor(env.AddressEdit, text, dark ? Argb(255, 26, 30, 36) : Argb(255, 255, 255, 255));
                    EmojiWindowNative.SetEditBoxFont(env.AddressEdit, _fontSegoe, _fontSegoe.Length, Scale(EditFontSize), 0, 0, 0);
                }
            }

            PaintButton(_btnNewEnv, Argb(255, 37, 99, 235));
            PaintButton(_btnDeleteEnv, Argb(255, 239, 68, 68));
            PaintButton(_btnTheme, Argb(255, 245, 158, 11));
            EmojiWindowNative.SetButtonRound(_btnNewEnv, 0);
            EmojiWindowNative.SetButtonRound(_btnDeleteEnv, 0);
            SetButtonText(_btnTheme, dark ? "☀️" : "🌙");

            for (int i = 0; i < _toolbarButtons.Count; i++)
            {
                PaintButton(_toolbarButtons[i], _toolbarColors[i]);
            }

            EmojiWindowNative.SetTreeViewBackgroundColor(_tree, leftBg);
            EmojiWindowNative.SetTreeViewTextColor(_tree, text);
            EmojiWindowNative.SetTreeViewSelectedBgColor(_tree, accent);
            EmojiWindowNative.SetTreeViewSelectedForeColor(_tree, Argb(255, 255, 255, 255));
            EmojiWindowNative.SetTreeViewHoverBgColor(_tree, dark ? ShiftColor(leftBg, 8) : MixColor(accent, leftBg, 0.88f));
            EmojiWindowNative.SetTreeViewRowHeight(_tree, TreeRowHeight);
            EmojiWindowNative.SetTreeViewItemSpacing(_tree, TreeItemSpacing);
            EmojiWindowNative.SetTreeViewFont(_tree, _fontYaHei, _fontYaHei.Length, TreeFontSize, 400, 0);

            foreach (EnvironmentRecord env in _environments.Values)
            {
                ApplyEnvironmentNodeColor(env.NodeId, env.Status);
            }

            foreach (KeyValuePair<int, NodeMeta> item in _nodeMeta)
            {
                if (item.Value.Kind == "switch_root" || item.Value.Kind == "group" || item.Value.Kind == "module")
                {
                    NativeExtras.SetNodeForeColor(_tree, item.Key, text);
                }
            }
        }

        private void Layout()
        {
            RefreshDpiScale();

            int outer = Scale(Outer);
            int titleBarHeight = GetRuntimeTitleBarHeight();
            int contentTopOffset = Scale(ContentTopOffset);
            int leftWidth = Scale(LeftWidth);
            int gap = Scale(Gap);
            int topActionHeight = Scale(TopActionHeight);
            int infoHeight = Scale(InfoHeight);
            int toolbarHeightBase = Scale(ToolbarHeight);

            int leftX = outer;
            int topY = titleBarHeight + contentTopOffset;
            int leftHeight = Math.Max(Scale(300), _height - topY - outer);
            int rightX = leftX + leftWidth + gap;
            int rightWidth = Math.Max(Scale(420), _width - rightX - outer);
            int rightHeight = leftHeight;

            Move(_leftPanel, leftX, topY, leftWidth, leftHeight);
            Move(_workspacePanel, rightX, topY, rightWidth, rightHeight);

            Move(_leftActionsPanel, 0, 0, leftWidth, topActionHeight);
            Move(_treePanel, 0, topActionHeight + Scale(6), leftWidth, leftHeight - topActionHeight - Scale(6));
            Move(_tree, Scale(12), Scale(6), leftWidth - Scale(24), leftHeight - topActionHeight - Scale(16));

            EmojiWindowNative.SetLabelBounds(_lblLeftTitle, Scale(14), Scale(2), Scale(180), Scale(24));

            int buttonHeight = Scale(36);
            int newWidth = Scale(120);
            int deleteWidth = Scale(120);
            int themeWidth = Scale(54);
            int rowY = Scale(16);
            int pad = Scale(14);
            int localGap = Scale(10);
            int actionShift = Scale(8);
            int newX = Math.Max(Scale(6), pad - actionShift);
            int deleteX = newX + newWidth + localGap;
            int themeShift = Scale(8);
            int themeX = leftWidth - pad - themeWidth + themeShift;
            EmojiWindowNative.SetButtonBounds(_btnNewEnv, leftX + newX, topY + rowY, newWidth, buttonHeight);
            EmojiWindowNative.SetButtonBounds(_btnDeleteEnv, leftX + deleteX, topY + rowY, deleteWidth, buttonHeight);
            EmojiWindowNative.SetButtonBounds(_btnTheme, leftX + themeX, topY + rowY, themeWidth, buttonHeight);
            EmojiWindowNative.ShowButton(_btnNewEnv, 1);
            EmojiWindowNative.ShowButton(_btnDeleteEnv, 1);
            EmojiWindowNative.ShowButton(_btnTheme, 1);

            Move(_infoPanel, 0, 0, rightWidth, infoHeight);
            EmojiWindowNative.SetLabelBounds(_lblInfoMain, Scale(16), Scale(8), rightWidth - Scale(32), Scale(24));
            EmojiWindowNative.SetLabelBounds(_lblInfoSub, Scale(16), Scale(30), rightWidth - Scale(32), Scale(20));

            int toolbarY = infoHeight + Scale(10);
            int toolbarHeight = _toolbarVisible ? toolbarHeightBase : 1;
            Move(_toolbarPanel, 0, toolbarY, rightWidth, toolbarHeight);
            NativeExtras.ShowWindow(_toolbarPanel, _toolbarVisible ? SwShow : SwHide);

            int browserY = toolbarY + (_toolbarVisible ? toolbarHeightBase + Scale(10) : Scale(10));
            int browserHeight = Math.Max(Scale(240), rightHeight - browserY);
            Move(_browserPanel, 0, browserY, rightWidth, browserHeight);
            Move(_browserFrame, 0, 0, rightWidth, browserHeight);
            Move(_browserCanvas, Scale(16), Scale(16), Math.Max(Scale(240), rightWidth - Scale(32)), Math.Max(Scale(160), browserHeight - Scale(32)));

            _browserCanvasWidth = Math.Max(Scale(240), rightWidth - Scale(32));
            _browserCanvasHeight = Math.Max(Scale(160), browserHeight - Scale(32));
            foreach (EnvironmentRecord env in _environments.Values)
            {
                LayoutEnvironmentHost(env);
            }

            EmojiWindowNative.ExpandAll(_tree);

            if (_toolbarVisible)
            {
                LayoutToolbarButtons(rightX, topY + toolbarY);
            }
            else
            {
                foreach (int buttonId in _toolbarButtons)
                {
                    EmojiWindowNative.ShowButton(buttonId, 0);
                }
            }
        }

        private void LayoutEnvironmentHost(EnvironmentRecord env)
        {
            if (_browserCanvasWidth <= 0 || _browserCanvasHeight <= 0)
            {
                return;
            }

            if (env.HostPanel != IntPtr.Zero)
            {
                Move(env.HostPanel, 0, 0, _browserCanvasWidth, _browserCanvasHeight);
            }
            if (env.AddressPanel != IntPtr.Zero)
            {
                Move(env.AddressPanel, Scale(16), Scale(16), Math.Max(Scale(220), _browserCanvasWidth - Scale(32)), Scale(48));
            }
            if (env.AddressEdit != IntPtr.Zero)
            {
                EmojiWindowNative.SetEditBoxBounds(env.AddressEdit, Scale(14), Scale(8), Math.Max(Scale(180), _browserCanvasWidth - Scale(60)), Scale(32));
            }
            if (env.BrowserView != IntPtr.Zero)
            {
                int browserWidth = Math.Max(Scale(220), _browserCanvasWidth - Scale(32));
                int browserHeight = Math.Max(Scale(120), _browserCanvasHeight - Scale(96));
                Move(env.BrowserView, Scale(16), Scale(76), browserWidth, browserHeight);
                ResizeEnvironmentBrowser(env, browserWidth, browserHeight);
            }
        }

        private void LayoutToolbarButtons(int originX, int originY)
        {
            int x = Scale(16);
            for (int i = 0; i < _toolbarButtons.Count; i++)
            {
                int width = Scale(_toolbarWidths[i]);
                EmojiWindowNative.SetButtonBounds(_toolbarButtons[i], originX + x, originY + Scale(7), width, Scale(34));
                EmojiWindowNative.ShowButton(_toolbarButtons[i], 1);
                x += width + Scale(8);
            }
        }

        private (string GroupName, int GroupNodeId) ResolveTargetGroup()
        {
            if (_currentNodeId.HasValue && _nodeMeta.TryGetValue(_currentNodeId.Value, out NodeMeta meta))
            {
                if (meta.Kind == "group")
                {
                    return (meta.Key, _currentNodeId.Value);
                }

                if (meta.Kind == "environment" && meta.EnvId.HasValue && _environments.TryGetValue(meta.EnvId.Value, out EnvironmentRecord env))
                {
                    return (env.GroupName, _groupNodes[env.GroupName]);
                }
            }

            foreach (KeyValuePair<string, int> item in _groupNodes)
            {
                return (item.Key, item.Value);
            }

            throw new InvalidOperationException("No group node found.");
        }

        private EnvironmentRecord CurrentEnvironment()
        {
            if (!_currentEnvId.HasValue)
            {
                return null;
            }

            _environments.TryGetValue(_currentEnvId.Value, out EnvironmentRecord env);
            return env;
        }

        private IFBroSharpBrowser GetEnvironmentBrowser(EnvironmentRecord env)
        {
            return FBroSharpBrowserListControl.GetBrowserFromFlag(env.BrowserFlag);
        }

        private void StartEnvironmentBrowser(EnvironmentRecord env, string targetUrl)
        {
            string normalized = NormalizeUrl(targetUrl);
            if (string.IsNullOrEmpty(normalized))
            {
                normalized = env.StartUrl;
            }

            env.LastUrl = normalized;
            env.LastTitle = env.Name;
            env.KeepAlive = false;

            IFBroSharpBrowser existingBrowser = GetEnvironmentBrowser(env);
            if (existingBrowser != null && existingBrowser.IsValid)
            {
                env.BrowserState = 2;
                env.Status = "运行中";
                ApplyEnvironmentNodeColor(env.NodeId, env.Status);
                NavigateBrowser(env, normalized);
                ResizeEnvironmentBrowser(env, Math.Max(220, _browserCanvasWidth - 32), Math.Max(120, _browserCanvasHeight - 84));
                RenderEnvironment(env);
                SetLabelText(_lblInfoSub, $"域名：{env.Domain}   代理：{env.Proxy}   缓存：{env.CachePath}");
                return;
            }

            EnsureBrowserDirectories(env);
            env.BrowserEventHandler ??= new EnvironmentBrowserEvent(OnEnvironmentBrowserAddressChanged, OnEnvironmentBrowserTitleChanged, OnEnvironmentBrowserClosed);

            FBroSharpWindowsInfo windowInfo = new FBroSharpWindowsInfo
            {
                parent_window = env.BrowserView,
                x = 0,
                y = 0,
                width = Math.Max(220, _browserCanvasWidth - 32),
                height = Math.Max(120, _browserCanvasHeight - 84),
                window_name = env.Name,
            };

            FBroSharpRequestContext requestContext = CreateBrowserRequestContext(env);
            FBroSharpDictionaryValue extraInfo = new FBroSharpDictionaryValue();
            extraInfo.SetBool("是否为后台", false);

            bool created = FBroSharpControl.CreatBrowser(
                normalized,
                windowInfo,
                default,
                requestContext,
                extraInfo,
                env.BrowserEventHandler,
                default,
                env.BrowserFlag);

            if (!created)
            {
                env.BrowserState = 6;
                env.Status = "异常";
                ApplyEnvironmentNodeColor(env.NodeId, env.Status);
                RenderEnvironment(env);
                SetLabelText(_lblInfoSub, $"域名：{env.Domain}   代理：{env.Proxy}   状态：浏览器创建失败");
                return;
            }

            env.BrowserState = 2;
            env.Status = "运行中";
            ApplyEnvironmentNodeColor(env.NodeId, env.Status);
            RenderEnvironment(env);
            SetLabelText(_lblInfoSub, $"域名：{env.Domain}   代理：{env.Proxy}   缓存：{env.CachePath}");
        }

        private void EnsureBrowserDirectories(EnvironmentRecord env)
        {
            Directory.CreateDirectory(env.CachePath);
            string cookieDirectory = Path.GetDirectoryName(env.CookiePath);
            if (!string.IsNullOrEmpty(cookieDirectory))
            {
                Directory.CreateDirectory(cookieDirectory);
            }
        }

        private FBroSharpRequestContext CreateBrowserRequestContext(EnvironmentRecord env)
        {
            FBroSharpRequestContextSet contextSet = new FBroSharpRequestContextSet
            {
                cache_folder = env.CachePath,
                persist_session_cookies = true,
            };

            FBroSharpRequestContext requestContext = (FBroSharpRequestContext)FBroSharpRequestContext.CreateContext(contextSet);
            FBroSharpValue enabledValue = new FBroSharpValue();
            enabledValue.SetBool(true);
            requestContext.SetPreference("credentials_enable_autosignin", enabledValue);
            requestContext.SetPreference("credentials_enable_service", enabledValue);
            return requestContext;
        }

        private void ResizeEnvironmentBrowser(EnvironmentRecord env, int width, int height)
        {
            IFBroSharpBrowser browser = GetEnvironmentBrowser(env);
            if (browser == null || !browser.IsValid)
            {
                return;
            }

            browser.MoveWindow(0, 0, Math.Max(1, width), Math.Max(1, height), true);
        }

        private void NavigateBrowser(EnvironmentRecord env, string normalized)
        {
            IFBroSharpBrowser browser = GetEnvironmentBrowser(env);
            if (browser == null || !browser.IsValid)
            {
                return;
            }

            IFBroSharpFrame frame = browser.GetMainFrame();
            if (frame != null)
            {
                frame.LoadURL(normalized);
            }
        }

        private void OnEnvironmentBrowserAddressChanged(string browserFlag, string url)
        {
            foreach (EnvironmentRecord env in _environments.Values)
            {
                if (env.BrowserFlag != browserFlag || string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                env.LastUrl = url;
                if (_currentEnvId == env.EnvId && env.AddressEdit != IntPtr.Zero)
                {
                    SetEditText(env.AddressEdit, url);
                }

                break;
            }
        }

        private void OnEnvironmentBrowserTitleChanged(string browserFlag, string title)
        {
            foreach (EnvironmentRecord env in _environments.Values)
            {
                if (env.BrowserFlag == browserFlag && !string.IsNullOrWhiteSpace(title))
                {
                    env.LastTitle = title;
                    break;
                }
            }
        }

        private void OnEnvironmentBrowserClosed(string browserFlag)
        {
            foreach (EnvironmentRecord env in _environments.Values)
            {
                if (env.BrowserFlag != browserFlag)
                {
                    continue;
                }

                env.BrowserState = 5;
                env.Status = "待启动";
                env.KeepAlive = false;
                ApplyEnvironmentNodeColor(env.NodeId, env.Status);
                break;
            }
        }

        private void SwitchToEnvironment(int envId)
        {
            if (!_environments.TryGetValue(envId, out EnvironmentRecord env))
            {
                return;
            }

            _currentEnvId = envId;
            if (_currentVisibleEnvId != envId)
            {
                HideVisibleEnvironment();
                EnsureEnvironmentHost(env);
                ShowEnvironment(env);
                _currentVisibleEnvId = envId;
            }

            RenderEnvironment(env);
        }

        private void HideVisibleEnvironment()
        {
            if (!_currentVisibleEnvId.HasValue || !_environments.TryGetValue(_currentVisibleEnvId.Value, out EnvironmentRecord env))
            {
                return;
            }

            if (env.HostPanel != IntPtr.Zero)
            {
                NativeExtras.ShowWindow(env.HostPanel, SwHide);
            }
            env.Visible = false;
            if (env.BrowserState == 2 && env.KeepAlive)
            {
                env.BrowserState = 3;
                env.Status = "后台中";
                ApplyEnvironmentNodeColor(env.NodeId, env.Status);
            }

            _currentVisibleEnvId = null;
        }

        private void EnsureEnvironmentHost(EnvironmentRecord env)
        {
            if (env.HostPanel != IntPtr.Zero)
            {
                return;
            }

            bool dark = EmojiWindowNative.IsDarkMode() != 0;
            uint panelBg = dark ? Argb(255, 33, 38, 46) : Argb(255, 255, 255, 255);
            uint canvasBg = dark ? Argb(255, 12, 15, 20) : Argb(255, 249, 251, 253);
            uint text = dark ? Argb(255, 241, 245, 249) : Argb(255, 31, 41, 55);

            env.HostPanel = EmojiWindowNative.CreatePanel(_browserCanvas, 0, 0, 100, 100, canvasBg);
            env.AddressPanel = EmojiWindowNative.CreatePanel(env.HostPanel, 0, 0, 100, 44, panelBg);
            env.AddressEdit = EditBox(env.AddressPanel, EnvironmentUrlText(env), false);
            env.BrowserView = EmojiWindowNative.CreatePanel(env.HostPanel, 0, 0, 100, 100, canvasBg);

            EmojiWindowNative.SetEditBoxColor(env.AddressEdit, text, dark ? Argb(255, 26, 30, 36) : Argb(255, 255, 255, 255));
            EmojiWindowNative.SetEditBoxFont(env.AddressEdit, _fontSegoe, _fontSegoe.Length, Scale(EditFontSize), 0, 0, 0);
            EmojiWindowNative.SetEditBoxKeyCallback(env.AddressEdit, _editKeyCallback);
            _editToEnvId[env.AddressEdit] = env.EnvId;

            LayoutEnvironmentHost(env);
            NativeExtras.ShowWindow(env.HostPanel, SwHide);
        }

        private void ShowEnvironment(EnvironmentRecord env)
        {
            EnsureEnvironmentHost(env);
            NativeExtras.ShowWindow(env.HostPanel, SwShow);
            env.Visible = true;
            if (env.BrowserState == 3)
            {
                env.BrowserState = 2;
                env.Status = "运行中";
                ApplyEnvironmentNodeColor(env.NodeId, env.Status);
            }

            ResizeEnvironmentBrowser(env, Math.Max(220, _browserCanvasWidth - 32), Math.Max(120, _browserCanvasHeight - 84));
        }

        private void CloseEnvironmentBrowser(EnvironmentRecord env)
        {
            IFBroSharpBrowser browser = GetEnvironmentBrowser(env);
            if (browser != null && browser.IsValid)
            {
                browser.CloseBrowser(true, true);
            }

            env.BrowserState = 5;
            env.KeepAlive = false;
            env.Status = "待启动";
            env.Visible = false;
            ApplyEnvironmentNodeColor(env.NodeId, env.Status);

            if (env.HostPanel != IntPtr.Zero)
            {
                NativeExtras.ShowWindow(env.HostPanel, SwHide);
            }
            if (_currentVisibleEnvId == env.EnvId)
            {
                _currentVisibleEnvId = null;
            }
        }

        private void DestroyEnvironmentHost(EnvironmentRecord env)
        {
            CloseEnvironmentBrowser(env);

            if (env.AddressEdit != IntPtr.Zero)
            {
                _editToEnvId.Remove(env.AddressEdit);
            }
            if (env.HostPanel != IntPtr.Zero)
            {
                NativeExtras.DestroyWindow(env.HostPanel);
            }

            env.HostPanel = IntPtr.Zero;
            env.AddressPanel = IntPtr.Zero;
            env.AddressEdit = IntPtr.Zero;
            env.BrowserView = IntPtr.Zero;
            env.BrowserEventHandler = null;

            if (_currentVisibleEnvId == env.EnvId)
            {
                _currentVisibleEnvId = null;
            }
        }

        private void ApplyEnvironmentNodeColor(int nodeId, string status)
        {
            uint color = status switch
            {
                "运行中" => Argb(255, 34, 197, 94),
                "空闲中" => Argb(255, 100, 116, 139),
                "待启动" => Argb(255, 245, 158, 11),
                "后台中" => Argb(255, 59, 130, 246),
                "异常" => Argb(255, 239, 68, 68),
                _ => Argb(255, 31, 41, 55),
            };
            NativeExtras.SetNodeForeColor(_tree, nodeId, color);
        }

        private string EnvironmentUrlText(EnvironmentRecord env)
        {
            return string.IsNullOrWhiteSpace(env.LastUrl) ? env.StartUrl : env.LastUrl;
        }

        private string DefaultStartUrl(string domain)
        {
            return $"https://{domain}";
        }

        private string NormalizeUrl(string url)
        {
            string value = (url ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            if (!value.Contains("://"))
            {
                value = $"https://{value}";
            }
            return value;
        }

        private void NavigateEnvironment(EnvironmentRecord env, string url)
        {
            string normalized = NormalizeUrl(url);
            if (string.IsNullOrEmpty(normalized))
            {
                return;
            }

            EnsureEnvironmentHost(env);
            env.LastUrl = normalized;
            env.LastTitle = normalized;
            if (env.BrowserState == 0 || env.BrowserState == 5 || env.BrowserState == 6)
            {
                StartEnvironmentBrowser(env, normalized);
            }
            else
            {
                NavigateBrowser(env, normalized);
            }
            if (env.AddressEdit != IntPtr.Zero)
            {
                SetEditText(env.AddressEdit, normalized);
            }
            if (_currentEnvId == env.EnvId)
            {
                RenderEnvironment(env);
                SetLabelText(_lblInfoSub, $"域名：{env.Domain}   代理：{env.Proxy}   当前网址：{normalized}");
            }
        }

        private IntPtr Label(IntPtr parent, string text, int size, bool bold)
        {
            byte[] textBytes = U(text);
            return EmojiWindowNative.CreateLabel(
                parent,
                0,
                0,
                Scale(120),
                Scale(24),
                textBytes,
                textBytes.Length,
                Argb(255, 31, 41, 55),
                Argb(255, 255, 255, 255),
                _fontYaHei,
                _fontYaHei.Length,
                Scale(size),
                bold ? 1 : 0,
                0,
                0,
                0,
                0);
        }

        private int Button(IntPtr parent, string text, uint bg, Action action)
        {
            byte[] textBytes = U(text);
            int buttonId = EmojiWindowNative.create_emoji_button_bytes(parent, Array.Empty<byte>(), 0, textBytes, textBytes.Length, 0, 0, Scale(120), Scale(34), bg);
            PaintButton(buttonId, bg);
            EmojiWindowNative.ShowButton(buttonId, 1);
            if (action != null)
            {
                _buttonActions[buttonId] = action;
            }
            return buttonId;
        }

        private IntPtr EditBox(IntPtr parent, string text, bool readOnly)
        {
            byte[] textBytes = U(text);
            return EmojiWindowNative.CreateEditBox(
                parent,
                0,
                0,
                Scale(120),
                Scale(32),
                textBytes,
                textBytes.Length,
                Argb(255, 31, 41, 55),
                Argb(255, 255, 255, 255),
                _fontSegoe,
                _fontSegoe.Length,
                Scale(EditFontSize),
                0,
                0,
                0,
                0,
                0,
                readOnly ? 1 : 0,
                0,
                1,
                1);
        }

        private void PaintButton(int buttonId, uint bg)
        {
            uint textColor = ColorBrightness(bg) >= 175 ? Argb(255, 22, 34, 56) : Argb(255, 255, 255, 255);
            EmojiWindowNative.SetButtonStyle(buttonId, 0);
            EmojiWindowNative.SetButtonSize(buttonId, 1);
            EmojiWindowNative.SetButtonRound(buttonId, 1);
            EmojiWindowNative.SetButtonBackgroundColor(buttonId, bg);
            EmojiWindowNative.SetButtonBorderColor(buttonId, ShiftColor(bg, -18));
            EmojiWindowNative.SetButtonTextColor(buttonId, textColor);
            EmojiWindowNative.SetButtonHoverColors(buttonId, ShiftColor(bg, 10), ShiftColor(bg, 4), textColor);
        }

        private void SetLabelText(IntPtr label, string text)
        {
            byte[] textBytes = U(text);
            EmojiWindowNative.SetLabelText(label, textBytes, textBytes.Length);
        }

        private void SetButtonText(int buttonId, string text)
        {
            byte[] textBytes = U(text);
            EmojiWindowNative.SetButtonText(buttonId, textBytes, textBytes.Length);
        }

        private void SetEditText(IntPtr edit, string text)
        {
            byte[] textBytes = U(text);
            EmojiWindowNative.SetEditBoxText(edit, textBytes, textBytes.Length);
        }

        private string GetEditText(IntPtr edit)
        {
            return EmojiWindowNative.ReadUtf8(EmojiWindowNative.GetEditBoxText, edit);
        }

        private void SetWindowTitle(string title)
        {
            byte[] titleBytes = U(title);
            EmojiWindowNative.set_window_title(_window, titleBytes, titleBytes.Length);
        }

        private static void SetLabelColors(IntPtr label, uint fg, uint bg)
        {
            EmojiWindowNative.SetLabelColor(label, fg, bg);
        }

        private static void Move(IntPtr hwnd, int x, int y, int width, int height)
        {
            NativeExtras.MoveWindow(hwnd, x, y, width, height, true);
        }

        private static uint Argb(int a, int r, int g, int b)
        {
            return EmojiWindowNative.ARGB(a, r, g, b);
        }

        private static uint ShiftColor(uint color, int delta)
        {
            int a = (int)((color >> 24) & 255);
            int r = Clamp(((int)(color >> 16) & 255) + delta);
            int g = Clamp(((int)(color >> 8) & 255) + delta);
            int b = Clamp(((int)color & 255) + delta);
            return Argb(a, r, g, b);
        }

        private static uint MixColor(uint @base, uint target, float ratio)
        {
            ratio = Math.Max(0f, Math.Min(1f, ratio));
            int a = (int)((@base >> 24) & 255);
            int br = (int)((@base >> 16) & 255);
            int bg = (int)((@base >> 8) & 255);
            int bb = (int)(@base & 255);
            int tr = (int)((target >> 16) & 255);
            int tg = (int)((target >> 8) & 255);
            int tb = (int)(target & 255);
            int r = (int)(br * (1f - ratio) + tr * ratio);
            int g = (int)(bg * (1f - ratio) + tg * ratio);
            int b = (int)(bb * (1f - ratio) + tb * ratio);
            return Argb(a, r, g, b);
        }

        private static int ColorBrightness(uint color)
        {
            int r = (int)((color >> 16) & 255);
            int g = (int)((color >> 8) & 255);
            int b = (int)(color & 255);
            return (int)(r * 0.299 + g * 0.587 + b * 0.114);
        }

        private static int Clamp(int value)
        {
            return Math.Max(0, Math.Min(255, value));
        }

        private static byte[] U(string text)
        {
            return EmojiWindowNative.ToUtf8(text);
        }
    }
}
