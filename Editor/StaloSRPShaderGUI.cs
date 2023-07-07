using JetBrains.Annotations;
using Stalo.ShaderUtils.Editor;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AnimatedValues;
using UnityEditor.IMGUI.Controls;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

// To make the full class name shorter, DO NOT add a namespace here.
// Make it internal so it won't pollute the global namespace.

[PublicAPI]
internal class StaloSRPShaderGUI : ShaderGUI
{
    private static readonly Lazy<GUIStyle> s_ShaderHeaderLabelStyle = new(() =>
    {
        return new GUIStyle(EditorStyles.largeLabel)
        {
            fontStyle = FontStyle.BoldAndItalic,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
        };
    });

    private Dictionary<uint, AnimBool> m_ExpandStates = new();
    private SearchField m_Search = new();
    private string m_SearchText = "";

    public override void OnGUI(MaterialEditor editor, MaterialProperty[] properties)
    {
        editor.SetDefaultGUIWidths();

        using (new GUILayout.VerticalScope())
        {
            // Shader Header
            GUILayout.Space(5);
            Shader shader = ((Material)editor.target).shader;
            EditorGUILayout.LabelField(shader.name, s_ShaderHeaderLabelStyle.Value);

            // Search Field
            GUILayout.Space(5);
            m_SearchText = m_Search.OnGUI(m_SearchText);
            GUILayout.Space(5);

            // Property Groups
            // 对齐 Slider 右侧输入框和其他输入框的宽度
            using (EditorGUIScopes.LabelWidth(EditorGUIUtility.labelWidth - 5f))
            {
                foreach (PropertyGroup group in GroupAndFilterProperties(editor, properties, m_SearchText))
                {
                    group.OnGUI(editor, m_ExpandStates);
                }
            }

            // Extra Properties
            CoreEditorUtils.DrawSplitter();
            GUILayout.Space(5);
        }

        if (SupportedRenderingFeatures.active.editableMaterialRenderQueue)
        {
            editor.RenderQueueField();
        }

        editor.EnableInstancingField();
        editor.DoubleSidedGIField();
    }

    private struct PropertyGroup
    {
        private readonly GUIContent m_Title;
        private readonly string m_Helps;
        private readonly uint m_BitExpanded;
        private readonly List<MaterialProperty> m_Properties;
        private readonly List<float> m_PropertyHeights;

        public PropertyGroup(string title, string helps, uint bitExpanded)
        {
            m_Title = new GUIContent(title, helps ?? string.Empty);
            m_Helps = helps;
            m_BitExpanded = bitExpanded;
            m_Properties = new List<MaterialProperty>();
            m_PropertyHeights = new List<float>();
        }

        public bool IsDefaultGroup => string.IsNullOrWhiteSpace(m_Title.text) || (m_BitExpanded == 0);

        public int PropertyCount => m_Properties.Count;

        public void AddProperty(MaterialProperty property, float height)
        {
            m_Properties.Add(property);
            m_PropertyHeights.Add(height);
        }

        public void OnGUI(MaterialEditor editor, IDictionary<uint, AnimBool> expandStates)
        {
            if (IsDefaultGroup)
            {
                DrawProperties(editor);
                GUILayout.Space(10);
                return;
            }

            using (MaterialHeaderScope headerScope = new(m_Title, m_BitExpanded, editor, spaceAtEnd: false))
            {
                if (expandStates.TryGetValue(m_BitExpanded, out AnimBool isExpanded))
                {
                    isExpanded.target = headerScope.expanded;
                }
                else
                {
                    isExpanded = new AnimBool(headerScope.expanded);
                    expandStates.Add(m_BitExpanded, isExpanded);
                }

                isExpanded.valueChanged.RemoveAllListeners();
                isExpanded.valueChanged.AddListener(editor.Repaint); // 变化后需要重新绘制，不然动画很卡

                if (EditorGUILayout.BeginFadeGroup(isExpanded.faded))
                {
                    GUILayout.Space(5);

                    if (!string.IsNullOrEmpty(m_Helps))
                    {
                        EditorGUILayout.HelpBox(m_Helps, MessageType.None);
                        GUILayout.Space(5);
                    }

                    DrawProperties(editor);
                    GUILayout.Space(10);
                }

                EditorGUILayout.EndFadeGroup();
            }
        }

        private void DrawProperties(MaterialEditor editor)
        {
            for (int i = 0; i < m_Properties.Count; i++)
            {
                MaterialProperty prop = m_Properties[i];
                float height = m_PropertyHeights[i];

                Rect rect = EditorGUILayout.GetControlRect(true, height, EditorStyles.layerMaskField);
                editor.ShaderProperty(rect, prop, prop.displayName);
            }
        }

        public static PropertyGroup CreateDefault() => new(null, null, 0);
    }

    public static readonly string HeaderFoldoutAttrName = "HeaderFoldout";

    private static List<PropertyGroup> GroupAndFilterProperties(
        MaterialEditor editor,
        MaterialProperty[] properties,
        string searchText,
        StringComparison searchComparisonType = StringComparison.InvariantCultureIgnoreCase)
    {
        List<PropertyGroup> groups = new();
        PropertyGroup currentGroup = PropertyGroup.CreateDefault();

        Shader shader = ((Material)editor.target).shader;
        bool enableSearch = !string.IsNullOrWhiteSpace(searchText);

        for (var i = 0; i < properties.Length; i++)
        {
            // 创建新的组
            if (TryGetHeaderFoldoutAttribute(shader, i, out string headerTitle, out string headerHelps))
            {
                groups.Add(currentGroup);

                uint bitExpanded = 1u << (groups.Count - 1);
                currentGroup = new PropertyGroup(headerTitle, headerHelps, bitExpanded);
            }

            MaterialProperty prop = properties[i];

            // 被隐藏的属性
            if ((prop.flags & MaterialProperty.PropFlags.HideInInspector) != 0)
            {
                continue;
            }

            // 不符合搜索的属性
            if (enableSearch && !prop.displayName.Contains(searchText, searchComparisonType))
            {
                continue;
            }

            float height = editor.GetPropertyHeight(prop, prop.displayName);

            // 没有高度的属性
            // 由于 GetControlRect 返回的 rect 的高度有一个最小值，所以需要提前剔除高度为 0 的属性
            if (height <= 0)
            {
                continue;
            }

            currentGroup.AddProperty(prop, height);
        }

        groups.Add(currentGroup);

        // 必须在最后移除空的组，保证前面 bitExpanded 的正确性
        groups.RemoveAll(g => g.PropertyCount == 0);
        return groups;
    }

    private static bool TryGetHeaderFoldoutAttribute(
        Shader shader,
        int propertyIndex,
        out string title,
        out string helps)
    {
        string[] attributes = shader.GetPropertyAttributes(propertyIndex);

        foreach (string attr in attributes)
        {
            Match match = Regex.Match(attr, @$"^{HeaderFoldoutAttrName}\((.+)\)$");

            if (match.Success)
            {
                string[] args = match.Groups[1].Value.Split(',');

                if (args.Length is not (1 or 2))
                {
                    continue;
                }

                title = args[0].Trim();
                helps = (args.Length is 2) ? args[1].Trim() : null;
                return true;
            }
        }

        title = null;
        helps = null;
        return false;
    }
}
