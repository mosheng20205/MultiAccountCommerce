using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using EmojiWindowDemo;
using FBroSharp.Lib;

namespace EmojiWindowEcommerceWorkspaceSketchDemo
{
    internal sealed partial class EcommerceWorkspaceSketchApp
    {
        [DataContract]
        private sealed class ProxyConfig
        {
            [DataMember(Order = 1)]
            public string Name { get; set; } = string.Empty;

            [DataMember(Order = 2)]
            public string Type { get; set; } = "HTTP";

            [DataMember(Order = 3)]
            public string Host { get; set; } = string.Empty;

            [DataMember(Order = 4)]
            public int Port { get; set; }

            [DataMember(Order = 5)]
            public string User { get; set; } = string.Empty;

            [DataMember(Order = 6)]
            public string Password { get; set; } = string.Empty;

            [DataMember(Order = 7)]
            public long LastUsedAtTicks { get; set; }

            [DataMember(Order = 8)]
            public long LastCheckAtTicks { get; set; }

            [DataMember(Order = 9)]
            public int LastLatencyMs { get; set; }

            [DataMember(Order = 10)]
            public string LastCheckStatus { get; set; } = string.Empty;

            [DataMember(Order = 11)]
            public string LastCheckMessage { get; set; } = string.Empty;
        }

        [DataContract]
        private sealed class PersistedProxyConfig
        {
            [DataMember(Order = 1)]
            public List<ProxyConfig> Proxies { get; set; } = new List<ProxyConfig>();
        }

        private sealed class ProxyTestResult
        {
            public bool Success { get; set; }
            public int LatencyMs { get; set; }
            public string Status { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
        }

        private const string DirectProxyOption = "__DIRECT__";
        private const int ProxyTypeRadioStyleButton = 2;
        private const int ProxyConnectTimeoutMs = 5000;

        private readonly List<string> _proxyFilteredOrder = new List<string>();
        private IntPtr _lblProxySearchCaption;
        private IntPtr _lblProxyQuickImportCaption;
        private IntPtr _lblProxyListStats;
        private IntPtr _editProxySearch;
        private IntPtr _editProxyQuickImport;
        private int _btnProxyFilter;
        private int _btnProxyParse;
        private int _btnProxyTest;
        private Action _pendingConfirmAction;

        private void InitializeProxyCatalog()
        {
            _proxyConfigs.Clear();
            _proxyOrder.Clear();

            PersistedProxyConfig persisted = LoadProxyConfiguration();
            if (persisted?.Proxies != null && persisted.Proxies.Count > 0)
            {
                foreach (ProxyConfig config in persisted.Proxies)
                {
                    if (string.IsNullOrWhiteSpace(config?.Name))
                    {
                        continue;
                    }

                    ProxyConfig normalized = NormalizeProxyConfig(config);
                    _proxyConfigs[normalized.Name] = normalized;
                    _proxyOrder.Add(normalized.Name);
                }
            }

            foreach (EnvironmentRecord env in _environments.Values)
            {
                if (string.IsNullOrWhiteSpace(env.Proxy) || _proxyConfigs.ContainsKey(env.Proxy))
                {
                    continue;
                }

                ProxyConfig placeholder = new ProxyConfig
                {
                    Name = env.Proxy,
                    Type = "HTTP",
                };
                _proxyConfigs[placeholder.Name] = placeholder;
                _proxyOrder.Add(placeholder.Name);
            }

            if (_proxyOrder.Count == 0)
            {
                ProxyConfig defaultConfig = new ProxyConfig
                {
                    Name = "默认代理",
                    Type = "HTTP",
                };
                _proxyConfigs[defaultConfig.Name] = defaultConfig;
                _proxyOrder.Add(defaultConfig.Name);
            }

            SaveProxyConfiguration();
        }

        private void InitializeProxyManagementUi()
        {
            _proxyManagementPanel = EmojiWindowNative.CreatePanel(_browserCanvas, 0, 0, 100, 100, Argb(255, 249, 251, 253));
            _lblProxyListTitle = Label(_proxyManagementPanel, "代理列表", 14, true);
            _lblProxyListStats = Label(_proxyManagementPanel, string.Empty, 11, false);
            _lblProxySearchCaption = Label(_proxyManagementPanel, "搜索代理", 11, true);
            _lblProxyQuickImportCaption = Label(_proxyManagementPanel, "快速导入", 11, true);
            _lblProxyEditorTitle = Label(_proxyManagementPanel, "代理设置", 14, true);
            _lblProxyNameCaption = Label(_proxyManagementPanel, "代理名称", 12, true);
            _lblProxyTypeCaption = Label(_proxyManagementPanel, "代理类型", 12, true);
            _lblProxyHostCaption = Label(_proxyManagementPanel, "主机地址", 12, true);
            _lblProxyPortCaption = Label(_proxyManagementPanel, "端口", 12, true);
            _lblProxyUserCaption = Label(_proxyManagementPanel, "用户名", 12, true);
            _lblProxyPasswordCaption = Label(_proxyManagementPanel, "密码", 12, true);
            _lblProxyHint = Label(_proxyManagementPanel, "开源版支持 HTTP、HTTP 账号密码代理，以及无认证 SOCKS5。", 12, false);

            _editProxySearch = EditBox(_proxyManagementPanel, string.Empty, false);
            _editProxyQuickImport = EditBox(_proxyManagementPanel, string.Empty, false);
            _editProxyName = EditBox(_proxyManagementPanel, string.Empty, false);
            _editProxyHost = EditBox(_proxyManagementPanel, string.Empty, false);
            _editProxyPort = EditBox(_proxyManagementPanel, string.Empty, false);
            _editProxyUser = EditBox(_proxyManagementPanel, string.Empty, false);
            _editProxyPassword = EditBox(_proxyManagementPanel, string.Empty, false);

            EmojiWindowNative.SetEditBoxKeyCallback(_editProxySearch, _proxyEditKeyCallback);
            EmojiWindowNative.SetEditBoxKeyCallback(_editProxyQuickImport, _proxyEditKeyCallback);

            _proxyListBox = EmojiWindowNative.CreateListBox(_proxyManagementPanel, 0, 0, 100, 100, 0, Argb(255, 31, 41, 55), Argb(255, 255, 255, 255));
            EmojiWindowNative.SetListBoxCallback(_proxyListBox, _proxyListCallback);

            byte[] httpText = U("HTTP");
            byte[] socks5Text = U("SOCKS5");
            _radioHttp = EmojiWindowNative.CreateRadioButton(_proxyManagementPanel, 0, 0, Scale(104), Scale(32), httpText, httpText.Length, 1, 1, ProxyTypeSelectedTextColor(), ProxyTypeSelectedBackColor(), _fontSegoe, _fontSegoe.Length, 12, 1, 0, 0);
            _radioSocks5 = EmojiWindowNative.CreateRadioButton(_proxyManagementPanel, 0, 0, Scale(124), Scale(32), socks5Text, socks5Text.Length, 1, 0, ProxyTypeNormalTextColor(), ProxyTypeNormalBackColor(), _fontSegoe, _fontSegoe.Length, 12, 1, 0, 0);
            EmojiWindowNative.SetRadioButtonStyle(_radioHttp, ProxyTypeRadioStyleButton);
            EmojiWindowNative.SetRadioButtonStyle(_radioSocks5, ProxyTypeRadioStyleButton);
            EmojiWindowNative.SetRadioButtonCallback(_radioHttp, _proxyRadioCallback);
            EmojiWindowNative.SetRadioButtonCallback(_radioSocks5, _proxyRadioCallback);

            _btnProxyTypeHttp = Button(_proxyManagementPanel, "HTTP", ProxyTypeSelectedBackColor(), () => SelectProxyType("HTTP"));
            _btnProxyTypeSocks5 = Button(_proxyManagementPanel, "SOCKS5", ProxyTypeNormalBackColor(), () => SelectProxyType("SOCKS5"));
            _btnProxyFilter = Button(_proxyManagementPanel, "筛选", Argb(255, 8, 145, 178), ApplyProxyListFilter);
            _btnProxyParse = Button(_proxyManagementPanel, "解析", Argb(255, 14, 116, 144), ParseQuickImportIntoEditor);
            _btnProxySave = Button(_proxyManagementPanel, "保存修改", Argb(255, 37, 99, 235), OnSaveProxyConfig);
            _btnProxyAdd = Button(_proxyManagementPanel, "新增代理", Argb(255, 34, 197, 94), OnAddProxy);
            _btnProxyDelete = Button(_proxyManagementPanel, "删除代理", Argb(255, 239, 68, 68), OnDeleteProxy);
            _btnProxyTest = Button(_proxyManagementPanel, "测试代理", Argb(255, 124, 58, 237), TestProxyFromEditor);

            RefreshProxyTypeRadioStyle();
            UpdateProxyTypeHint();
            SetProxyManagementVisible(false);
        }

        private void RenderProxyManagement()
        {
            EnsureProxySelection();
            RefreshProxyListItems();
            BindProxyEditorState(_selectedManagedProxyName);
            SetLabelText(_lblInfoMain, "当前模块：代理管理");
            SetLabelText(_lblInfoSub, "左侧维护代理池，右侧支持搜索、快速导入、测试和编辑。");
            SetWindowTitle("电商多账号浏览器 - 代理管理");
        }

        private void SetProxyManagementVisible(bool visible)
        {
            int show = visible ? SwShow : SwHide;
            if (_proxyManagementPanel != IntPtr.Zero)
            {
                NativeExtras.ShowWindow(_proxyManagementPanel, show);
            }

            foreach (IntPtr label in new[]
            {
                _lblProxyListTitle, _lblProxyListStats, _lblProxySearchCaption, _lblProxyQuickImportCaption,
                _lblProxyEditorTitle, _lblProxyNameCaption, _lblProxyTypeCaption, _lblProxyHostCaption,
                _lblProxyPortCaption, _lblProxyUserCaption, _lblProxyPasswordCaption, _lblProxyHint
            })
            {
                if (label != IntPtr.Zero)
                {
                    EmojiWindowNative.ShowLabel(label, visible ? 1 : 0);
                }
            }

            foreach (IntPtr edit in new[]
            {
                _editProxySearch, _editProxyQuickImport, _editProxyName, _editProxyHost,
                _editProxyPort, _editProxyUser, _editProxyPassword
            })
            {
                if (edit != IntPtr.Zero)
                {
                    EmojiWindowNative.ShowEditBox(edit, visible ? 1 : 0);
                }
            }

            if (_proxyListBox != IntPtr.Zero)
            {
                EmojiWindowNative.ShowListBox(_proxyListBox, visible ? 1 : 0);
            }

            if (_radioHttp != IntPtr.Zero)
            {
                EmojiWindowNative.ShowRadioButton(_radioHttp, 0);
            }

            if (_radioSocks5 != IntPtr.Zero)
            {
                EmojiWindowNative.ShowRadioButton(_radioSocks5, 0);
            }

            foreach (int buttonId in new[]
            {
                _btnProxyTypeHttp, _btnProxyTypeSocks5, _btnProxyFilter, _btnProxyParse,
                _btnProxySave, _btnProxyAdd, _btnProxyDelete, _btnProxyTest
            })
            {
                if (buttonId != 0)
                {
                    EmojiWindowNative.ShowButton(buttonId, visible ? 1 : 0);
                }
            }
        }

        private void EnsureProxySelection()
        {
            if (!string.IsNullOrWhiteSpace(_selectedManagedProxyName) && _proxyConfigs.ContainsKey(_selectedManagedProxyName))
            {
                return;
            }

            _selectedManagedProxyName = _proxyOrder.Count > 0 ? _proxyOrder[0] : string.Empty;
        }

        private void RefreshProxyListItems()
        {
            if (_proxyListBox == IntPtr.Zero)
            {
                return;
            }

            _proxyFilteredOrder.Clear();
            _proxyFilteredOrder.AddRange(GetProxyManagementFilteredOrder());

            for (int i = EmojiWindowNative.GetListItemCount(_proxyListBox) - 1; i >= 0; i--)
            {
                EmojiWindowNative.RemoveListItem(_proxyListBox, i);
            }

            int selectedIndex = -1;
            for (int i = 0; i < _proxyFilteredOrder.Count; i++)
            {
                string proxyName = _proxyFilteredOrder[i];
                string text = DescribeProxyListItem(proxyName);
                byte[] textBytes = U(text);
                EmojiWindowNative.AddListItem(_proxyListBox, textBytes, textBytes.Length);
                if (string.Equals(proxyName, _selectedManagedProxyName, StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = i;
                }
            }

            if (selectedIndex >= 0)
            {
                EmojiWindowNative.SetSelectedIndex(_proxyListBox, selectedIndex);
            }

            if (_lblProxyListStats != IntPtr.Zero)
            {
                SetLabelText(_lblProxyListStats, $"共 {_proxyFilteredOrder.Count} 个代理，已维护 {_proxyOrder.Count} 个。");
            }
        }

        private IEnumerable<string> GetProxyManagementFilteredOrder()
        {
            string keyword = (_editProxySearch != IntPtr.Zero ? GetEditText(_editProxySearch) : string.Empty).Trim();
            foreach (string proxyName in _proxyOrder)
            {
                if (!_proxyConfigs.TryGetValue(proxyName, out ProxyConfig config))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(keyword))
                {
                    yield return proxyName;
                    continue;
                }

                string haystack = $"{config.Name} {config.Type} {config.Host} {config.Port} {config.User}".ToLowerInvariant();
                if (haystack.Contains(keyword.ToLowerInvariant()))
                {
                    yield return proxyName;
                }
            }
        }

        private void BindProxyEditorState(string proxyName)
        {
            if (string.IsNullOrWhiteSpace(proxyName) || !_proxyConfigs.TryGetValue(proxyName, out ProxyConfig config))
            {
                SetEditText(_editProxyName, string.Empty);
                SetEditText(_editProxyHost, string.Empty);
                SetEditText(_editProxyPort, string.Empty);
                SetEditText(_editProxyUser, string.Empty);
                SetEditText(_editProxyPassword, string.Empty);
                EmojiWindowNative.SetRadioButtonState(_radioHttp, 1);
                EmojiWindowNative.SetRadioButtonState(_radioSocks5, 0);
                RefreshProxyTypeRadioStyle();
                UpdateProxyTypeHint();
                return;
            }

            _selectedManagedProxyName = proxyName;
            SetEditText(_editProxyName, config.Name);
            SetEditText(_editProxyHost, config.Host);
            SetEditText(_editProxyPort, config.Port > 0 ? config.Port.ToString() : string.Empty);
            SetEditText(_editProxyUser, config.User);
            SetEditText(_editProxyPassword, config.Password);
            bool isSocks5 = string.Equals(config.Type, "SOCKS5", StringComparison.OrdinalIgnoreCase);
            EmojiWindowNative.SetRadioButtonState(_radioHttp, isSocks5 ? 0 : 1);
            EmojiWindowNative.SetRadioButtonState(_radioSocks5, isSocks5 ? 1 : 0);
            RefreshProxyTypeRadioStyle();
            UpdateProxyTypeHint();
        }

        private void LayoutProxyManagementPage()
        {
            if (_proxyManagementPanel == IntPtr.Zero || _browserCanvasWidth <= 0 || _browserCanvasHeight <= 0)
            {
                return;
            }

            Move(_proxyManagementPanel, 0, 0, _browserCanvasWidth, _browserCanvasHeight);

            int outer = Scale(24);
            int gap = Scale(28);
            int listWidth = Math.Max(Scale(300), Math.Min(Scale(380), _browserCanvasWidth / 3));
            int editorX = outer + listWidth + gap;
            int editorWidth = Math.Max(Scale(420), _browserCanvasWidth - editorX - outer);
            int titleHeight = Scale(28);
            int labelHeight = Scale(24);
            int editHeight = Scale(38);
            int rowGap = Scale(12);
            int buttonHeight = Scale(40);
            int buttonWidth = Scale(116);

            EmojiWindowNative.SetLabelBounds(_lblProxyListTitle, outer, outer, listWidth - Scale(90), titleHeight);
            EmojiWindowNative.SetLabelBounds(_lblProxyListStats, outer, outer + Scale(28), listWidth, Scale(20));
            EmojiWindowNative.SetLabelBounds(_lblProxySearchCaption, outer, outer + Scale(58), listWidth, Scale(22));
            EmojiWindowNative.SetEditBoxBounds(_editProxySearch, outer, outer + Scale(84), listWidth - Scale(96), editHeight);
            EmojiWindowNative.SetButtonBounds(_btnProxyFilter, outer + listWidth - Scale(84), outer + Scale(84), Scale(84), editHeight);
            EmojiWindowNative.SetListBoxBounds(_proxyListBox, outer, outer + Scale(136), listWidth, Math.Max(Scale(260), _browserCanvasHeight - outer - Scale(160)));

            EmojiWindowNative.SetLabelBounds(_lblProxyEditorTitle, editorX, outer, editorWidth, titleHeight);
            EmojiWindowNative.SetLabelBounds(_lblProxyQuickImportCaption, editorX, outer + Scale(38), editorWidth, labelHeight);
            EmojiWindowNative.SetEditBoxBounds(_editProxyQuickImport, editorX, outer + Scale(64), editorWidth - Scale(94), editHeight);
            EmojiWindowNative.SetButtonBounds(_btnProxyParse, editorX + editorWidth - Scale(82), outer + Scale(64), Scale(82), editHeight);

            EmojiWindowNative.SetLabelBounds(_lblProxyNameCaption, editorX, outer + Scale(118), editorWidth, labelHeight);
            EmojiWindowNative.SetEditBoxBounds(_editProxyName, editorX, outer + Scale(144), editorWidth, editHeight);

            EmojiWindowNative.SetLabelBounds(_lblProxyTypeCaption, editorX, outer + Scale(198), Scale(140), labelHeight);
            int radioY = outer + Scale(226);
            int radioHttpWidth = Scale(100);
            int radioSocks5Width = Scale(120);
            int radioGap = Scale(10);
            EmojiWindowNative.SetRadioButtonBounds(_radioHttp, -1000, -1000, radioHttpWidth, editHeight);
            EmojiWindowNative.SetRadioButtonBounds(_radioSocks5, -1000, -1000, radioSocks5Width, editHeight);
            EmojiWindowNative.SetButtonBounds(_btnProxyTypeHttp, editorX, radioY, radioHttpWidth, editHeight);
            EmojiWindowNative.SetButtonBounds(_btnProxyTypeSocks5, editorX + radioHttpWidth + radioGap, radioY, radioSocks5Width, editHeight);

            int hostX = editorX + radioHttpWidth + radioSocks5Width + radioGap + rowGap;
            int hostWidth = editorWidth - (hostX - editorX);
            EmojiWindowNative.SetLabelBounds(_lblProxyHostCaption, hostX, outer + Scale(198), hostWidth, labelHeight);
            EmojiWindowNative.SetEditBoxBounds(_editProxyHost, hostX, outer + Scale(226), hostWidth, editHeight);

            EmojiWindowNative.SetLabelBounds(_lblProxyPortCaption, editorX, outer + Scale(280), Scale(120), labelHeight);
            EmojiWindowNative.SetEditBoxBounds(_editProxyPort, editorX, outer + Scale(306), Scale(120), editHeight);

            int credsX = editorX + Scale(132);
            int credsWidth = editorWidth - Scale(132);
            int userWidth = Math.Max(Scale(160), (credsWidth - rowGap) / 2);
            int passwordX = credsX + userWidth + rowGap;
            int passwordWidth = editorWidth - (passwordX - editorX);
            EmojiWindowNative.SetLabelBounds(_lblProxyUserCaption, credsX, outer + Scale(280), userWidth, labelHeight);
            EmojiWindowNative.SetEditBoxBounds(_editProxyUser, credsX, outer + Scale(306), userWidth, editHeight);
            EmojiWindowNative.SetLabelBounds(_lblProxyPasswordCaption, passwordX, outer + Scale(280), passwordWidth, labelHeight);
            EmojiWindowNative.SetEditBoxBounds(_editProxyPassword, passwordX, outer + Scale(306), passwordWidth, editHeight);

            EmojiWindowNative.SetLabelBounds(_lblProxyHint, editorX, outer + Scale(360), editorWidth, Scale(54));

            int buttonY = outer + Scale(432);
            EmojiWindowNative.SetButtonBounds(_btnProxySave, editorX, buttonY, buttonWidth, buttonHeight);
            EmojiWindowNative.SetButtonBounds(_btnProxyAdd, editorX + buttonWidth + rowGap, buttonY, buttonWidth, buttonHeight);
            EmojiWindowNative.SetButtonBounds(_btnProxyDelete, editorX + (buttonWidth + rowGap) * 2, buttonY, buttonWidth, buttonHeight);
            EmojiWindowNative.SetButtonBounds(_btnProxyTest, editorX + (buttonWidth + rowGap) * 3, buttonY, buttonWidth, buttonHeight);
        }

        private void OnProxyListSelected(IntPtr hListBox, int index)
        {
            if (index < 0 || index >= _proxyFilteredOrder.Count)
            {
                return;
            }

            _selectedManagedProxyName = _proxyFilteredOrder[index];
            BindProxyEditorState(_selectedManagedProxyName);
        }

        private void OnProxyTypeChanged(IntPtr hComboBox, int index)
        {
            UpdateProxyTypeHint();
        }

        private void OnProxyTypeRadioChanged(IntPtr hRadioButton, int groupId, int checkedState)
        {
            RefreshProxyTypeRadioStyle();
            if (checkedState == 1)
            {
                UpdateProxyTypeHint();
            }
        }

        private void SelectProxyType(string type)
        {
            bool isSocks5 = string.Equals(type, "SOCKS5", StringComparison.OrdinalIgnoreCase);
            EmojiWindowNative.SetRadioButtonState(_radioHttp, isSocks5 ? 0 : 1);
            EmojiWindowNative.SetRadioButtonState(_radioSocks5, isSocks5 ? 1 : 0);
            RefreshProxyTypeRadioStyle();
            UpdateProxyTypeHint();
        }

        private void RefreshProxyTypeRadioStyle()
        {
            bool httpSelected = _radioHttp != IntPtr.Zero && EmojiWindowNative.GetRadioButtonState(_radioHttp) == 1;
            bool socksSelected = _radioSocks5 != IntPtr.Zero && EmojiWindowNative.GetRadioButtonState(_radioSocks5) == 1;
            SetProxyRadioButtonVisual(_radioHttp, httpSelected);
            SetProxyRadioButtonVisual(_radioSocks5, socksSelected);
            SetProxyTypeButtonVisual(_btnProxyTypeHttp, httpSelected);
            SetProxyTypeButtonVisual(_btnProxyTypeSocks5, socksSelected);
        }

        private void SetProxyRadioButtonVisual(IntPtr radio, bool selected)
        {
            if (radio == IntPtr.Zero)
            {
                return;
            }

            EmojiWindowNative.SetRadioButtonStyle(radio, ProxyTypeRadioStyleButton);
            EmojiWindowNative.SetRadioButtonColor(
                radio,
                selected ? ProxyTypeSelectedTextColor() : ProxyTypeNormalTextColor(),
                selected ? ProxyTypeSelectedBackColor() : ProxyTypeNormalBackColor());
        }

        private void SetProxyTypeButtonVisual(int buttonId, bool selected)
        {
            if (buttonId == 0)
            {
                return;
            }

            uint bg = selected ? ProxyTypeSelectedBackColor() : ProxyTypeNormalBackColor();
            uint border = selected ? ProxyTypeSelectedBorderColor() : ProxyTypeNormalBorderColor();
            uint text = selected ? ProxyTypeSelectedTextColor() : ProxyTypeNormalTextColor();
            EmojiWindowNative.SetButtonStyle(buttonId, 0);
            EmojiWindowNative.SetButtonSize(buttonId, 1);
            EmojiWindowNative.SetButtonRound(buttonId, 0);
            EmojiWindowNative.SetButtonBackgroundColor(buttonId, bg);
            EmojiWindowNative.SetButtonBorderColor(buttonId, border);
            EmojiWindowNative.SetButtonTextColor(buttonId, text);
            EmojiWindowNative.SetButtonHoverColors(buttonId, selected ? ShiftColor(bg, 6) : Argb(255, 239, 246, 255), border, text);
        }

        private uint ProxyTypeSelectedTextColor()
        {
            return Argb(255, 255, 255, 255);
        }

        private uint ProxyTypeSelectedBackColor()
        {
            return Argb(255, 37, 99, 235);
        }

        private uint ProxyTypeSelectedBorderColor()
        {
            return Argb(255, 29, 78, 216);
        }

        private uint ProxyTypeNormalTextColor()
        {
            return Argb(255, 37, 99, 235);
        }

        private uint ProxyTypeNormalBackColor()
        {
            return Argb(255, 248, 251, 255);
        }

        private uint ProxyTypeNormalBorderColor()
        {
            return Argb(255, 191, 219, 254);
        }

        private void UpdateProxyTypeHint()
        {
            bool isSocks5 = string.Equals(SelectedProxyType(), "SOCKS5", StringComparison.OrdinalIgnoreCase);
            if (_editProxyUser != IntPtr.Zero)
            {
                EmojiWindowNative.EnableEditBox(_editProxyUser, isSocks5 ? 0 : 1);
            }

            if (_editProxyPassword != IntPtr.Zero)
            {
                EmojiWindowNative.EnableEditBox(_editProxyPassword, isSocks5 ? 0 : 1);
            }

            if (isSocks5)
            {
                SetLabelText(_lblProxyHint, "SOCKS5 在开源版仅支持无认证代理。用户名和密码会被忽略。");
                return;
            }

            SetLabelText(_lblProxyHint, "HTTP 代理支持无认证或账号密码认证。可先测试，再保存到代理池。");
        }

        private string SelectedProxyType()
        {
            bool isSocks5 = _radioSocks5 != IntPtr.Zero && EmojiWindowNative.GetRadioButtonState(_radioSocks5) == 1;
            return isSocks5 ? "SOCKS5" : "HTTP";
        }

        private void OnSaveProxyConfig()
        {
            SaveProxyManagementChanges();
        }

        private void OnAddProxy()
        {
            AddManagedProxy();
        }

        private void OnDeleteProxy()
        {
            DeleteManagedProxy();
        }

        private void SaveProxyManagementChanges()
        {
            if (string.IsNullOrWhiteSpace(_selectedManagedProxyName) || !_proxyConfigs.ContainsKey(_selectedManagedProxyName))
            {
                SetLabelText(_lblInfoSub, "请先在代理列表中选中一个代理。");
                return;
            }

            if (!TryBuildProxyFromEditor(out ProxyConfig updated, out string error))
            {
                SetLabelText(_lblInfoSub, error);
                return;
            }

            string oldName = _selectedManagedProxyName;
            if (!string.Equals(oldName, updated.Name, StringComparison.OrdinalIgnoreCase) && _proxyConfigs.ContainsKey(updated.Name))
            {
                SetLabelText(_lblInfoSub, $"代理 {updated.Name} 已存在。");
                return;
            }

            ProxyConfig existing = _proxyConfigs[oldName];
            updated.LastUsedAtTicks = existing.LastUsedAtTicks;
            updated.LastCheckAtTicks = existing.LastCheckAtTicks;
            updated.LastLatencyMs = existing.LastLatencyMs;
            updated.LastCheckStatus = existing.LastCheckStatus;
            updated.LastCheckMessage = existing.LastCheckMessage;

            if (!string.Equals(oldName, updated.Name, StringComparison.OrdinalIgnoreCase))
            {
                int index = _proxyOrder.IndexOf(oldName);
                if (index >= 0)
                {
                    _proxyOrder[index] = updated.Name;
                }

                _proxyConfigs.Remove(oldName);
                foreach (EnvironmentRecord env in _environments.Values)
                {
                    if (string.Equals(env.Proxy, oldName, StringComparison.OrdinalIgnoreCase))
                    {
                        env.Proxy = updated.Name;
                    }
                }

                if (string.Equals(_quickProxySelectionName, oldName, StringComparison.OrdinalIgnoreCase))
                {
                    _quickProxySelectionName = updated.Name;
                }
            }

            _proxyConfigs[updated.Name] = updated;
            _selectedManagedProxyName = updated.Name;
            SaveProxyConfiguration();
            BindProxyEditorState(updated.Name);
            RefreshProxyListItems();
            RefreshQuickProxyPanelForCurrentEnvironment();
            SetLabelText(_lblInfoSub, $"代理 {updated.Name} 已保存。");

            if (_currentEnvId.HasValue && _environments.TryGetValue(_currentEnvId.Value, out EnvironmentRecord currentEnv))
            {
                RenderEnvironment(currentEnv);
            }
        }

        private void AddManagedProxy()
        {
            if (!TryBuildProxyFromEditor(out ProxyConfig config, out string error))
            {
                SetLabelText(_lblInfoSub, error);
                return;
            }

            if (_proxyConfigs.ContainsKey(config.Name))
            {
                SetLabelText(_lblInfoSub, $"代理 {config.Name} 已存在。");
                return;
            }

            _proxyConfigs[config.Name] = config;
            _proxyOrder.Add(config.Name);
            _selectedManagedProxyName = config.Name;
            TouchProxyUsage(config.Name, saveImmediately: false);
            SaveProxyConfiguration();
            RefreshProxyListItems();
            BindProxyEditorState(config.Name);
            RefreshQuickProxyPanelForCurrentEnvironment();
            SetLabelText(_lblInfoSub, $"已新增代理 {config.Name}。");
        }

        private void DeleteManagedProxy()
        {
            if (string.IsNullOrWhiteSpace(_selectedManagedProxyName) || !_proxyConfigs.ContainsKey(_selectedManagedProxyName))
            {
                SetLabelText(_lblInfoSub, "请先选中一个代理再删除。");
                return;
            }

            List<string> usages = _environments.Values
                .Where(env => string.Equals(env.Proxy, _selectedManagedProxyName, StringComparison.OrdinalIgnoreCase))
                .Select(env => $"{env.GroupName} / {env.Name}")
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (usages.Count > 0)
            {
                ShowMessageBox(
                    "无法删除代理",
                    $"代理 {_selectedManagedProxyName} 仍被 {usages.Count} 个环境使用：\n{string.Join("\n", usages.Take(8))}{(usages.Count > 8 ? "\n..." : string.Empty)}");
                SetLabelText(_lblInfoSub, $"代理 {_selectedManagedProxyName} 仍被 {usages.Count} 个环境使用，无法删除。");
                return;
            }

            string deletingName = _selectedManagedProxyName;
            ShowConfirmBox(
                "删除代理",
                $"确定要删除代理 {deletingName} 吗？删除后将从代理池移除。",
                () => PerformDeleteManagedProxy(deletingName));
        }

        private void PerformDeleteManagedProxy(string proxyName)
        {
            if (string.IsNullOrWhiteSpace(proxyName) || !_proxyConfigs.ContainsKey(proxyName))
            {
                return;
            }

            _proxyConfigs.Remove(proxyName);
            _proxyOrder.Remove(proxyName);
            if (string.Equals(_quickProxySelectionName, proxyName, StringComparison.OrdinalIgnoreCase))
            {
                _quickProxySelectionName = string.Empty;
            }

            _selectedManagedProxyName = _proxyOrder.Count > 0 ? _proxyOrder[0] : string.Empty;
            SaveProxyConfiguration();
            RefreshProxyListItems();
            BindProxyEditorState(_selectedManagedProxyName);
            RefreshQuickProxyPanelForCurrentEnvironment();
            SetLabelText(_lblInfoSub, $"已删除代理 {proxyName}。");
        }

        private bool TryBuildProxyFromEditor(out ProxyConfig config, out string error)
        {
            config = null;
            error = string.Empty;

            string name = GetEditText(_editProxyName).Trim();
            string type = SelectedProxyType();
            string host = GetEditText(_editProxyHost).Trim();
            string portText = GetEditText(_editProxyPort).Trim();
            string user = GetEditText(_editProxyUser).Trim();
            string password = GetEditText(_editProxyPassword).Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                error = "代理名称不能为空。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(host))
            {
                error = "代理主机不能为空。";
                return false;
            }

            if (!int.TryParse(portText, out int port) || port <= 0 || port > 65535)
            {
                error = "代理端口必须是 1-65535 之间的整数。";
                return false;
            }

            if (type == "SOCKS5" && (!string.IsNullOrWhiteSpace(user) || !string.IsNullOrWhiteSpace(password)))
            {
                error = "开源版不支持带账号密码的 SOCKS5 代理。";
                return false;
            }

            config = new ProxyConfig
            {
                Name = name,
                Type = type,
                Host = host,
                Port = port,
                User = user,
                Password = password,
            };
            return true;
        }

        private ProxyConfig NormalizeProxyConfig(ProxyConfig config)
        {
            return new ProxyConfig
            {
                Name = config?.Name?.Trim() ?? string.Empty,
                Type = string.Equals(config?.Type, "SOCKS5", StringComparison.OrdinalIgnoreCase) ? "SOCKS5" : "HTTP",
                Host = config?.Host?.Trim() ?? string.Empty,
                Port = config?.Port ?? 0,
                User = config?.User?.Trim() ?? string.Empty,
                Password = config?.Password?.Trim() ?? string.Empty,
                LastUsedAtTicks = config?.LastUsedAtTicks ?? 0,
                LastCheckAtTicks = config?.LastCheckAtTicks ?? 0,
                LastLatencyMs = config?.LastLatencyMs ?? 0,
                LastCheckStatus = config?.LastCheckStatus?.Trim() ?? string.Empty,
                LastCheckMessage = config?.LastCheckMessage?.Trim() ?? string.Empty,
            };
        }

        private string ProxyConfigPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "proxies.json");
        }

        private PersistedProxyConfig LoadProxyConfiguration()
        {
            string path = ProxyConfigPath();
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                using (FileStream stream = File.OpenRead(path))
                {
                    var serializer = new DataContractJsonSerializer(typeof(PersistedProxyConfig));
                    return serializer.ReadObject(stream) as PersistedProxyConfig;
                }
            }
            catch
            {
                return null;
            }
        }

        private void SaveProxyConfiguration()
        {
            PersistedProxyConfig config = new PersistedProxyConfig();
            foreach (string proxyName in _proxyOrder)
            {
                if (_proxyConfigs.TryGetValue(proxyName, out ProxyConfig proxy))
                {
                    config.Proxies.Add(proxy);
                }
            }

            using (FileStream stream = File.Create(ProxyConfigPath()))
            {
                var serializer = new DataContractJsonSerializer(typeof(PersistedProxyConfig));
                serializer.WriteObject(stream, config);
            }
        }

        private ProxyConfig ResolveProxyConfig(EnvironmentRecord env)
        {
            if (env == null || string.IsNullOrWhiteSpace(env.Proxy))
            {
                return null;
            }

            return _proxyConfigs.TryGetValue(env.Proxy, out ProxyConfig config) ? config : null;
        }

        private bool ApplyProxyToBrowser(EnvironmentRecord env, IFBroSharpBrowser browser, bool updateStatus)
        {
            if (env == null || browser == null || !browser.IsValid)
            {
                return false;
            }

            ProxyConfig config = ResolveProxyConfig(env);
            if (config == null)
            {
                env.ProxyStatus = "直连";
                if (updateStatus)
                {
                    SetLabelText(_lblInfoSub, $"{FormatEnvironmentProxySummary(env)}   已切换为直连");
                }
                return true;
            }

            if (string.IsNullOrWhiteSpace(config.Host) || config.Port <= 0)
            {
                env.ProxyStatus = "配置不完整";
                if (updateStatus)
                {
                    SetLabelText(_lblInfoSub, $"{FormatEnvironmentProxySummary(env)}   代理配置不完整，当前仍为直连");
                }
                return true;
            }

            string proxyUrl = $"{(config.Type == "SOCKS5" ? "socks5" : "http")}://{config.Host}:{config.Port}";
            if (config.Type == "SOCKS5")
            {
                browser.SetProxy(proxyUrl);
            }
            else if (!string.IsNullOrWhiteSpace(config.User) || !string.IsNullOrWhiteSpace(config.Password))
            {
                browser.SetProxy(proxyUrl, config.User, config.Password);
            }
            else
            {
                browser.SetProxy(proxyUrl);
            }

            env.ProxyStatus = "已应用";
            TouchProxyUsage(config.Name, saveImmediately: false);
            SaveProxyConfiguration();

            if (updateStatus)
            {
                SetLabelText(_lblInfoSub, $"{FormatEnvironmentProxySummary(env)}   已应用代理：{proxyUrl}");
            }
            return true;
        }

        private string CurrentProxyDisplayName(EnvironmentRecord env)
        {
            return env == null || string.IsNullOrWhiteSpace(env.Proxy) ? "未设置" : env.Proxy;
        }

        private string FormatEnvironmentProxySummary(EnvironmentRecord env)
        {
            if (env == null)
            {
                return string.Empty;
            }

            string status = string.IsNullOrWhiteSpace(env.ProxyStatus)
                ? (string.IsNullOrWhiteSpace(env.Proxy) ? "直连（待启动）" : "待启动")
                : env.ProxyStatus;
            return $"域名：{env.Domain}   当前代理：{CurrentProxyDisplayName(env)}   代理状态：{status}";
        }

        private void UpdateProxyToolbarButtonText(EnvironmentRecord env)
        {
            if (_toolbarButtons.Count <= 5)
            {
                return;
            }

            string proxyName = CurrentProxyDisplayName(env);
            if (proxyName.Length > 16)
            {
                proxyName = proxyName.Substring(0, 16) + "...";
            }

            SetButtonText(_toolbarButtons[5], $"代理设置: {proxyName}");
        }

        private string DescribeProxyListItem(string proxyName)
        {
            if (!_proxyConfigs.TryGetValue(proxyName, out ProxyConfig config))
            {
                return proxyName;
            }

            string endpoint = string.IsNullOrWhiteSpace(config.Host) || config.Port <= 0 ? "未配置" : $"{config.Host}:{config.Port}";
            string check = string.IsNullOrWhiteSpace(config.LastCheckStatus) ? "未测试" : config.LastCheckStatus;
            return $"{config.Name}   {config.Type}   {endpoint}   {check}";
        }

        private string DescribeProxyPickerItem(string proxyName)
        {
            if (IsDirectProxyOption(proxyName))
            {
                return "不使用代理（直连）";
            }

            if (!_proxyConfigs.TryGetValue(proxyName, out ProxyConfig config))
            {
                return proxyName;
            }

            string endpoint = string.IsNullOrWhiteSpace(config.Host) || config.Port <= 0 ? "未配置" : $"{config.Host}:{config.Port}";
            return $"{config.Name}   {config.Type}   {endpoint}";
        }

        private bool IsDirectProxyOption(string proxyName)
        {
            return string.Equals(proxyName, DirectProxyOption, StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyProxyListFilter()
        {
            RefreshProxyListItems();
        }

        private void ParseQuickImportIntoEditor()
        {
            string raw = GetEditText(_editProxyQuickImport).Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                SetLabelText(_lblInfoSub, "请先粘贴代理字符串，再点击解析。");
                return;
            }

            if (!TryParseProxyInput(raw, out ProxyConfig config, out string error))
            {
                SetLabelText(_lblInfoSub, error);
                return;
            }

            if (string.IsNullOrWhiteSpace(GetEditText(_editProxyName)))
            {
                string autoName = $"{config.Type}-{config.Host}:{config.Port}";
                SetEditText(_editProxyName, autoName);
            }

            SetEditText(_editProxyHost, config.Host);
            SetEditText(_editProxyPort, config.Port.ToString());
            SetEditText(_editProxyUser, config.User);
            SetEditText(_editProxyPassword, config.Password);
            SelectProxyType(config.Type);
            SetLabelText(_lblInfoSub, $"已解析代理：{config.Type} {config.Host}:{config.Port}");
        }

        private bool TryParseProxyInput(string raw, out ProxyConfig config, out string error)
        {
            config = null;
            error = string.Empty;

            string value = raw.Trim();
            string type = "HTTP";
            string host = string.Empty;
            int port = 0;
            string user = string.Empty;
            string password = string.Empty;

            if (Uri.TryCreate(value, UriKind.Absolute, out Uri uri) && !string.IsNullOrWhiteSpace(uri.Host))
            {
                type = string.Equals(uri.Scheme, "socks5", StringComparison.OrdinalIgnoreCase) ? "SOCKS5" : "HTTP";
                host = uri.Host;
                port = uri.Port;
                if (!string.IsNullOrWhiteSpace(uri.UserInfo))
                {
                    string[] userInfo = uri.UserInfo.Split(new[] { ':' }, 2);
                    user = Uri.UnescapeDataString(userInfo[0]);
                    if (userInfo.Length > 1)
                    {
                        password = Uri.UnescapeDataString(userInfo[1]);
                    }
                }
            }
            else
            {
                string[] parts = value.Split(':');
                if (parts.Length == 2 || parts.Length == 4)
                {
                    host = parts[0].Trim();
                    int.TryParse(parts[1].Trim(), out port);
                    if (parts.Length == 4)
                    {
                        user = parts[2].Trim();
                        password = parts[3].Trim();
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(host) || port <= 0 || port > 65535)
            {
                error = "无法解析代理。支持 host:port、host:port:user:pass、http://user:pass@host:port、socks5://host:port。";
                return false;
            }

            if (type == "SOCKS5" && (!string.IsNullOrWhiteSpace(user) || !string.IsNullOrWhiteSpace(password)))
            {
                error = "开源版不支持带账号密码的 SOCKS5 代理。";
                return false;
            }

            config = new ProxyConfig
            {
                Type = type,
                Host = host,
                Port = port,
                User = user,
                Password = password,
            };
            return true;
        }

        private void TestProxyFromEditor()
        {
            if (!TryBuildProxyFromEditor(out ProxyConfig config, out string error))
            {
                SetLabelText(_lblInfoSub, error);
                return;
            }

            ProxyTestResult result = TestProxyConfiguration(config);
            if (_proxyConfigs.TryGetValue(config.Name, out ProxyConfig stored))
            {
                stored.LastCheckAtTicks = DateTime.UtcNow.Ticks;
                stored.LastLatencyMs = result.LatencyMs;
                stored.LastCheckStatus = result.Status;
                stored.LastCheckMessage = result.Message;
                SaveProxyConfiguration();
                RefreshProxyListItems();
                RefreshQuickProxyPanelForCurrentEnvironment();
            }

            SetLabelText(_lblInfoSub, $"{config.Name}   测试结果：{result.Status}   {result.Message}");
        }

        private ProxyTestResult TestProxyConfiguration(ProxyConfig config)
        {
            if (config == null)
            {
                return new ProxyTestResult
                {
                    Success = false,
                    Status = "测试失败",
                    Message = "代理配置为空。",
                };
            }

            ProxyTestResult result = string.Equals(config.Type, "SOCKS5", StringComparison.OrdinalIgnoreCase)
                ? TestSocks5Proxy(config)
                : TestHttpProxy(config);
            result.Status = result.Success ? "连通成功" : "连通失败";
            return result;
        }

        private ProxyTestResult TestHttpProxy(ProxyConfig config)
        {
            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    if (!client.ConnectAsync(config.Host, config.Port).Wait(ProxyConnectTimeoutMs))
                    {
                        return new ProxyTestResult
                        {
                            Success = false,
                            LatencyMs = ProxyConnectTimeoutMs,
                            Message = "连接代理服务器超时。",
                        };
                    }

                    client.ReceiveTimeout = ProxyConnectTimeoutMs;
                    client.SendTimeout = ProxyConnectTimeoutMs;
                    using (NetworkStream stream = client.GetStream())
                    {
                        StringBuilder builder = new StringBuilder();
                        builder.Append("CONNECT 1.1.1.1:80 HTTP/1.1\r\n");
                        builder.Append("Host: 1.1.1.1:80\r\n");
                        builder.Append("Proxy-Connection: Keep-Alive\r\n");
                        if (!string.IsNullOrWhiteSpace(config.User) || !string.IsNullOrWhiteSpace(config.Password))
                        {
                            string auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.User}:{config.Password}"));
                            builder.Append($"Proxy-Authorization: Basic {auth}\r\n");
                        }
                        builder.Append("\r\n");

                        byte[] payload = Encoding.ASCII.GetBytes(builder.ToString());
                        stream.Write(payload, 0, payload.Length);

                        byte[] buffer = new byte[512];
                        int read = stream.Read(buffer, 0, buffer.Length);
                        string response = read > 0 ? Encoding.ASCII.GetString(buffer, 0, read) : string.Empty;
                        watch.Stop();

                        if (response.Contains("200"))
                        {
                            return new ProxyTestResult
                            {
                                Success = true,
                                LatencyMs = (int)watch.ElapsedMilliseconds,
                                Message = $"HTTP CONNECT 成功，延迟 {watch.ElapsedMilliseconds}ms。",
                            };
                        }

                        if (response.Contains("407"))
                        {
                            return new ProxyTestResult
                            {
                                Success = false,
                                LatencyMs = (int)watch.ElapsedMilliseconds,
                                Message = "代理认证失败，请检查用户名或密码。",
                            };
                        }

                        return new ProxyTestResult
                        {
                            Success = false,
                            LatencyMs = (int)watch.ElapsedMilliseconds,
                            Message = string.IsNullOrWhiteSpace(response) ? "未收到有效响应。" : $"代理响应异常：{response.Split('\n')[0].Trim()}",
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                watch.Stop();
                return new ProxyTestResult
                {
                    Success = false,
                    LatencyMs = (int)watch.ElapsedMilliseconds,
                    Message = $"连接异常：{ex.Message}",
                };
            }
        }

        private ProxyTestResult TestSocks5Proxy(ProxyConfig config)
        {
            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    if (!client.ConnectAsync(config.Host, config.Port).Wait(ProxyConnectTimeoutMs))
                    {
                        return new ProxyTestResult
                        {
                            Success = false,
                            LatencyMs = ProxyConnectTimeoutMs,
                            Message = "连接代理服务器超时。",
                        };
                    }

                    client.ReceiveTimeout = ProxyConnectTimeoutMs;
                    client.SendTimeout = ProxyConnectTimeoutMs;
                    using (NetworkStream stream = client.GetStream())
                    {
                        byte[] hello = { 0x05, 0x01, 0x00 };
                        stream.Write(hello, 0, hello.Length);

                        byte[] response = new byte[2];
                        ReadExact(stream, response, response.Length);
                        if (response[0] != 0x05 || response[1] != 0x00)
                        {
                            watch.Stop();
                            return new ProxyTestResult
                            {
                                Success = false,
                                LatencyMs = (int)watch.ElapsedMilliseconds,
                                Message = "SOCKS5 握手失败或代理要求认证。",
                            };
                        }

                        byte[] connect = { 0x05, 0x01, 0x00, 0x01, 0x01, 0x01, 0x01, 0x01, 0x00, 0x50 };
                        stream.Write(connect, 0, connect.Length);

                        byte[] head = new byte[4];
                        ReadExact(stream, head, head.Length);
                        if (head[1] != 0x00)
                        {
                            watch.Stop();
                            return new ProxyTestResult
                            {
                                Success = false,
                                LatencyMs = (int)watch.ElapsedMilliseconds,
                                Message = $"SOCKS5 CONNECT 被拒绝，错误码 {head[1]}。",
                            };
                        }

                        int addressLength = head[3] switch
                        {
                            0x01 => 4,
                            0x04 => 16,
                            0x03 => stream.ReadByte(),
                            _ => 0
                        };
                        if (addressLength > 0)
                        {
                            byte[] tail = new byte[addressLength + 2];
                            ReadExact(stream, tail, tail.Length);
                        }

                        watch.Stop();
                        return new ProxyTestResult
                        {
                            Success = true,
                            LatencyMs = (int)watch.ElapsedMilliseconds,
                            Message = $"SOCKS5 握手成功，延迟 {watch.ElapsedMilliseconds}ms。",
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                watch.Stop();
                return new ProxyTestResult
                {
                    Success = false,
                    LatencyMs = (int)watch.ElapsedMilliseconds,
                    Message = $"连接异常：{ex.Message}",
                };
            }
        }

        private void ReadExact(NetworkStream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                {
                    throw new IOException("代理连接被中断。");
                }
                offset += read;
            }
        }

        private void TouchProxyUsage(string proxyName, bool saveImmediately = true)
        {
            if (string.IsNullOrWhiteSpace(proxyName) || !_proxyConfigs.TryGetValue(proxyName, out ProxyConfig config))
            {
                return;
            }

            config.LastUsedAtTicks = DateTime.UtcNow.Ticks;
            if (saveImmediately)
            {
                SaveProxyConfiguration();
            }
        }

        private IEnumerable<string> GetRecentProxyNames(int count)
        {
            return _proxyConfigs.Values
                .Where(config => !string.IsNullOrWhiteSpace(config.Name))
                .OrderByDescending(config => config.LastUsedAtTicks)
                .ThenBy(config => config.Name, StringComparer.OrdinalIgnoreCase)
                .Select(config => config.Name)
                .Take(count);
        }

        private void ShowMessageBox(string title, string message)
        {
            byte[] titleBytes = U(title);
            byte[] messageBytes = U(message);
            EmojiWindowNative.show_message_box_bytes(_window, titleBytes, titleBytes.Length, messageBytes, messageBytes.Length, Array.Empty<byte>(), 0);
        }

        private void ShowConfirmBox(string title, string message, Action onConfirmed)
        {
            _pendingConfirmAction = onConfirmed;
            byte[] titleBytes = U(title);
            byte[] messageBytes = U(message);
            EmojiWindowNative.show_confirm_box_bytes(_window, titleBytes, titleBytes.Length, messageBytes, messageBytes.Length, Array.Empty<byte>(), 0, _confirmBoxCallback);
        }

        private void OnConfirmBoxClosed(int confirmed)
        {
            Action action = _pendingConfirmAction;
            _pendingConfirmAction = null;
            if (confirmed == 1)
            {
                action?.Invoke();
            }
        }
    }
}
