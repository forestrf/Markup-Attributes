using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MarkupAttributes.Editor
{
    internal class InspectorLayoutController
    {
        public bool IncludeChildren(int index) => layoutData[index] == null || layoutData[index].includeChildren;
        public bool Hide(int index) => layoutData[index] == null || layoutData[index].hide;
        public bool TopLevel(int index) => layoutData[index] == null || layoutData[index].topLevel;
        public bool ScopeEnabled => groupsStack.Count == 0 || groupsStack.Peek().isEnabled;
        public bool ScopeVisible => groupsStack.Count == 0 || groupsStack.Peek().isVisible;
        
        private readonly string defaultPrefsPrefix;
        private readonly PropertyLayoutData[] layoutData;
        private readonly Stack<InspectorLayoutGroup> groupsStack = new Stack<InspectorLayoutGroup>();
        private List<string> currentPath = new List<string>();
        private string prefsPrefix = null;
        private int localScopeStart = -1;
        private string activeTabName;

        public InspectorLayoutController(string prefsPrefix, PropertyLayoutData[] layoutData)
        {
            defaultPrefsPrefix = prefsPrefix;
            this.layoutData = layoutData;
        }

        public void Begin()
        {
            groupsStack.Clear();
            currentPath.Clear();
            prefsPrefix = defaultPrefsPrefix;
            localScopeStart = -1;
            activeTabName = null;
        }

        public void Finish()
        {
            EndAll();
        }

        public void BeforeProperty(int index)
        {
            if (layoutData[index].end != null)
            {
                EndGroupsUntill(layoutData[index].end.name);
            }

            for (int i = 0; i < layoutData[index].numberOfScopesToClose; i++)
            {
                EndLocalScope();
            }

            if (layoutData[index].groups == null)
                return;

            foreach (var group in layoutData[index].groups)
            {
                SetScope(group.pathArray);
                bool isVisible = ScopeVisible;
                bool isEnabled = ScopeEnabled;

                if (group.data.Type == LayoutGroupType.LocalScope)
                {
                    group.cachedLocalScopeStart = localScopeStart;
                    localScopeStart = groupsStack.Count;
                }

                if (ScopeVisible)
                {
                    BeginGroup(group, ref isVisible, ref isEnabled);
                }

                group.isVisible = isVisible;
                group.isEnabled = isEnabled;
                groupsStack.Push(group);
            }
        }

        private void BeginGroup(InspectorLayoutGroup group, ref bool isVisible, ref bool isEnabled)
        {
            group.labelState = MarkupGUI.CurrentLabelState();
            if (group.data.Type == LayoutGroupType.LocalScope)
            {
                if (group.localScope.indent)
                    EditorGUI.indentLevel += 1;
                isVisible &= !group.localScope.showControl || group.localScope.IsExpanded;
                group.cachedPrefsPrefix = prefsPrefix;
                prefsPrefix = group.localScope.prefsPrefixOverride;
            }

            if (group.data.Type == LayoutGroupType.DisableIf)
            {
                isEnabled &= !group.data.conditionWrapper.GetValue();
            }

            if (group.data.Type == LayoutGroupType.HideIf)
            {
                isVisible &= !group.data.conditionWrapper.GetValue();
            }

            if (group.data.Type == LayoutGroupType.Tab)
            {
                isVisible &= (activeTabName == null || activeTabName == group.name);
            }

            if (group.data.Type == LayoutGroupType.TabScope)
            {
                bool boxed = group.data.BodyStyle == MarkupBodyStyle.Box;
                string prefsName = GetPrefsName();
                int activeTab;
                activeTab = MarkupGUI.BeginTabsGroup(MarkupAttributesPrefs.GetInt(prefsName), group.data.Tabs, boxed);
                MarkupAttributesPrefs.SetInt(prefsName, activeTab);
                group.cachedActiveTab = activeTabName;
                activeTabName = group.data.Tabs[activeTab];
            }

            if (group.data.Type == LayoutGroupType.Horizontal)
            {
                EditorGUIUtility.labelWidth = group.data.LabelWidth;
                EditorGUILayout.BeginHorizontal(GUIStyle.none);
            }

            if (group.data.Type == LayoutGroupType.Vertical)
            {
                string prefsName = GetPrefsName();
                bool isExpanded = MarkupAttributesPrefs.GetBool(prefsName);
                MarkupGUI.BeginGenericVerticalGroup(ref isExpanded, ref isEnabled,
                    group.data.HeaderStyle, group.data.BodyStyle, group.name,
                    group.data.togglableValueWrapper);

                MarkupAttributesPrefs.SetBool(prefsName, isExpanded);
                isVisible &= isExpanded;
            }
        }
        
        private string GetPrefsName()
        {
            if (prefsPrefix != null)
            {
                return prefsPrefix + "/" + string.Join("/", currentPath.ToArray(),
                    localScopeStart + 1, currentPath.Count - localScopeStart - 1);
            }
            else
            {
                return defaultPrefsPrefix + "/" + string.Join("/", currentPath);
            }
        }

        private void SetScope(string[] path)
        {
            if (path == null || path.Length < 1)
            {
                EndAll();
                currentPath.Add("");
                return;
            }

            if (path.Length > 1 && (path[0] == "." || path[0] == ".."))
            {
                if (currentPath.Count > 0 && path[0] == "..")
                {
                    EndGroup();
                }
                currentPath.Add(path.Last());
                return;
            }

            var newPath = new List<string>();
            int i = 0;
            int j = 0;
            while (j < path.Length && i < currentPath.Count)
            {
                if (i <= localScopeStart)
                {
                    newPath.Add(currentPath[i]);
                }
                else
                {
                    if (path[j] != currentPath[i])
                    {
                        break;
                    }
                    newPath.Add(path[j]);
                    j++;
                }
                i++;
            }

            if (j < path.Length)
                newPath.Add(path.Last());

            int groupsToRemove = currentPath.Count - i;
            for (int k = 0; k < groupsToRemove; k++)
            {
                EndGroup();
            }

            currentPath = newPath;
        }

        private void EndGroupsUntill(string name)
        {
            int limit = name != null ? -1 :
                Mathf.Max(-1, currentPath.Count - 2);
            limit = Mathf.Max(limit, localScopeStart);
            int index = currentPath.Count - 1;
            while (index > limit)
            {
                if (currentPath[index] != name)
                {
                    EndGroup();
                    index -= 1;
                }
                else
                {
                    EndGroup();
                    break;
                }     
            }
        }

        private void EndLocalScope()
        {
            int count = localScopeStart;
            while (groupsStack.Count > count)
            {
                EndGroup();
            }
        }

        private void EndAll()
        {
            while (groupsStack.Count > 0)
            {
                EndGroup();
            }
        }

        private void EndGroup()
        {
            if (groupsStack.Count > 0)
            {
                InspectorLayoutGroup group = groupsStack.Pop();
                if (currentPath.Count > 0)
                    currentPath.RemoveAt(currentPath.Count - 1);

                if (group.cachedLocalScopeStart.HasValue)
                    localScopeStart = group.cachedLocalScopeStart.Value;
                if (ScopeVisible)
                {
                    if (group.data.Type == LayoutGroupType.Vertical)
                        EditorGUILayout.EndVertical();

                    if (group.data.Type == LayoutGroupType.Horizontal)
                        EditorGUILayout.EndHorizontal();

                    if (group.data.Type == LayoutGroupType.TabScope)
                        EditorGUILayout.EndVertical();

                    if (group.cachedPrefsPrefix != null)
                        prefsPrefix = group.cachedPrefsPrefix;

                    if (group.cachedActiveTab != null)
                        activeTabName = group.cachedActiveTab;

                    group.labelState.Restore();

                    group.cachedPrefsPrefix = null;
                    group.cachedActiveTab = null;
                }
            }
        }
    }
}

