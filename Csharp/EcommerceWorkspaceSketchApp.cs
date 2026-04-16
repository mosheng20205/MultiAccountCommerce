using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using EmojiWindowDemo;

namespace EmojiWindowEcommerceWorkspaceSketchDemo
{
    internal sealed class EcommerceWorkspaceSketchApp
    {
        private sealed class EnvironmentRecord
        {
            public int NodeId { get; set; }
            public string GroupName { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Domain { get; set; } = string.Empty;
            public string Proxy { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public int Score { get; set; }
        }

        private static class NativeExtras
        {
            private const string Dll = "emoji_window.dll";
            private const CallingConvention Cc = CallingConvention.StdCall;
            private const uint WmSetRedraw = 0x000B;
            private const uint RdwInvalidate = 0x0001;
            private const uint RdwErase = 0x0004;
            private const uint RdwAllChildren = 0x0080;
            private const uint RdwFrame = 0x0400;
            private const uint RdwUpdateNow = 0x0100;

            [DllImport(Dll, CallingConvention = Cc)] public static extern int ClearTree(IntPtr hTreeView);
            [DllImport(Dll, CallingConvention = Cc)] public static extern int RemoveNode(IntPtr hTreeView, int nodeId);
            [DllImport(Dll, CallingConvention = Cc)] public static extern int ExpandNode(IntPtr hTreeView, int nodeId);
            [DllImport(Dll, CallingConvention = Cc)] public static extern int SetNodeForeColor(IntPtr hTreeView, int nodeId, uint color);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool MoveWindow(IntPtr hwnd, int x, int y, int width, int height, [MarshalAs(UnmanagedType.Bool)] bool repaint);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool ShowWindow(IntPtr hwnd, int cmdShow);

            [DllImport("user32.dll", SetLastError = true)]
            private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

            public static void SetRedraw(IntPtr hwnd, bool enabled)
            {
                if (hwnd == IntPtr.Zero) return;
                SendMessage(hwnd, WmSetRedraw, enabled ? new IntPtr(1) : IntPtr.Zero, IntPtr.Zero);
            }

            public static void RefreshWindow(IntPtr hwnd)
            {
                if (hwnd == IntPtr.Zero) return;
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

        private const int SwShow = 5;
        private const int SwHide = 0;
        private const int CallbackNodeSelected = 1;
        private const int WindowWidth = 1480;
        private const int WindowHeight = 920;
        private const int TitleBarHeight = 32;
        private const int ContentTopOffset = 6;
        private const int Outer = 16;
        private const int LeftWidth = 320;
        private const int Gap = 12;
        private const int TopActionHeight = 56;
        private const int InfoHeight = 52;
        private const int ToolbarHeight = 48;

        private readonly byte[] _fontYaHei = EmojiWindowNative.ToUtf8("Microsoft YaHei UI");
        private readonly Dictionary<int, Action> _buttonActions = new Dictionary<int, Action>();
        private readonly Dictionary<int, Dictionary<string, string>> _nodeMeta = new Dictionary<int, Dictionary<string, string>>();
        private readonly Dictionary<string, int> _groupNodes = new Dictionary<string, int>();
        private readonly Dictionary<string, List<int>> _groupEnvironmentNodes = new Dictionary<string, List<int>>();
        private readonly Dictionary<string, int> _moduleNodes = new Dictionary<string, int>();
        private readonly Dictionary<int, EnvironmentRecord> _environments = new Dictionary<int, EnvironmentRecord>();
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

        private readonly Dictionary<string, (string Domain, string Proxy, string Status, int Score)[]> _groupSeed =
            new Dictionary<string, (string Domain, string Proxy, string Status, int Score)[]>
            {
                ["Amazon 店群"] = new[]
                {
                    ("amazon.com", "US-Proxy-01", "运行中", 92),
                    ("amazon.com", "US-Proxy-02", "空闲中", 88),
                },
                ["TikTok 店群"] = new[]
                {
                    ("seller-us.tiktok.com", "US-Proxy-09", "运行中", 95),
                },
                ["独立站"] = new[]
                {
                    ("shop.example.com", "JP-Proxy-03", "待启动", 84),
                }
            };

        private readonly string[] _groupEnvironmentNames =
        {
            "美区-环境01",
            "美区-环境02",
            "TK-环境01",
            "独立站-环境01",
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
        private IntPtr _lblCanvasTitle;
        private IntPtr _lblCanvasDesc;
        private IntPtr _lblCanvasHint;

        private int _btnNewEnv;
        private int _btnDeleteEnv;
        private int _btnTheme;
        private readonly List<int> _toolbarButtons = new List<int>();

        private int _width = WindowWidth;
        private int _height = WindowHeight;
        private int? _currentNodeId;
        private int? _currentEnvNode;
        private bool _toolbarVisible = true;
        private int _envCounter = 1;

        public EcommerceWorkspaceSketchApp()
        {
            _buttonClickCallback = OnButtonClick;
            _treeNodeCallback = OnTreeSelected;
            _windowResizeCallback = OnResize;
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
            byte[] title = U("电商多账号浏览器 - 草图版");
            _window = EmojiWindowNative.create_window_bytes_ex(title, title.Length, -1, -1, _width, _height, Argb(255, 37, 99, 235), Argb(255, 244, 247, 251));
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
            _leftActionsPanel = EmojiWindowNative.CreatePanel(_leftPanel, 0, 0, 100, TopActionHeight, Argb(255, 255, 255, 255));
            _treePanel = EmojiWindowNative.CreatePanel(_leftPanel, 0, 0, 100, 100, Argb(255, 255, 255, 255));
            _workspacePanel = EmojiWindowNative.CreatePanel(_window, 0, 0, 100, 100, Argb(255, 244, 247, 251));
            _infoPanel = EmojiWindowNative.CreatePanel(_workspacePanel, 0, 0, 100, InfoHeight, Argb(255, 255, 255, 255));
            _toolbarPanel = EmojiWindowNative.CreatePanel(_workspacePanel, 0, 0, 100, ToolbarHeight, Argb(255, 255, 255, 255));
            _browserPanel = EmojiWindowNative.CreatePanel(_workspacePanel, 0, 0, 100, 100, Argb(255, 241, 245, 250));
            _browserFrame = EmojiWindowNative.CreatePanel(_browserPanel, 0, 0, 100, 100, Argb(255, 255, 255, 255));
            _browserCanvas = EmojiWindowNative.CreatePanel(_browserFrame, 0, 0, 100, 100, Argb(255, 249, 251, 253));

            _lblLeftTitle = Label(_leftPanel, "环境面板", 12, true);
            _lblInfoMain = Label(_infoPanel, string.Empty, 13, true);
            _lblInfoSub = Label(_infoPanel, string.Empty, 11, false);
            _lblCanvasTitle = Label(_browserFrame, string.Empty, 18, true);
            _lblCanvasDesc = Label(_browserFrame, string.Empty, 11, false);
            _lblCanvasHint = Label(_browserCanvas, string.Empty, 18, true);

            _btnNewEnv = Button(_leftPanel, "新建环境", Argb(255, 37, 99, 235), OnNewEnvironment);
            _btnDeleteEnv = Button(_leftPanel, "删除环境", Argb(255, 239, 68, 68), OnDeleteEnvironment);
            _btnTheme = Button(_leftPanel, "🌙", Argb(255, 245, 158, 11), ToggleTheme);

            string[] toolbarTexts = { "启动浏览器", "停止", "刷新", "打开后台", "同步Cookie", "切换代理", "更多操作" };
            for (int i = 0; i < toolbarTexts.Length; i++)
            {
                _toolbarButtons.Add(Button(_toolbarPanel, toolbarTexts[i], _toolbarColors[i], ActionPlaceholder));
            }

            _tree = EmojiWindowNative.CreateTreeView(_treePanel, 0, 0, 100, 100, Argb(255, 255, 255, 255), Argb(255, 31, 41, 55), IntPtr.Zero);
            NativeExtras.ClearTree(_tree);
            EmojiWindowNative.SetTreeViewSidebarMode(_tree, 1);
            EmojiWindowNative.SetTreeViewRowHeight(_tree, 34.0f);
            EmojiWindowNative.SetTreeViewItemSpacing(_tree, 4.0f);
            EmojiWindowNative.SetTreeViewFont(_tree, _fontYaHei, _fontYaHei.Length, 12.0f, 400, 0);
            EmojiWindowNative.SetTreeViewCallback(_tree, CallbackNodeSelected, _treeNodeCallback);
            SeedTree();
        }

        private void SeedTree()
        {
            int switchRoot = AddRootNode("环境切换", "🧭", "switch_root");

            int envNameIndex = 0;
            foreach (KeyValuePair<string, (string Domain, string Proxy, string Status, int Score)[]> entry in _groupSeed)
            {
                int groupNodeId = AddChildNode(switchRoot, entry.Key, "📁", "group", entry.Key);
                _groupNodes[entry.Key] = groupNodeId;
                _groupEnvironmentNodes[entry.Key] = new List<int>();

                foreach ((string domain, string proxy, string status, int score) env in entry.Value)
                {
                    string envName = _groupEnvironmentNames[envNameIndex++];
                    int envNodeId = AddEnvironment(groupNodeId, entry.Key, envName, env.domain, env.proxy, env.status, env.score);
                    _groupEnvironmentNodes[entry.Key].Add(envNodeId);
                }
            }

            foreach (KeyValuePair<string, string> module in _moduleIcons)
            {
                int nodeId = AddRootNode(module.Key, module.Value, "module", module.Key);
                _moduleNodes[module.Key] = nodeId;
            }

            EmojiWindowNative.ExpandAll(_tree);
            _envCounter = _environments.Count + 1;
        }

        private int AddRootNode(string text, string icon, string kind, string key = null)
        {
            byte[] textBytes = U(text);
            byte[] iconBytes = U(icon);
            int nodeId = EmojiWindowNative.AddRootNode(_tree, textBytes, textBytes.Length, iconBytes, iconBytes.Length);
            _nodeMeta[nodeId] = new Dictionary<string, string>
            {
                ["kind"] = kind,
                ["key"] = key ?? text,
            };
            return nodeId;
        }

        private int AddChildNode(int parentId, string text, string icon, string kind, string key = null)
        {
            byte[] textBytes = U(text);
            byte[] iconBytes = U(icon);
            int nodeId = EmojiWindowNative.AddChildNode(_tree, parentId, textBytes, textBytes.Length, iconBytes, iconBytes.Length);
            _nodeMeta[nodeId] = new Dictionary<string, string>
            {
                ["kind"] = kind,
                ["key"] = key ?? text,
            };
            return nodeId;
        }

        private int AddEnvironment(int groupNodeId, string groupName, string name, string domain, string proxy, string status, int score)
        {
            int nodeId = AddChildNode(groupNodeId, name, "●", "environment", name);
            _environments[nodeId] = new EnvironmentRecord
            {
                NodeId = nodeId,
                GroupName = groupName,
                Name = name,
                Domain = domain,
                Proxy = proxy,
                Status = status,
                Score = score,
            };
            ApplyEnvironmentNodeColor(nodeId, status);
            return nodeId;
        }

        private void SelectDefaultEnvironment()
        {
            if (_environments.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<int, EnvironmentRecord> item in _environments)
            {
                EmojiWindowNative.SetSelectedNode(_tree, item.Key);
                ActivateNode(item.Key);
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
            using (new RedrawScope(_window))
            {
                Layout();
            }
        }

        private void ActivateNode(int nodeId)
        {
            if (!_nodeMeta.TryGetValue(nodeId, out Dictionary<string, string> meta))
            {
                return;
            }

            _currentNodeId = nodeId;
            string kind = meta["kind"];
            if (kind == "environment")
            {
                _currentEnvNode = nodeId;
                _toolbarVisible = true;
                RenderEnvironment(_environments[nodeId]);
                return;
            }

            if (kind == "group")
            {
                _toolbarVisible = false;
                RenderGroup(meta["key"]);
                return;
            }

            if (kind == "module")
            {
                _toolbarVisible = false;
                RenderModule(meta["key"]);
                return;
            }

            _toolbarVisible = false;
            RenderSwitchRoot();
        }

        private void RenderEnvironment(EnvironmentRecord env)
        {
            SetLabelText(_lblInfoMain, $"当前环境：{env.Name}   分组：{env.GroupName}   状态：{env.Status}   评分：{env.Score}分");
            SetLabelText(_lblInfoSub, $"域名：{env.Domain}   代理：{env.Proxy}");
            SetLabelText(_lblCanvasTitle, "浏览器主工作区");
            SetLabelText(_lblCanvasDesc, $"当前环境 {env.Name} 的浏览器区域占位壳子");
            SetLabelText(_lblCanvasHint, env.Name);
            SetWindowTitle($"电商多账号浏览器 - {env.Name}");
        }

        private void RenderGroup(string groupName)
        {
            int count = _groupEnvironmentNodes.TryGetValue(groupName, out List<int> ids) ? ids.Count : 0;
            SetLabelText(_lblInfoMain, $"当前分组：{groupName}");
            SetLabelText(_lblInfoSub, $"该分组下共有 {count} 个环境，点击环境节点可快速切换");
            SetLabelText(_lblCanvasTitle, $"{groupName} 工作区");
            SetLabelText(_lblCanvasDesc, "这里可以继续扩展为分组总览、批量操作或分组统计页面");
            SetLabelText(_lblCanvasHint, groupName);
            SetWindowTitle($"电商多账号浏览器 - {groupName}");
        }

        private void RenderModule(string moduleName)
        {
            SetLabelText(_lblInfoMain, $"当前模块：{moduleName}");
            SetLabelText(_lblInfoSub, "该区域先保留为模块工作区占位，后续可替换为真实业务页面");
            SetLabelText(_lblCanvasTitle, $"{moduleName} 工作区");
            SetLabelText(_lblCanvasDesc, "右侧区域已按 Python 草图保留为最大化内容区");
            SetLabelText(_lblCanvasHint, moduleName);
            SetWindowTitle($"电商多账号浏览器 - {moduleName}");
        }

        private void RenderSwitchRoot()
        {
            SetLabelText(_lblInfoMain, "环境切换");
            SetLabelText(_lblInfoSub, "左侧展开分组并点击环境节点，即可快速切换右侧工作区");
            SetLabelText(_lblCanvasTitle, "浏览器主工作区");
            SetLabelText(_lblCanvasDesc, "当前未选中具体环境");
            SetLabelText(_lblCanvasHint, "请选择一个环境");
            SetWindowTitle("电商多账号浏览器 - 草图版");
        }

        private void OnNewEnvironment()
        {
            (string groupName, int groupNodeId) = ResolveTargetGroup();
            string name = $"{groupName.Split(' ')[0]}-环境{_envCounter:00}";
            string proxy = $"Proxy-{_envCounter:00}";
            int nodeId = AddEnvironment(groupNodeId, groupName, name, "new-env.local", proxy, "待启动", 80);
            _groupEnvironmentNodes[groupName].Add(nodeId);
            _envCounter++;
            NativeExtras.ExpandNode(_tree, groupNodeId);
            EmojiWindowNative.SetSelectedNode(_tree, nodeId);
            ActivateNode(nodeId);
        }

        private void OnDeleteEnvironment()
        {
            if (!_currentNodeId.HasValue || !_environments.TryGetValue(_currentNodeId.Value, out EnvironmentRecord env))
            {
                SetLabelText(_lblInfoSub, "请先在左侧树形框中选中一个环境节点，再执行删除。");
                return;
            }

            _environments.Remove(env.NodeId);
            _nodeMeta.Remove(env.NodeId);
            if (_groupEnvironmentNodes.TryGetValue(env.GroupName, out List<int> ids))
            {
                ids.Remove(env.NodeId);
            }

            NativeExtras.RemoveNode(_tree, env.NodeId);

            int? nextNode = null;
            foreach (List<int> groupIds in _groupEnvironmentNodes.Values)
            {
                if (groupIds.Count > 0)
                {
                    nextNode = groupIds[0];
                    break;
                }
            }

            if (nextNode.HasValue)
            {
                EmojiWindowNative.SetSelectedNode(_tree, nextNode.Value);
                ActivateNode(nextNode.Value);
            }
            else
            {
                _currentNodeId = null;
                _currentEnvNode = null;
                RenderSwitchRoot();
            }
        }

        private void ActionPlaceholder()
        {
            if (!_currentEnvNode.HasValue || !_environments.TryGetValue(_currentEnvNode.Value, out EnvironmentRecord env))
            {
                SetLabelText(_lblCanvasDesc, "当前不是环境页面，工具栏操作未执行。");
                return;
            }

            SetLabelText(_lblCanvasDesc, $"已触发 {env.Name} 的草图操作，占位逻辑生效。");
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

            foreach ((IntPtr panel, uint color) item in new[]
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
                EmojiWindowNative.SetPanelBackgroundColor(item.panel, item.color);
            }

            SetLabelColors(_lblLeftTitle, text, leftBg);
            SetLabelColors(_lblInfoMain, text, panelBg);
            SetLabelColors(_lblInfoSub, muted, panelBg);
            SetLabelColors(_lblCanvasTitle, text, panelBg);
            SetLabelColors(_lblCanvasDesc, muted, panelBg);
            SetLabelColors(_lblCanvasHint, accent, canvasBg);

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

            foreach (KeyValuePair<int, EnvironmentRecord> item in _environments)
            {
                ApplyEnvironmentNodeColor(item.Key, item.Value.Status);
            }

            foreach (KeyValuePair<int, Dictionary<string, string>> item in _nodeMeta)
            {
                string kind = item.Value["kind"];
                if (kind == "switch_root" || kind == "group" || kind == "module")
                {
                    NativeExtras.SetNodeForeColor(_tree, item.Key, text);
                }
            }
        }

        private void Layout()
        {
            int leftX = Outer;
            int topY = TitleBarHeight + ContentTopOffset;
            int leftHeight = Math.Max(300, _height - topY - Outer);
            int rightX = leftX + LeftWidth + Gap;
            int rightWidth = Math.Max(420, _width - rightX - Outer);
            int rightHeight = leftHeight;

            Move(_leftPanel, leftX, topY, LeftWidth, leftHeight);
            Move(_workspacePanel, rightX, topY, rightWidth, rightHeight);

            Move(_leftActionsPanel, 0, 0, LeftWidth, TopActionHeight);
            Move(_treePanel, 0, TopActionHeight + 6, LeftWidth, leftHeight - TopActionHeight - 6);
            Move(_tree, 12, 6, LeftWidth - 24, leftHeight - TopActionHeight - 16);

            EmojiWindowNative.SetLabelBounds(_lblLeftTitle, 14, 2, 120, 16);

            int buttonHeight = 30;
            int newWidth = 102;
            int deleteWidth = 102;
            int themeWidth = 42;
            int rowY = 18;
            int gap = 10;
            int leftPad = 14;
            int rightPad = 14;
            int newX = leftPad;
            int deleteX = newX + newWidth + gap;
            int themeX = LeftWidth - rightPad - themeWidth;
            EmojiWindowNative.SetButtonBounds(_btnNewEnv, leftX + newX, topY + rowY, newWidth, buttonHeight);
            EmojiWindowNative.SetButtonBounds(_btnDeleteEnv, leftX + deleteX, topY + rowY, deleteWidth, buttonHeight);
            EmojiWindowNative.SetButtonBounds(_btnTheme, leftX + themeX, topY + rowY, themeWidth, buttonHeight);
            EmojiWindowNative.ShowButton(_btnNewEnv, 1);
            EmojiWindowNative.ShowButton(_btnDeleteEnv, 1);
            EmojiWindowNative.ShowButton(_btnTheme, 1);

            Move(_infoPanel, 0, 0, rightWidth, InfoHeight);
            EmojiWindowNative.SetLabelBounds(_lblInfoMain, 16, 10, rightWidth - 32, 18);
            EmojiWindowNative.SetLabelBounds(_lblInfoSub, 16, 28, rightWidth - 32, 16);

            int toolbarY = InfoHeight + 10;
            int toolbarHeight = _toolbarVisible ? ToolbarHeight : 1;
            Move(_toolbarPanel, 0, toolbarY, rightWidth, toolbarHeight);
            NativeExtras.ShowWindow(_toolbarPanel, _toolbarVisible ? SwShow : SwHide);

            int browserY = toolbarY + (_toolbarVisible ? ToolbarHeight + 10 : 10);
            int browserHeight = Math.Max(240, rightHeight - browserY);
            Move(_browserPanel, 0, browserY, rightWidth, browserHeight);
            Move(_browserFrame, 0, 0, rightWidth, browserHeight);
            Move(_browserCanvas, 16, 74, Math.Max(240, rightWidth - 32), Math.Max(160, browserHeight - 90));
            EmojiWindowNative.SetLabelBounds(_lblCanvasTitle, 18, 18, rightWidth - 36, 24);
            EmojiWindowNative.SetLabelBounds(_lblCanvasDesc, 18, 44, rightWidth - 36, 18);

            int canvasWidth = Math.Max(240, rightWidth - 32);
            int canvasHeight = Math.Max(160, browserHeight - 90);
            int hintWidth = Math.Min(420, canvasWidth - 40);
            int hintX = Math.Max(20, (canvasWidth - hintWidth) / 2);
            int hintY = Math.Max(20, (canvasHeight - 30) / 2 - 20);
            EmojiWindowNative.SetLabelBounds(_lblCanvasHint, hintX, hintY, hintWidth, 30);

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

        private void LayoutToolbarButtons(int originX, int originY)
        {
            int x = 16;
            int y = 7;
            for (int i = 0; i < _toolbarButtons.Count; i++)
            {
                EmojiWindowNative.SetButtonBounds(_toolbarButtons[i], originX + x, originY + y, _toolbarWidths[i], 34);
                EmojiWindowNative.ShowButton(_toolbarButtons[i], 1);
                x += _toolbarWidths[i] + 8;
            }
        }

        private (string GroupName, int GroupNodeId) ResolveTargetGroup()
        {
            if (_currentNodeId.HasValue && _nodeMeta.TryGetValue(_currentNodeId.Value, out Dictionary<string, string> meta))
            {
                if (meta["kind"] == "group")
                {
                    return (meta["key"], _currentNodeId.Value);
                }

                if (meta["kind"] == "environment" && _environments.TryGetValue(_currentNodeId.Value, out EnvironmentRecord env))
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

        private void ApplyEnvironmentNodeColor(int nodeId, string status)
        {
            uint color = status switch
            {
                "运行中" => Argb(255, 34, 197, 94),
                "空闲中" => Argb(255, 100, 116, 139),
                "待启动" => Argb(255, 245, 158, 11),
                "异常" => Argb(255, 239, 68, 68),
                _ => Argb(255, 31, 41, 55),
            };
            NativeExtras.SetNodeForeColor(_tree, nodeId, color);
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

        private IntPtr Label(IntPtr parent, string text, int size, bool bold)
        {
            byte[] textBytes = U(text);
            return EmojiWindowNative.CreateLabel(
                parent,
                0,
                0,
                100,
                20,
                textBytes,
                textBytes.Length,
                Argb(255, 31, 41, 55),
                Argb(255, 255, 255, 255),
                _fontYaHei,
                _fontYaHei.Length,
                size,
                bold ? 1 : 0,
                0,
                0,
                0,
                0);
        }

        private int Button(IntPtr parent, string text, uint bg, Action action)
        {
            byte[] textBytes = U(text);
            int buttonId = EmojiWindowNative.create_emoji_button_bytes(parent, Array.Empty<byte>(), 0, textBytes, textBytes.Length, 0, 0, 100, 34, bg);
            PaintButton(buttonId, bg);
            EmojiWindowNative.ShowButton(buttonId, 1);
            if (action != null)
            {
                _buttonActions[buttonId] = action;
            }

            return buttonId;
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

        private void SetWindowTitle(string title)
        {
            byte[] textBytes = U(title);
            EmojiWindowNative.set_window_title(_window, textBytes, textBytes.Length);
        }

        private static void SetLabelColors(IntPtr label, uint fg, uint bg)
        {
            EmojiWindowNative.SetLabelColor(label, fg, bg);
        }

        private static void Move(IntPtr hwnd, int x, int y, int width, int height)
        {
            NativeExtras.MoveWindow(hwnd, x, y, width, height, true);
        }

        private static uint Argb(int a, int r, int g, int b) => EmojiWindowNative.ARGB(a, r, g, b);

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
            if (value < 0) return 0;
            if (value > 255) return 255;
            return value;
        }

        private static byte[] U(string text) => EmojiWindowNative.ToUtf8(text);
    }
}
