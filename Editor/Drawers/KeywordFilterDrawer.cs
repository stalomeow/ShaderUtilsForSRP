using JetBrains.Annotations;
using UnityEditor;
using UnityEngine;

namespace Stalo.ShaderUtils.Editor.Drawers
{
    [PublicAPI]
    internal class KeywordFilterDrawer : MaterialPropertyDrawer
    {
        private readonly string m_Keyword;
        private readonly bool m_State;

        public KeywordFilterDrawer(string keyword, string state = "On")
        {
            string stateLower = state.ToLower();

            if (stateLower is not ("on" or "off"))
            {
                Debug.LogWarning($"Invalid argument '{state}' in KeywordFilter. Use 'On' or 'Off' instead.");
            }

            m_Keyword = keyword;
            m_State = (stateLower != "off");
        }

        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
        {
            if (prop.hasMixedValue || MatchKeywordState(editor.target as Material))
            {
                return MaterialEditor.GetDefaultPropertyHeight(prop);
            }

            return 0f;
        }

        public override void OnGUI(Rect position, MaterialProperty prop, string label, MaterialEditor editor)
        {
            if (prop.hasMixedValue || MatchKeywordState(editor.target as Material))
            {
                editor.DefaultShaderProperty(position, prop, label);
                return;
            }

            // remove useless references
            switch (prop.type)
            {
                case MaterialProperty.PropType.Texture:
                    prop.textureValue = null;
                    break;
            }
        }

        private bool MatchKeywordState(Material material)
        {
            return material.IsKeywordEnabled(m_Keyword) == m_State;
        }
    }
}
