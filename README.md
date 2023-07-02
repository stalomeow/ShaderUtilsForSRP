# Shader Utils for SRP

Utilities for **SRP** shaders.

## HLSL Extensions

Create hlsl file using menu item `Assets/Create/Shader/HLSL Shader Include`.

## Advanced Shader GUI

Use `StaloSRPShaderGUI` as CustomEditor.

``` shaderlab
CustomEditor "StaloSRPShaderGUI"
```

## Custom Attributes in Shader

- `HelpBox(None|Info|Warning|Error, ...messages)`

    Display a HelpBox.

- `KeywordFilter(keyword, On|Off)`

    Display the decorated property only when the keyword is on/off.

- `MinMaxRange(min-limit, max-limit)`

    Display a vector property with a MinMaxSlider.

    - `property.x`: min value.
    - `property.y`: max value.

- `RampTexture`

    Display a texture as a ramp texture.

- `SingleLineTextureNoScaleOffset([color-property])`

    Display a texture property in single line. If `color-property` is specified, an additional color field will be displayed next to the texture.

- `TextureScaleOffset([indent-count])`

    Display a vector property as the scale and offset of texture with specific indent count (default is 0).

- `ToggleEnum([keyword], on-value, off-value)`

    Display a toggle. If it is on/off, the `on-value`/`off-value` will be assigned. If `keyword` is provided, the keyword will be enabled/disabled when the toggle is on/off.

    The type of the property must be `Float`/`Range`/`Int`.
