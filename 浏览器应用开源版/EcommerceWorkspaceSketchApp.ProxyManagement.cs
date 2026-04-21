using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
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
        }

        [DataContract]
        private sealed class PersistedProxyConfig
        {
            [DataMember(Order = 1)]
            public List<ProxyConfig> Proxies { get; set; } = new List<ProxyConfig>();
        }

        private static readonly string[] ProxyTypeItems = { "HTTP", "SOCKS5" };
        private const int ProxyTypeRadioStyleButton = 2;

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
                ProxyConfig defaultConfig = new ProxyConfig { Name = "默认代理", Type = "HTTP" };
                _proxyConfigs[defaultConfig.Name] = defaultConfig;
                _proxyOrder.Add(defaultConfig.Name);
            }

            SaveProxyConfiguration();
        }

        private void InitializeProxyManagementUi()
        {
            _proxyManagementPanel = EmojiWindowNative.CreatePanel(_browserCanvas, 0, 0, 100, 100, Argb(255, 249, 251, 253));
            _lblProxyListTitle = Label(_proxyManagementPanel, "代理列表", 14, true);
            _lblProxyEditorTitle = Label(_proxyManagementPanel, "代理设置", 14, true);
            _lblProxyNameCaption = Label(_proxyManagementPanel, "代理名称", 12, true);
            _lblProxyTypeCaption = Label(_proxyManagementPanel, "代理类型", 12, true);
            _lblProxyHostCaption = Label(_proxyManagementPanel, "主机地址", 12, true);
            _lblProxyPortCaption = Label(_proxyManagementPanel, "端口", 12, true);
            _lblProxyUserCaption = Label(_proxyManagementPanel, "用户名", 12, true);
            _lblProxyPasswordCaption = Label(_proxyManagementPanel, "密码", 12, true);
            _lblProxyHint = Label(_proxyManagementPanel, "开源版支持：HTTP、HTTP(带账号密码)、SOCKS5(无认证)。不支持带账号密码的 SOCKS5。", 12, false);

            _editProxyName = EditBox(_proxyManagementPanel, string.Empty, false);
            _editProxyHost = EditBox(_proxyManagementPanel, string.Empty, false);
            _editProxyPort = EditBox(_proxyManagementPanel, string.Empty, false);
            _editProxyUser = EditBox(_proxyManagementPanel, string.Empty, false);
            _editProxyPassword = EditBox(_proxyManagementPanel, string.Empty, false);

            _proxyListBox = EmojiWindowNative.CreateListBox(_proxyManagementPanel, 0, 0, 100, 100, 0, Argb(255, 31, 41, 55), Argb(255, 255, 255, 255));
            EmojiWindowNative.SetListBoxCallback(_proxyListBox, _proxyListCallback);

            // 创建单选框替代组合框，使用按钮样式
            // 注意：按钮样式需要颜色反转 - 前景色用于文字，背景色用于按钮背景
            byte[] httpText = U("HTTP");
            byte[] socks5Text = U("SOCKS5");
            uint btnFg = Argb(255, 255, 255, 255);  // 白色文字
            uint btnBg = Argb(255, 64, 158, 255);   // 蓝色背景
            _radioHttp = EmojiWindowNative.CreateRadioButton(_proxyManagementPanel, 0, 0, Scale(104), Scale(32), httpText, httpText.Length, 1, 1, ProxyTypeSelectedTextColor(), ProxyTypeSelectedBackColor(), _fontSegoe, _fontSegoe.Length, 12, 1, 0, 0);
            _radioSocks5 = EmojiWindowNative.CreateRadioButton(_proxyManagementPanel, 0, 0, Scale(124), Scale(32), socks5Text, socks5Text.Length, 1, 0, ProxyTypeNormalTextColor(), ProxyTypeNormalBackColor(), _fontSegoe, _fontSegoe.Length, 12, 1, 0, 0);

            // 设置为按钮样式 (RADIO_STYLE_BUTTON = 2)
            EmojiWindowNative.SetRadioButtonStyle(_radioHttp, ProxyTypeRadioStyleButton);
            EmojiWindowNative.SetRadioButtonStyle(_radioSocks5, ProxyTypeRadioStyleButton);
            EmojiWindowNative.SetRadioButtonCallback(_radioHttp, _proxyRadioCallback);
            EmojiWindowNative.SetRadioButtonCallback(_radioSocks5, _proxyRadioCallback);
            RefreshProxyTypeRadioStyle();

            Console.WriteLine($"Created radio buttons: HTTP={_radioHttp}, SOCKS5={_radioSocks5}");

            _btnProxyTypeHttp = Button(_proxyManagementPanel, "HTTP", ProxyTypeSelectedBackColor(), () => SelectProxyType("HTTP"));
            _btnProxyTypeSocks5 = Button(_proxyManagementPanel, "SOCKS5", ProxyTypeNormalBackColor(), () => SelectProxyType("SOCKS5"));
            RefreshProxyTypeRadioStyle();

            _btnProxySave = Button(_proxyManagementPanel, "保存修改", Argb(255, 37, 99, 235), OnSaveProxyConfig);
            _btnProxyAdd = Button(_proxyManagementPanel, "新增代理", Argb(255, 34, 197, 94), OnAddProxy);
            _btnProxyDelete = Button(_proxyManagementPanel, "删除代理", Argb(255, 239, 68, 68), OnDeleteProxy);

            SetProxyManagementVisible(false);
        }

        private void RenderProxyManagement()
        {
            EnsureProxySelection();
            RefreshProxyListItems();
            BindProxyEditorState(_selectedManagedProxyName);
            SetLabelText(_lblInfoMain, "当前模块：代理管理");
            SetLabelText(_lblInfoSub, "右侧可维护代理配置。开源版支持 HTTP、HTTP 认证、无认证 SOCKS5。");
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
                _lblProxyListTitle, _lblProxyEditorTitle, _lblProxyNameCaption, _lblProxyTypeCaption,
                _lblProxyHostCaption, _lblProxyPortCaption, _lblProxyUserCaption, _lblProxyPasswordCaption, _lblProxyHint
            })
            {
                if (label != IntPtr.Zero)
                {
                    EmojiWindowNative.ShowLabel(label, visible ? 1 : 0);
                }
            }

            foreach (IntPtr edit in new[] { _editProxyName, _editProxyHost, _editProxyPort, _editProxyUser, _editProxyPassword })
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

            foreach (int buttonId in new[] { _btnProxyTypeHttp, _btnProxyTypeSocks5, _btnProxySave, _btnProxyAdd, _btnProxyDelete })
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

            for (int i = EmojiWindowNative.GetListItemCount(_proxyListBox) - 1; i >= 0; i--)
            {
                EmojiWindowNative.RemoveListItem(_proxyListBox, i);
            }

            int selectedIndex = -1;
            for (int i = 0; i < _proxyOrder.Count; i++)
            {
                string proxyName = _proxyOrder[i];
                byte[] textBytes = U(proxyName);
                EmojiWindowNative.AddListItem(_proxyListBox, textBytes, textBytes.Length);
                if (proxyName == _selectedManagedProxyName)
                {
                    selectedIndex = i;
                }
            }

            if (selectedIndex >= 0)
            {
                EmojiWindowNative.SetSelectedIndex(_proxyListBox, selectedIndex);
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
                SetLabelText(_lblProxyHint, "当前没有可编辑的代理。");
                return;
            }

            _selectedManagedProxyName = proxyName;
            SetEditText(_editProxyName, config.Name);
            SetEditText(_editProxyHost, config.Host);
            SetEditText(_editProxyPort, config.Port > 0 ? config.Port.ToString() : string.Empty);
            SetEditText(_editProxyUser, config.User);
            SetEditText(_editProxyPassword, config.Password);
            bool isSocks5 = config.Type == "SOCKS5";
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
            int listWidth = Math.Max(Scale(260), Math.Min(Scale(360), _browserCanvasWidth / 3));
            int gap = Scale(28);
            int titleHeight = Scale(28);
            int labelHeight = Scale(24);
            int editHeight = Scale(38);
            int smallWidth = Scale(140);
            int editorX = outer + listWidth + gap;
            int editorWidth = Math.Max(Scale(360), _browserCanvasWidth - editorX - outer);
            int listTop = outer + titleHeight + Scale(12);
            int listHeight = Math.Max(Scale(260), _browserCanvasHeight - listTop - outer);
            int rowGap = Scale(12);
            int buttonHeight = Scale(40);
            int buttonWidth = Scale(116);

            EmojiWindowNative.SetLabelBounds(_lblProxyListTitle, outer, outer, listWidth, titleHeight);
            EmojiWindowNative.SetListBoxBounds(_proxyListBox, outer, listTop, listWidth, listHeight);

            EmojiWindowNative.SetLabelBounds(_lblProxyEditorTitle, editorX, outer, editorWidth, titleHeight);
            EmojiWindowNative.SetLabelBounds(_lblProxyNameCaption, editorX, outer + Scale(44), editorWidth, labelHeight);
            EmojiWindowNative.SetEditBoxBounds(_editProxyName, editorX, outer + Scale(72), editorWidth, editHeight);

            EmojiWindowNative.SetLabelBounds(_lblProxyTypeCaption, editorX, outer + Scale(126), smallWidth, labelHeight);
            // 单选框水平排列
            int radioY = outer + Scale(154);
            int radioHttpWidth = Scale(100);
            int radioSocks5Width = Scale(120);
            int radioGap = Scale(10);
            EmojiWindowNative.SetRadioButtonBounds(_radioHttp, -1000, -1000, radioHttpWidth, editHeight);
            EmojiWindowNative.SetRadioButtonBounds(_radioSocks5, -1000, -1000, radioSocks5Width, editHeight);
            EmojiWindowNative.SetButtonBounds(_btnProxyTypeHttp, editorX, radioY, radioHttpWidth, editHeight);
            EmojiWindowNative.SetButtonBounds(_btnProxyTypeSocks5, editorX + radioHttpWidth + radioGap, radioY, radioSocks5Width, editHeight);

            // 主机地址标签和编辑框右移，避免与SOCKS5单选框重叠
            int radioTotalWidth = radioHttpWidth + radioGap + radioSocks5Width;
            int hostX = editorX + radioTotalWidth + rowGap;
            int hostWidth = editorWidth - radioTotalWidth - rowGap;
            EmojiWindowNative.SetLabelBounds(_lblProxyHostCaption, hostX, outer + Scale(126), hostWidth, labelHeight);
            EmojiWindowNative.SetEditBoxBounds(_editProxyHost, hostX, outer + Scale(154), hostWidth, editHeight);

            EmojiWindowNative.SetLabelBounds(_lblProxyPortCaption, editorX, outer + Scale(208), smallWidth, labelHeight);
            EmojiWindowNative.SetEditBoxBounds(_editProxyPort, editorX, outer + Scale(236), smallWidth, editHeight);

            EmojiWindowNative.SetLabelBounds(_lblProxyUserCaption, editorX + smallWidth + rowGap, outer + Scale(208), (editorWidth - smallWidth - rowGap) / 2 - rowGap / 2, labelHeight);
            EmojiWindowNative.SetEditBoxBounds(_editProxyUser, editorX + smallWidth + rowGap, outer + Scale(236), (editorWidth - smallWidth - rowGap) / 2 - rowGap / 2, editHeight);

            int passwordX = editorX + smallWidth + rowGap + ((editorWidth - smallWidth - rowGap) / 2);
            int passwordWidth = editorWidth - (passwordX - editorX);
            EmojiWindowNative.SetLabelBounds(_lblProxyPasswordCaption, passwordX, outer + Scale(208), passwordWidth, labelHeight);
            EmojiWindowNative.SetEditBoxBounds(_editProxyPassword, passwordX, outer + Scale(236), passwordWidth, editHeight);

            EmojiWindowNative.SetLabelBounds(_lblProxyHint, editorX, outer + Scale(294), editorWidth, Scale(54));

            int buttonY = outer + Scale(364);
            EmojiWindowNative.SetButtonBounds(_btnProxySave, editorX, buttonY, buttonWidth, buttonHeight);
            EmojiWindowNative.SetButtonBounds(_btnProxyAdd, editorX + buttonWidth + rowGap, buttonY, buttonWidth, buttonHeight);
            EmojiWindowNative.SetButtonBounds(_btnProxyDelete, editorX + (buttonWidth + rowGap) * 2, buttonY, buttonWidth, buttonHeight);
        }

        private void OnProxyListSelected(IntPtr hListBox, int index)
        {
            if (index < 0 || index >= _proxyOrder.Count)
            {
                return;
            }

            _selectedManagedProxyName = _proxyOrder[index];
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
            string type = SelectedProxyType();
            if (type == "SOCKS5")
            {
                SetLabelText(_lblProxyHint, "SOCKS5 仅支持无认证代理。请留空用户名和密码。");
                return;
            }

            SetLabelText(_lblProxyHint, "HTTP 代理支持无认证或带账号密码认证。");
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
            }

            _proxyConfigs[updated.Name] = updated;
            _selectedManagedProxyName = updated.Name;
            SaveProxyConfiguration();
            BindProxyEditorState(updated.Name);
            RefreshProxyListItems();
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
            SaveProxyConfiguration();
            RefreshProxyListItems();
            BindProxyEditorState(config.Name);
            SetLabelText(_lblInfoSub, $"已新增代理 {config.Name}。");
        }

        private void DeleteManagedProxy()
        {
            if (string.IsNullOrWhiteSpace(_selectedManagedProxyName) || !_proxyConfigs.ContainsKey(_selectedManagedProxyName))
            {
                SetLabelText(_lblInfoSub, "请先选中一个代理再删除。");
                return;
            }

            int usageCount = 0;
            foreach (EnvironmentRecord env in _environments.Values)
            {
                if (string.Equals(env.Proxy, _selectedManagedProxyName, StringComparison.OrdinalIgnoreCase))
                {
                    usageCount++;
                }
            }

            if (usageCount > 0)
            {
                SetLabelText(_lblInfoSub, $"代理 {_selectedManagedProxyName} 仍被 {usageCount} 个环境使用，不能删除。");
                return;
            }

            string deletingName = _selectedManagedProxyName;
            _proxyConfigs.Remove(deletingName);
            _proxyOrder.Remove(deletingName);
            _selectedManagedProxyName = _proxyOrder.Count > 0 ? _proxyOrder[0] : string.Empty;
            SaveProxyConfiguration();
            RefreshProxyListItems();
            BindProxyEditorState(_selectedManagedProxyName);
            SetLabelText(_lblInfoSub, $"已删除代理 {deletingName}。");
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
                Password = password
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
                Password = config?.Password?.Trim() ?? string.Empty
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
            ProxyConfig config = ResolveProxyConfig(env);
            if (config == null || string.IsNullOrWhiteSpace(config.Host) || config.Port <= 0)
            {
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

            if (updateStatus)
            {
                SetLabelText(_lblInfoSub, $"域名：{env.Domain}   代理：{env.Proxy}   已应用代理：{proxyUrl}");
            }
            return true;
        }
    }
}
