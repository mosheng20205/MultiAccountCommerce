using System;
using System.Collections.Generic;
using System.Linq;
using EmojiWindowDemo;

namespace EmojiWindowEcommerceWorkspaceSketchDemo
{
    internal sealed partial class EcommerceWorkspaceSketchApp
    {
        private const int ProxyMenuSelectedTopId = 5100;
        private const int ProxyMenuRecentTopId = 5200;
        private const int ProxyMenuAllTopId = 5300;
        private const int ProxyMenuActionTopId = 5400;

        private const int ProxyMenuSelectedInfoId = 5110;
        private const int ProxyMenuRecentBaseId = 5201;
        private const int ProxyMenuAllBaseId = 5301;
        private const int ProxyMenuActionApplyId = 5401;
        private const int ProxyMenuActionSaveLaterId = 5402;
        private const int ProxyMenuActionTestId = 5403;
        private const int ProxyMenuActionManageId = 5404;
        private const int ProxyMenuActionCloseId = 5405;

        private const int ProxyMenuMaxRecentItems = 8;
        private const int ProxyMenuMaxAllItems = 18;
        private const int QuickProxyHostHeight = 42;
        private const int QuickProxyMenuInset = 8;

        private IntPtr _quickProxyHostPanel;
        private IntPtr _quickProxyMenuBar;
        private bool _quickProxyPanelVisible;
        private int _quickProxyEnvId = -1;
        private string _quickProxySelectionName = string.Empty;
        private readonly Dictionary<int, string> _proxyMenuSelectionMap = new Dictionary<int, string>();

        private void InitializeQuickProxyUi()
        {
            _quickProxyHostPanel = IntPtr.Zero;
            _quickProxyPanelVisible = false;
            _quickProxyEnvId = -1;
            _quickProxySelectionName = string.Empty;
            _proxyMenuSelectionMap.Clear();
        }

        private void SetQuickProxyVisible(bool visible)
        {
            _quickProxyPanelVisible = visible;
            if (!visible)
            {
                DestroyQuickProxyMenuBar();
                if (_quickProxyHostPanel != IntPtr.Zero)
                {
                    NativeExtras.ShowWindow(_quickProxyHostPanel, SwHide);
                }
            }
        }

        private void EnsureQuickProxyHostPanel()
        {
            if (_quickProxyHostPanel != IntPtr.Zero || _workspacePanel == IntPtr.Zero)
            {
                return;
            }

            _quickProxyHostPanel = EmojiWindowNative.CreatePanel(
                _workspacePanel,
                0,
                0,
                Scale(560),
                Scale(QuickProxyHostHeight),
                GetQuickProxyHostColor());
            NativeExtras.ShowWindow(_quickProxyHostPanel, SwHide);
        }

        private uint GetQuickProxyHostColor()
        {
            bool dark = EmojiWindowNative.IsDarkMode() != 0;
            return dark ? Argb(255, 20, 38, 54) : Argb(255, 227, 244, 255);
        }

        private void ToggleQuickProxyPanel()
        {
            EnvironmentRecord env = CurrentEnvironment();
            if (env == null)
            {
                return;
            }

            if (_quickProxyPanelVisible)
            {
                SetQuickProxyVisible(false);
                SetLabelText(_lblInfoSub, $"{FormatEnvironmentProxySummary(env)}   已收起代理菜单");
                return;
            }

            _quickProxyEnvId = env.EnvId;
            _quickProxySelectionName = string.IsNullOrWhiteSpace(env.Proxy) ? DirectProxyOption : env.Proxy;
            _quickProxyPanelVisible = true;
            RebuildQuickProxyMenuBar(env);
            SetLabelText(_lblInfoSub, $"{FormatEnvironmentProxySummary(env)}   已展开代理菜单");
        }

        private void LayoutQuickProxyPanel()
        {
            if (!_quickProxyPanelVisible || _quickProxyMenuBar == IntPtr.Zero || _quickProxyHostPanel == IntPtr.Zero || _toolbarButtons.Count <= 5)
            {
                return;
            }

            EmojiWindowNative.GetButtonBounds(_toolbarButtons[5], out int buttonX, out int buttonY, out int buttonWidth, out int buttonHeight);

            int outer = Scale(Outer);
            int leftWidth = Scale(LeftWidth);
            int gap = Scale(Gap);
            int infoHeight = Scale(InfoHeight);
            int toolbarY = infoHeight + Scale(10);
            int rightWidth = Math.Max(Scale(420), _width - (outer + leftWidth + gap) - outer);
            int menuWidth = Math.Min(Scale(620), Math.Max(Scale(420), rightWidth - Scale(24)));
            int hostX = Math.Max(Scale(12), Math.Min(rightWidth - menuWidth - Scale(12), buttonX));
            int hostY = toolbarY + buttonY + buttonHeight + Scale(6);

            Move(_quickProxyHostPanel, hostX, hostY, menuWidth, Scale(QuickProxyHostHeight));
            NativeExtras.ShowWindow(_quickProxyHostPanel, SwShow);
            NativeExtras.BringToTop(_quickProxyHostPanel);

            EmojiWindowNative.SetMenuBarPlacement(
                _quickProxyMenuBar,
                Scale(QuickProxyMenuInset),
                Scale(4),
                Math.Max(Scale(280), menuWidth - Scale(QuickProxyMenuInset * 2)),
                Scale(34));
            NativeExtras.ShowWindow(_quickProxyMenuBar, SwShow);
            NativeExtras.BringToTop(_quickProxyMenuBar);
        }

        private void RefreshQuickProxyPanelForCurrentEnvironment()
        {
            if (!_quickProxyPanelVisible)
            {
                return;
            }

            EnvironmentRecord env = CurrentEnvironment();
            if (env == null)
            {
                SetQuickProxyVisible(false);
                return;
            }

            if (_quickProxyEnvId != env.EnvId)
            {
                _quickProxyEnvId = env.EnvId;
                _quickProxySelectionName = string.IsNullOrWhiteSpace(env.Proxy) ? DirectProxyOption : env.Proxy;
            }

            RebuildQuickProxyMenuBar(env);
        }

        private void RebuildQuickProxyMenuBar(EnvironmentRecord env)
        {
            DestroyQuickProxyMenuBar();
            EnsureQuickProxyHostPanel();

            _proxyMenuSelectionMap.Clear();
            _quickProxyMenuBar = EmojiWindowNative.CreateMenuBar(_quickProxyHostPanel);
            if (_quickProxyMenuBar == IntPtr.Zero)
            {
                _quickProxyPanelVisible = false;
                SetLabelText(_lblInfoSub, $"{FormatEnvironmentProxySummary(env)}   代理菜单创建失败");
                return;
            }

            EmojiWindowNative.SetMenuBarCallback(_quickProxyMenuBar, _proxyMenuBarCallback);

            AddProxyTopMenu("🛰️ 已选代理", ProxyMenuSelectedTopId);
            AddProxyTopMenu("🕘 最近使用", ProxyMenuRecentTopId);
            AddProxyTopMenu("🌐 全部代理", ProxyMenuAllTopId);
            AddProxyTopMenu("⚙️ 操作", ProxyMenuActionTopId);

            AddProxySubMenu(ProxyMenuSelectedTopId, BuildSelectedProxyMenuText(env), ProxyMenuSelectedInfoId);
            BuildRecentProxySubMenus(env);
            BuildAllProxySubMenus(env);
            BuildActionSubMenus();
            LayoutQuickProxyPanel();
        }

        private void DestroyQuickProxyMenuBar()
        {
            if (_quickProxyMenuBar != IntPtr.Zero)
            {
                EmojiWindowNative.DestroyMenuBar(_quickProxyMenuBar);
                _quickProxyMenuBar = IntPtr.Zero;
            }

            _proxyMenuSelectionMap.Clear();
        }

        private void BuildRecentProxySubMenus(EnvironmentRecord env)
        {
            IEnumerable<string> recentNames = GetRecentProxyNames(ProxyMenuMaxRecentItems);
            List<string> ordered = recentNames.ToList();
            if (!string.IsNullOrWhiteSpace(env.Proxy) && !ordered.Contains(env.Proxy, StringComparer.OrdinalIgnoreCase))
            {
                ordered.Insert(0, env.Proxy);
            }

            if (ordered.Count == 0)
            {
                AddProxySubMenu(ProxyMenuRecentTopId, "🕘 暂无最近使用", ProxyMenuRecentBaseId);
                return;
            }

            for (int i = 0; i < ordered.Count; i++)
            {
                string proxyName = ordered[i];
                int itemId = ProxyMenuRecentBaseId + i;
                _proxyMenuSelectionMap[itemId] = proxyName;
                AddProxySubMenu(ProxyMenuRecentTopId, BuildProxyMenuItemText(proxyName), itemId);
            }
        }

        private void BuildAllProxySubMenus(EnvironmentRecord env)
        {
            List<string> allNames = new List<string> { DirectProxyOption };
            allNames.AddRange(_proxyOrder.Take(ProxyMenuMaxAllItems));

            for (int i = 0; i < allNames.Count; i++)
            {
                string proxyName = allNames[i];
                int itemId = ProxyMenuAllBaseId + i;
                _proxyMenuSelectionMap[itemId] = proxyName;
                AddProxySubMenu(ProxyMenuAllTopId, BuildProxyMenuItemText(proxyName), itemId);
            }
        }

        private void BuildActionSubMenus()
        {
            AddProxySubMenu(ProxyMenuActionTopId, "⚡ 应用并刷新", ProxyMenuActionApplyId);
            AddProxySubMenu(ProxyMenuActionTopId, "💾 仅保存下次生效", ProxyMenuActionSaveLaterId);
            AddProxySubMenu(ProxyMenuActionTopId, "🧪 测试当前选择", ProxyMenuActionTestId);
            AddProxySubMenu(ProxyMenuActionTopId, "🗂️ 去代理管理", ProxyMenuActionManageId);
            AddProxySubMenu(ProxyMenuActionTopId, "✖ 关闭菜单", ProxyMenuActionCloseId);
        }

        private void AddProxyTopMenu(string text, int itemId)
        {
            byte[] bytes = U(text);
            EmojiWindowNative.MenuBarAddItem(_quickProxyMenuBar, bytes, bytes.Length, itemId);
        }

        private void AddProxySubMenu(int parentId, string text, int itemId)
        {
            byte[] bytes = U(text);
            EmojiWindowNative.MenuBarAddSubItem(_quickProxyMenuBar, parentId, bytes, bytes.Length, itemId);
        }

        private string BuildSelectedProxyMenuText(EnvironmentRecord env)
        {
            string selection = string.IsNullOrWhiteSpace(_quickProxySelectionName) ? DirectProxyOption : _quickProxySelectionName;
            string selectedDisplay = DescribeProxyPickerItem(selection);
            return $"⭐ {selectedDisplay} | {env.ProxyStatus}";
        }

        private string BuildProxyMenuItemText(string proxyName)
        {
            string display = DescribeProxyPickerItem(proxyName);
            bool selected = string.Equals(proxyName, _quickProxySelectionName, StringComparison.OrdinalIgnoreCase)
                || (IsDirectProxyOption(proxyName) && IsDirectProxyOption(_quickProxySelectionName));
            return selected ? $"✅ {display}" : $"▫️ {display}";
        }

        private void OnProxyMenuItemClick(int menuId, int itemId)
        {
            EnvironmentRecord env = CurrentEnvironment();
            if (env == null)
            {
                SetQuickProxyVisible(false);
                return;
            }

            if (_proxyMenuSelectionMap.TryGetValue(itemId, out string proxyName))
            {
                _quickProxySelectionName = proxyName;
                SetLabelText(_lblInfoSub, $"{FormatEnvironmentProxySummary(env)}   已选择：{DescribeProxyPickerItem(proxyName)}");
                RefreshQuickProxyPanelForCurrentEnvironment();
                return;
            }

            switch (itemId)
            {
                case ProxyMenuSelectedInfoId:
                    SetLabelText(_lblInfoSub, $"{FormatEnvironmentProxySummary(env)}   当前菜单选择：{DescribeProxyPickerItem(_quickProxySelectionName)}");
                    break;
                case ProxyMenuActionApplyId:
                    ApplyQuickProxyNow();
                    break;
                case ProxyMenuActionSaveLaterId:
                    SaveQuickProxyForLater();
                    break;
                case ProxyMenuActionTestId:
                    TestQuickProxySelection();
                    break;
                case ProxyMenuActionManageId:
                    OpenProxyManagementFromQuickPanel();
                    break;
                case ProxyMenuActionCloseId:
                    SetQuickProxyVisible(false);
                    SetLabelText(_lblInfoSub, $"{FormatEnvironmentProxySummary(env)}   已关闭代理菜单");
                    break;
            }
        }

        private void OnQuickRecentProxySelected(IntPtr hListBox, int index)
        {
        }

        private void OnQuickAllProxySelected(IntPtr hListBox, int index)
        {
        }

        private void SaveQuickProxyForLater()
        {
            EnvironmentRecord env = CurrentEnvironment();
            if (env == null)
            {
                return;
            }

            if (!ResolveQuickProxySelection(true, out ProxyConfig config, out string error))
            {
                SetLabelText(_lblInfoSub, error);
                RefreshQuickProxyPanelForCurrentEnvironment();
                return;
            }

            env.Proxy = config?.Name ?? string.Empty;
            env.ProxyStatus = string.IsNullOrWhiteSpace(env.Proxy)
                ? "直连，下次启动生效"
                : (env.BrowserState == 2 || env.BrowserState == 3 ? "已变更，待刷新生效" : "下次启动生效");

            if (config != null)
            {
                TouchProxyUsage(config.Name);
            }

            RenderEnvironment(env);
            RefreshQuickProxyPanelForCurrentEnvironment();
            SetLabelText(_lblInfoSub, $"{FormatEnvironmentProxySummary(env)}   已保存到当前环境");
        }

        private void ApplyQuickProxyNow()
        {
            EnvironmentRecord env = CurrentEnvironment();
            if (env == null)
            {
                return;
            }

            if (!ResolveQuickProxySelection(true, out ProxyConfig config, out string error))
            {
                SetLabelText(_lblInfoSub, error);
                RefreshQuickProxyPanelForCurrentEnvironment();
                return;
            }

            env.Proxy = config?.Name ?? string.Empty;
            if (config != null)
            {
                TouchProxyUsage(config.Name);
            }

            if (env.BrowserState == 2 || env.BrowserState == 3)
            {
                string targetUrl = EnvironmentUrlText(env);
                SetQuickProxyVisible(false);
                CloseEnvironmentBrowser(env);
                EnsureEnvironmentHost(env);
                StartEnvironmentBrowser(env, targetUrl);
                env.ProxyStatus = string.IsNullOrWhiteSpace(env.Proxy) ? "直连" : "已应用";
                RenderEnvironment(env);
                SetLabelText(_lblInfoSub, $"{FormatEnvironmentProxySummary(env)}   已按所选代理重启浏览器");
                return;
            }

            env.ProxyStatus = string.IsNullOrWhiteSpace(env.Proxy) ? "直连，下次启动生效" : "下次启动生效";
            RenderEnvironment(env);
            RefreshQuickProxyPanelForCurrentEnvironment();
            SetLabelText(_lblInfoSub, $"{FormatEnvironmentProxySummary(env)}   浏览器未启动，已保存为下次启动生效");
        }

        private void TestQuickProxySelection()
        {
            EnvironmentRecord env = CurrentEnvironment();
            if (env == null)
            {
                return;
            }

            if (!ResolveQuickProxySelection(false, out ProxyConfig config, out string error))
            {
                SetLabelText(_lblInfoSub, error);
                RefreshQuickProxyPanelForCurrentEnvironment();
                return;
            }

            ProxyCheckResponse result = ExecuteProxyCheck(config, ResolveProxyCheckTargetUrl());
            PersistProxyCheckResult(config.Name, result);
            SetLabelText(_lblInfoSub, $"{config.Name}   测试结果：{result.Status}   {result.SummaryMessage}");
            RefreshQuickProxyPanelForCurrentEnvironment();
        }

        private bool ResolveQuickProxySelection(bool allowDirect, out ProxyConfig config, out string error)
        {
            config = null;
            error = string.Empty;

            string selection = string.IsNullOrWhiteSpace(_quickProxySelectionName) ? DirectProxyOption : _quickProxySelectionName;
            if (IsDirectProxyOption(selection))
            {
                if (allowDirect)
                {
                    return true;
                }

                error = "当前选择的是直连模式，不需要测试代理。";
                return false;
            }

            if (!_proxyConfigs.TryGetValue(selection, out config))
            {
                error = "所选代理不存在，请重新选择。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(config.Host) || config.Port <= 0)
            {
                error = $"代理 {config.Name} 配置不完整，请先去代理管理补全主机和端口。";
                return false;
            }

            if (string.Equals(config.Type, "SOCKS5", StringComparison.OrdinalIgnoreCase)
                && (!string.IsNullOrWhiteSpace(config.User) || !string.IsNullOrWhiteSpace(config.Password)))
            {
                error = "开源版不支持带账号密码的 SOCKS5 代理。";
                return false;
            }

            return true;
        }

        private void OpenProxyManagementFromQuickPanel()
        {
            SetQuickProxyVisible(false);
            if (_moduleNodes.TryGetValue("代理管理", out int nodeId))
            {
                EmojiWindowNative.SetSelectedNode(_tree, nodeId);
                ActivateNode(nodeId);
            }
        }

        private void OnProxyEditorKey(IntPtr hEdit, int keyCode, int keyDown, int shift, int ctrl, int alt)
        {
            if (keyDown != 1 || keyCode != 13)
            {
                return;
            }

            if (hEdit == _editProxySearch)
            {
                ApplyProxyListFilter();
                return;
            }

            if (hEdit == _editProxyQuickImport)
            {
                ParseQuickImportIntoEditor();
            }
        }

        private void ApplyProxyUiTheme(bool dark, uint text, uint muted, uint canvasBg, uint panelBg, uint accent, uint leftBg)
        {
            if (_quickProxyHostPanel != IntPtr.Zero)
            {
                EmojiWindowNative.SetPanelBackgroundColor(_quickProxyHostPanel, GetQuickProxyHostColor());
            }

            if (_quickProxyPanelVisible)
            {
                RefreshQuickProxyPanelForCurrentEnvironment();
            }
        }
    }
}
