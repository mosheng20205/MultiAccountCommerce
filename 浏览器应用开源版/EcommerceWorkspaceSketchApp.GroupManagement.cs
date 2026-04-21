using System;
using System.Collections.Generic;
using EmojiWindowDemo;

namespace EmojiWindowEcommerceWorkspaceSketchDemo
{
    internal sealed partial class EcommerceWorkspaceSketchApp
    {
        private void InitializeGroupManagementUi()
        {
            _groupManagementPanel = EmojiWindowNative.CreatePanel(_browserCanvas, 0, 0, 100, 100, Argb(255, 249, 251, 253));
            _lblGroupListTitle = Label(_groupManagementPanel, "分组列表", 14, true);
            _lblGroupEditorTitle = Label(_groupManagementPanel, "分组设置", 14, true);
            _lblGroupNameCaption = Label(_groupManagementPanel, "分组名称", 12, true);
            _lblGroupUrlCaption = Label(_groupManagementPanel, "默认打开网址", 12, true);
            _lblGroupStats = Label(_groupManagementPanel, string.Empty, 12, false);
            _lblGroupHint = Label(_groupManagementPanel, "新建环境时会继承当前分组的默认打开网址。", 12, false);
            _editGroupName = EditBox(_groupManagementPanel, string.Empty, false);
            _editGroupUrl = EditBox(_groupManagementPanel, string.Empty, false);
            _groupListBox = EmojiWindowNative.CreateListBox(_groupManagementPanel, 0, 0, 100, 100, 0, Argb(255, 31, 41, 55), Argb(255, 255, 255, 255));
            EmojiWindowNative.SetListBoxCallback(_groupListBox, _groupListCallback);

            _btnGroupSave = Button(_groupManagementPanel, "保存修改", Argb(255, 37, 99, 235), OnSaveGroupConfig);
            _btnGroupAdd = Button(_groupManagementPanel, "新增分组", Argb(255, 34, 197, 94), OnAddGroup);
            _btnGroupDelete = Button(_groupManagementPanel, "删除分组", Argb(255, 239, 68, 68), OnDeleteGroup);

            ApplyGroupManagementVisibility(false);
        }

        private void RenderGroupManagementModule()
        {
            EnsureGroupManagementSelection();
            RefreshGroupManagementListItems();
            BindGroupManagementEditorState(_selectedManagedGroupName);
            SetLabelText(_lblInfoMain, "当前模块：分组管理");
            SetLabelText(_lblInfoSub, "右侧可配置分组名称、默认打开网址，并管理新增、删除分组。");
            SetWindowTitle("电商多账号浏览器 - 分组管理");
        }

        private void HandleGroupListSelection(int index)
        {
            if (index < 0 || index >= _groupOrder.Count)
            {
                return;
            }

            _selectedManagedGroupName = _groupOrder[index];
            BindGroupManagementEditorState(_selectedManagedGroupName);
        }

        private void ApplyGroupManagementVisibility(bool visible)
        {
            int show = visible ? SwShow : SwHide;
            if (_groupManagementPanel != IntPtr.Zero)
            {
                NativeExtras.ShowWindow(_groupManagementPanel, show);
            }

            foreach (IntPtr label in new[] { _lblGroupListTitle, _lblGroupEditorTitle, _lblGroupNameCaption, _lblGroupUrlCaption, _lblGroupStats, _lblGroupHint })
            {
                if (label != IntPtr.Zero)
                {
                    EmojiWindowNative.ShowLabel(label, visible ? 1 : 0);
                }
            }

            foreach (IntPtr edit in new[] { _editGroupName, _editGroupUrl })
            {
                if (edit != IntPtr.Zero)
                {
                    EmojiWindowNative.ShowEditBox(edit, visible ? 1 : 0);
                }
            }

            if (_groupListBox != IntPtr.Zero)
            {
                EmojiWindowNative.ShowListBox(_groupListBox, visible ? 1 : 0);
            }

            foreach (int buttonId in new[] { _btnGroupSave, _btnGroupAdd, _btnGroupDelete })
            {
                if (buttonId != 0)
                {
                    EmojiWindowNative.ShowButton(buttonId, visible ? 1 : 0);
                }
            }
        }

        private void EnsureGroupManagementSelection()
        {
            if (!string.IsNullOrWhiteSpace(_selectedManagedGroupName) && _groupEnvironmentIds.ContainsKey(_selectedManagedGroupName))
            {
                return;
            }

            _selectedManagedGroupName = _groupOrder.Count > 0 ? _groupOrder[0] : string.Empty;
        }

        private void RefreshGroupManagementListItems()
        {
            if (_groupListBox == IntPtr.Zero)
            {
                return;
            }

            for (int i = EmojiWindowNative.GetListItemCount(_groupListBox) - 1; i >= 0; i--)
            {
                EmojiWindowNative.RemoveListItem(_groupListBox, i);
            }

            int selectedIndex = -1;
            for (int i = 0; i < _groupOrder.Count; i++)
            {
                string groupName = _groupOrder[i];
                byte[] textBytes = U(groupName);
                EmojiWindowNative.AddListItem(_groupListBox, textBytes, textBytes.Length);
                if (groupName == _selectedManagedGroupName)
                {
                    selectedIndex = i;
                }
            }

            if (selectedIndex >= 0)
            {
                EmojiWindowNative.SetSelectedIndex(_groupListBox, selectedIndex);
            }
        }

        private void BindGroupManagementEditorState(string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName) || !_groupEnvironmentIds.TryGetValue(groupName, out List<int> envIds))
            {
                SetEditText(_editGroupName, string.Empty);
                SetEditText(_editGroupUrl, string.Empty);
                SetLabelText(_lblGroupStats, "当前没有可编辑的分组。");
                return;
            }

            _selectedManagedGroupName = groupName;
            SetEditText(_editGroupName, groupName);
            SetEditText(_editGroupUrl, GetGroupDefaultUrl(groupName, DefaultStartUrl("new-env.local")));
            SetLabelText(_lblGroupStats, $"当前分组下环境数：{envIds.Count}。修改默认网址只影响后续新建环境。");
        }

        private void LayoutGroupManagementModule()
        {
            if (_groupManagementPanel == IntPtr.Zero || _browserCanvasWidth <= 0 || _browserCanvasHeight <= 0)
            {
                return;
            }

            Move(_groupManagementPanel, 0, 0, _browserCanvasWidth, _browserCanvasHeight);

            int outer = Scale(24);
            int listWidth = Math.Max(Scale(260), Math.Min(Scale(360), _browserCanvasWidth / 3));
            int gap = Scale(28);
            int titleHeight = Scale(28);
            int labelHeight = Scale(24);
            int editHeight = Scale(40);
            int buttonHeight = Scale(40);
            int buttonWidth = Scale(116);
            int editorX = outer + listWidth + gap;
            int editorWidth = Math.Max(Scale(320), _browserCanvasWidth - editorX - outer);
            int rowGap = Scale(12);
            int listTop = outer + titleHeight + Scale(12);
            int listHeight = Math.Max(Scale(260), _browserCanvasHeight - listTop - outer);

            EmojiWindowNative.SetLabelBounds(_lblGroupListTitle, outer, outer, listWidth, titleHeight);
            EmojiWindowNative.SetListBoxBounds(_groupListBox, outer, listTop, listWidth, listHeight);

            EmojiWindowNative.SetLabelBounds(_lblGroupEditorTitle, editorX, outer, editorWidth, titleHeight);
            EmojiWindowNative.SetLabelBounds(_lblGroupNameCaption, editorX, outer + Scale(44), editorWidth, labelHeight);
            EmojiWindowNative.SetEditBoxBounds(_editGroupName, editorX, outer + Scale(72), editorWidth, editHeight);
            EmojiWindowNative.SetLabelBounds(_lblGroupUrlCaption, editorX, outer + Scale(128), editorWidth, labelHeight);
            EmojiWindowNative.SetEditBoxBounds(_editGroupUrl, editorX, outer + Scale(156), editorWidth, editHeight);
            EmojiWindowNative.SetLabelBounds(_lblGroupStats, editorX, outer + Scale(214), editorWidth, labelHeight);
            EmojiWindowNative.SetLabelBounds(_lblGroupHint, editorX, outer + Scale(244), editorWidth, Scale(44));

            int buttonY = outer + Scale(308);
            EmojiWindowNative.SetButtonBounds(_btnGroupSave, editorX, buttonY, buttonWidth, buttonHeight);
            EmojiWindowNative.SetButtonBounds(_btnGroupAdd, editorX + buttonWidth + rowGap, buttonY, buttonWidth, buttonHeight);
            EmojiWindowNative.SetButtonBounds(_btnGroupDelete, editorX + (buttonWidth + rowGap) * 2, buttonY, buttonWidth, buttonHeight);
        }

        private void SaveGroupManagementChanges()
        {
            if (string.IsNullOrWhiteSpace(_selectedManagedGroupName) || !_groupEnvironmentIds.ContainsKey(_selectedManagedGroupName))
            {
                SetLabelText(_lblInfoSub, "请先在分组列表中选中一个分组。");
                return;
            }

            string newName = GetEditText(_editGroupName).Trim();
            string newUrl = NormalizeUrl(GetEditText(_editGroupUrl).Trim());
            if (string.IsNullOrWhiteSpace(newName))
            {
                SetLabelText(_lblInfoSub, "分组名称不能为空。");
                return;
            }

            if (string.IsNullOrWhiteSpace(newUrl))
            {
                SetLabelText(_lblInfoSub, "默认打开网址不能为空。");
                return;
            }

            string oldName = _selectedManagedGroupName;
            string oldDefaultUrl = GetGroupDefaultUrl(oldName, string.Empty);
            if (!string.Equals(oldName, newName, StringComparison.Ordinal) && _groupEnvironmentIds.ContainsKey(newName))
            {
                SetLabelText(_lblInfoSub, $"分组 {newName} 已存在，请更换名称。");
                return;
            }

            if (!string.Equals(oldName, newName, StringComparison.Ordinal))
            {
                int index = _groupOrder.IndexOf(oldName);
                if (index >= 0)
                {
                    _groupOrder[index] = newName;
                }

                List<int> envIds = _groupEnvironmentIds[oldName];
                _groupEnvironmentIds.Remove(oldName);
                _groupEnvironmentIds[newName] = envIds;

                string existingUrl = GetGroupDefaultUrl(oldName, newUrl);
                _groupDefaultUrls.Remove(oldName);
                _groupDefaultUrls[newName] = existingUrl;

                foreach (int envId in envIds)
                {
                    if (_environments.TryGetValue(envId, out EnvironmentRecord env))
                    {
                        env.GroupName = newName;
                    }
                }
            }

            _groupDefaultUrls[newName] = newUrl;
            ApplyGroupDefaultUrlToEnvironments(newName, oldDefaultUrl, newUrl);
            _selectedManagedGroupName = newName;
            SaveGroupConfiguration();
            RebuildGroupNavigationTree();
            ActivateGroupManagementModule();
            SetLabelText(_lblInfoSub, $"分组 {newName} 已保存，默认网址已更新为 {newUrl}。");
        }

        private void AddManagedGroup()
        {
            string groupName = GetEditText(_editGroupName).Trim();
            string defaultUrl = NormalizeUrl(GetEditText(_editGroupUrl).Trim());
            if (string.IsNullOrWhiteSpace(groupName))
            {
                SetLabelText(_lblInfoSub, "请输入分组名称后再新增。");
                return;
            }

            if (_groupEnvironmentIds.ContainsKey(groupName))
            {
                SetLabelText(_lblInfoSub, $"分组 {groupName} 已存在。");
                return;
            }

            if (string.IsNullOrWhiteSpace(defaultUrl))
            {
                defaultUrl = DefaultStartUrl("new-env.local");
            }

            _groupOrder.Add(groupName);
            _groupEnvironmentIds[groupName] = new List<int>();
            _groupDefaultUrls[groupName] = defaultUrl;
            _selectedManagedGroupName = groupName;

            SaveGroupConfiguration();
            RebuildGroupNavigationTree();
            ActivateGroupManagementModule();
            SetLabelText(_lblInfoSub, $"已新增分组 {groupName}，默认网址为 {defaultUrl}。");
        }

        private void DeleteManagedGroup()
        {
            if (string.IsNullOrWhiteSpace(_selectedManagedGroupName) || !_groupEnvironmentIds.TryGetValue(_selectedManagedGroupName, out List<int> envIds))
            {
                SetLabelText(_lblInfoSub, "请先选中一个分组再删除。");
                return;
            }

            if (envIds.Count > 0)
            {
                SetLabelText(_lblInfoSub, $"分组 {_selectedManagedGroupName} 下还有 {envIds.Count} 个环境，请先清空环境再删除。");
                return;
            }

            if (_groupOrder.Count <= 1)
            {
                SetLabelText(_lblInfoSub, "至少保留一个分组，不能删除最后一个分组。");
                return;
            }

            string deletingName = _selectedManagedGroupName;
            _groupOrder.Remove(deletingName);
            _groupEnvironmentIds.Remove(deletingName);
            _groupDefaultUrls.Remove(deletingName);
            _selectedManagedGroupName = _groupOrder.Count > 0 ? _groupOrder[0] : string.Empty;

            SaveGroupConfiguration();
            RebuildGroupNavigationTree();
            ActivateGroupManagementModule();
            SetLabelText(_lblInfoSub, $"已删除分组 {deletingName}。");
        }

        private void RebuildGroupNavigationTree()
        {
            NativeExtras.ClearTree(_tree);
            _nodeMeta.Clear();
            _nodeToEnvId.Clear();
            _groupNodes.Clear();
            _moduleNodes.Clear();

            int switchRoot = AddRootNode("环境切换", "🧭", "switch_root", "环境切换");
            foreach (string groupName in _groupOrder)
            {
                int groupNodeId = AddChildNode(switchRoot, groupName, "📁", "group", groupName);
                _groupNodes[groupName] = groupNodeId;
                if (!_groupEnvironmentIds.TryGetValue(groupName, out List<int> envIds))
                {
                    envIds = new List<int>();
                    _groupEnvironmentIds[groupName] = envIds;
                }

                for (int i = 0; i < envIds.Count; i++)
                {
                    int envId = envIds[i];
                    if (!_environments.TryGetValue(envId, out EnvironmentRecord env))
                    {
                        continue;
                    }

                    int nodeId = AddChildNode(groupNodeId, env.Name, "●", "environment", env.Name);
                    env.NodeId = nodeId;
                    env.GroupName = groupName;
                    _nodeToEnvId[nodeId] = envId;
                    _nodeMeta[nodeId].EnvId = envId;
                    ApplyEnvironmentNodeColor(nodeId, env.Status);
                }
            }

            foreach (KeyValuePair<string, string> module in _moduleIcons)
            {
                int moduleNodeId = AddRootNode(module.Key, module.Value, "module", module.Key);
                _moduleNodes[module.Key] = moduleNodeId;
            }

            EmojiWindowNative.ExpandAll(_tree);
            foreach (KeyValuePair<int, NodeMeta> item in _nodeMeta)
            {
                if (item.Value.Kind == "switch_root" || item.Value.Kind == "group" || item.Value.Kind == "module")
                {
                    NativeExtras.SetNodeForeColor(_tree, item.Key, EmojiWindowNative.IsDarkMode() != 0 ? Argb(255, 241, 245, 249) : Argb(255, 31, 41, 55));
                }
            }
        }

        private void ActivateGroupManagementModule()
        {
            if (_moduleNodes.TryGetValue("分组管理", out int moduleNodeId))
            {
                EmojiWindowNative.SetSelectedNode(_tree, moduleNodeId);
                ActivateNode(moduleNodeId);
            }
        }

        private void ApplyGroupDefaultUrlToEnvironments(string groupName, string oldDefaultUrl, string newDefaultUrl)
        {
            if (string.IsNullOrWhiteSpace(groupName) || !_groupEnvironmentIds.TryGetValue(groupName, out List<int> envIds))
            {
                return;
            }

            string normalizedNew = NormalizeUrl(newDefaultUrl);
            foreach (int envId in envIds)
            {
                if (!_environments.TryGetValue(envId, out EnvironmentRecord env))
                {
                    continue;
                }

                env.StartUrl = normalizedNew;
                env.LastUrl = normalizedNew;
                env.Domain = ExtractDomain(normalizedNew, env.Domain);

                if (env.AddressEdit != IntPtr.Zero && _currentEnvId == env.EnvId)
                {
                    SetEditText(env.AddressEdit, EnvironmentUrlText(env));
                }

                if (_currentEnvId == env.EnvId)
                {
                    RenderEnvironment(env);
                }
            }
        }

    }
}
