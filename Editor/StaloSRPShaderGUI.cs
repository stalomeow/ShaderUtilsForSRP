// #define SHADER_GUI_LOG

using JetBrains.Annotations;
using Stalo.ShaderUtils.Editor;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AnimatedValues;
using UnityEditor.IMGUI.Controls;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

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

    // Record the last ShaderGUI object for the material
    private static readonly Dictionary<Material, StaloSRPShaderGUI> s_LastShaderGUI = new();

    private List<PropertyGroup> m_PropGroups = null;
    private Dictionary<uint, AnimBool> m_ExpandStates = new();
    private SearchField m_Search = new();
    private string m_SearchText = "";
    private Shader m_LastShader;

    public override void OnGUI(MaterialEditor editor, MaterialProperty[] properties)
    {
        Material[] materials = Array.ConvertAll(editor.targets, obj => (Material)obj);
        Array.ForEach(materials, material => s_LastShaderGUI[material] = this); // Record ShaderGUI

        Shader shader = materials[0].shader;
        m_LastShader = shader; // Record Shader
        UpdatePropertyGroups(ref m_PropGroups, shader, properties);

        editor.SetDefaultGUIWidths();

        using (new GUILayout.VerticalScope())
        {
            // Shader Header
            GUILayout.Space(5);
            EditorGUILayout.LabelField(shader.name, s_ShaderHeaderLabelStyle.Value);

            // Search Field
            GUILayout.Space(5);
            m_SearchText = m_Search.OnGUI(m_SearchText);
            GUILayout.Space(5);

            // Property Groups
            // 对齐 Slider 右侧输入框和其他输入框的宽度
            using (EditorGUIScopes.LabelWidth(EditorGUIUtility.labelWidth - 5f))
            {
                foreach (PropertyGroup group in m_PropGroups)
                {
                    group.OnGUI(editor, m_ExpandStates, m_SearchText);
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

    public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
    {
        base.AssignNewShaderToMaterial(material, oldShader, newShader);

        m_PropGroups = null;
        m_ExpandStates.Clear();
        m_SearchText = string.Empty;
    }

    public override void OnClosed(Material material)
    {
        if (s_LastShaderGUI.TryGetValue(material, out var shaderGUI) && shaderGUI == this)
        {
            s_LastShaderGUI.Remove(material);
        }

        base.OnClosed(material);
    }

    public override void ValidateMaterial(Material material)
    {
        base.ValidateMaterial(material);

        // !!! When user undoes an action, Unity creates a new instance to call this method.
        // !!! That instance is not the one actually drawing the inspector.
        // !!! We should rebuild all groups to sync external changes.

        if (s_LastShaderGUI.TryGetValue(material, out var shaderGUI) && shaderGUI != this)
        {
            shaderGUI.m_PropGroups = null;

            if (shaderGUI.m_LastShader != material.shader)
            {
                // The user possibly undoes a shader-assignment.

                shaderGUI.m_ExpandStates.Clear();
                shaderGUI.m_SearchText = string.Empty;
            }
        }
    }

    private class PropertyGroup
    {
        private readonly GUIContent m_Title;
        private readonly string m_Helps;
        private readonly uint m_BitExpanded;
        private readonly List<MaterialProperty> m_Properties;

        public PropertyGroup() : this(null, null, 0) { }

        public PropertyGroup(string title, string helps, uint bitExpanded)
        {
            m_Title = new GUIContent(title, helps ?? string.Empty);
            m_Helps = helps;
            m_BitExpanded = bitExpanded;
            m_Properties = new List<MaterialProperty>();
        }

        public bool IsDefaultGroup => string.IsNullOrWhiteSpace(m_Title.text) || (m_BitExpanded == 0);

        public int PropertyCount => m_Properties.Count;

        public bool TryAddProperty(MaterialProperty property)
        {
            if ((property.flags & MaterialProperty.PropFlags.HideInInspector) != 0)
            {
                // 属性被隐藏
                return false;
            }

            m_Properties.Add(property);
            return true;
        }

        public bool OnGUI(
            MaterialEditor editor,
            IDictionary<uint, AnimBool> expandStates,
            string searchText,
            StringComparison searchComparisonType = StringComparison.InvariantCultureIgnoreCase)
        {
            Action propDrawer = CreateFilteredPropertyDrawer(editor, searchText, searchComparisonType);

            if (propDrawer == null)
            {
                // 没有属性需要绘制
                return false;
            }

            if (IsDefaultGroup)
            {
                propDrawer();
                GUILayout.Space(10);
                return true;
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

                    propDrawer();
                    GUILayout.Space(10);
                }

                EditorGUILayout.EndFadeGroup();
            }

            return true;
        }

        private Action CreateFilteredPropertyDrawer(
            MaterialEditor editor,
            string searchText,
            StringComparison searchComparisonType)
        {
            bool enableSearch = !string.IsNullOrWhiteSpace(searchText);
            List<(MaterialProperty prop, float height)> validProps = new();

            foreach (MaterialProperty prop in m_Properties)
            {
                float height = editor.GetPropertyHeight(prop, prop.displayName);

                if (height <= 0)
                {
                    // 属性没有高度
                    // 由于 GetControlRect 返回的 rect 的高度有一个最小值，所以需要提前剔除高度为 0 的属性
                    continue;
                }

                if (enableSearch && !prop.displayName.Contains(searchText, searchComparisonType))
                {
                    // 属性不符合搜索
                    continue;
                }

                validProps.Add((prop, height));
            }

            return validProps.Count == 0 ? null : () =>
            {
                foreach ((MaterialProperty prop, float height) in validProps)
                {
                    Rect rect = EditorGUILayout.GetControlRect(true, height, EditorStyles.layerMaskField);
                    editor.ShaderProperty(rect, prop, prop.displayName);
                }
            };
        }
    }

    private static void UpdatePropertyGroups(
        ref List<PropertyGroup> groups,
        Shader shader,
        MaterialProperty[] properties)
    {
        if (groups != null)
        {
            return;
        }

        groups = new List<PropertyGroup>() { new PropertyGroup() }; // With One Default Group

        for (var i = 0; i < properties.Length; i++)
        {
            if (TryGetHeaderFoldoutAttribute(shader, i, out string headerTitle, out string headerHelps))
            {
                // 创建新的组
                uint bitExpanded = 1u << (groups.Count - 1);
                groups.Add(new PropertyGroup(headerTitle, headerHelps, bitExpanded));
            }

            groups[^1].TryAddProperty(properties[i]);
        }

        // 必须在最后移除空的组，保证前面 bitExpanded 的正确性
        groups.RemoveAll(g => g.PropertyCount == 0);
        LogToConsole("Rebuild Groups");
    }

    public static readonly string HeaderFoldoutAttrName = "HeaderFoldout";

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

    [Conditional("SHADER_GUI_LOG")]
    private static void LogToConsole(params object[] messages)
    {
        Debug.LogWarning(string.Join('\n', messages));
    }
}
