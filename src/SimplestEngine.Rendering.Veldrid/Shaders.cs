namespace SimplestEngine.Rendering.Veldrid;

/// <summary>
/// Inlined SPIR-V cross-compilable canvas shader (GLSL 450).
/// Veldrid.SPIRV translates to GLSL/HLSL/MSL at runtime.
/// </summary>
internal static class CanvasShaders
{
    public const string VertexCode = @"
#version 450

layout(set = 0, binding = 0) uniform Projection {
    mat4 uProj;
};

// Names must match VertexElementDescription.Name — SPIRV-Cross + D3D11 reflect these
// into the vertex bytecode; mismatched identifiers cause CreateInputLayout E_INVALIDARG.
layout(location = 0) in vec2 Position;
layout(location = 1) in vec2 UV;
layout(location = 2) in vec4 Color;

layout(location = 0) out vec2 v_UV;
layout(location = 1) out vec4 v_Color;

void main()
{
    gl_Position = uProj * vec4(Position, 0.0, 1.0);
    v_UV = UV;
    v_Color = Color;
}
";

    public const string FragmentCode = @"
#version 450

layout(set = 1, binding = 0) uniform texture2D u_Texture;
layout(set = 1, binding = 1) uniform sampler u_Sampler;

layout(location = 0) in vec2 v_UV;
layout(location = 1) in vec4 v_Color;

layout(location = 0) out vec4 out_Color;

void main()
{
    out_Color = texture(sampler2D(u_Texture, u_Sampler), v_UV) * v_Color;
}
";
}
