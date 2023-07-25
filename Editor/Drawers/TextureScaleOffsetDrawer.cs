using JetBrains.Annotations;
using UnityEditor;
using UnityEngine;

namespace Stalo.ShaderUtils.Editor.Drawers
{
    [PublicAPI]
    internal class TextureScaleOffsetDrawer : MaterialPropertyDrawer
    {
        private readonly int m_IndentCount;

        public TextureScaleOffsetDrawer() : this(0) { }

        public TextureScaleOffsetDrawer(float indentCount)
        {
            m_IndentCount = (int)indentCount;
        }

        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
        {
            return 2 * EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        }

        public override void OnGUI(Rect position, MaterialProperty prop, GUIContent label, MaterialEditor editor)
        {
            MaterialEditor.BeginProperty(position, prop);

            using (new EditorGUI.IndentLevelScope(m_IndentCount))
            using (new MemberValueScope<bool>(() => EditorGUI.showMixedValue, prop.hasMixedValue))
            using (new MemberValueScope<float>(() => EditorGUIUtility.labelWidth, 0))
            {
                EditorGUI.BeginChangeCheck();

                Vector4 value = MaterialEditor.TextureScaleOffsetProperty(position, prop.vectorValue, false);

                if (EditorGUI.EndChangeCheck())
                {
                    prop.vectorValue = value;
                }
            }

            MaterialEditor.EndProperty();
        }
    }
}
