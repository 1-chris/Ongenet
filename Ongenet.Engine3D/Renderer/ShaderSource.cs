namespace Ongenet.Engine3D.Renderer;

/// <summary>
/// GLSL (Vulkan dialect) source for the reference lit shader, compiled to SPIR-V at session start by
/// <see cref="Vulkan.ShaderCompiler"/>. One dynamic uniform block carries the per-draw transform +
/// material + lighting (the engine binds it with a per-draw dynamic offset). The lighting is a cheap
/// Blinn-Phong approximation modulated by the material's metallic/roughness, plus an emissive term.
/// </summary>
internal static class ShaderSource
{
    public const string UniformBlock = @"
layout(set = 0, binding = 0) uniform Ubo {
    mat4 mvp;
    mat4 model;
    mat4 normalMat;
    vec4 baseColor;
    vec4 emissive;    // rgb = emissive
    vec4 lightDir;    // xyz = directional light travel direction
    vec4 lightColor;  // rgb = directional colour * intensity
    vec4 ambient;     // rgb = ambient colour * intensity
    vec4 camPos;      // xyz = world-space camera position
    vec4 params;      // x = metallic, y = roughness
} ubo;
";

    public const string Vertex = @"#version 450
layout(location = 0) in vec3 inPos;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec4 inColor;
" + UniformBlock + @"
layout(location = 0) out vec3 vNormal;
layout(location = 1) out vec4 vColor;
layout(location = 2) out vec3 vWorldPos;
void main() {
    vec4 world = ubo.model * vec4(inPos, 1.0);
    gl_Position = ubo.mvp * vec4(inPos, 1.0);
    vNormal = mat3(ubo.normalMat) * inNormal;
    vColor = inColor * ubo.baseColor;
    vWorldPos = world.xyz;
}
";

    public const string Fragment = @"#version 450
layout(location = 0) in vec3 vNormal;
layout(location = 1) in vec4 vColor;
layout(location = 2) in vec3 vWorldPos;
" + UniformBlock + @"
layout(location = 0) out vec4 outColor;
void main() {
    vec3 N = normalize(vNormal);
    vec3 L = normalize(-ubo.lightDir.xyz);
    float diff = max(dot(N, L), 0.0);

    vec3 V = normalize(ubo.camPos.xyz - vWorldPos);
    vec3 H = normalize(L + V);
    float rough = clamp(ubo.params.y, 0.04, 1.0);
    float shininess = mix(8.0, 256.0, 1.0 - rough);
    float spec = pow(max(dot(N, H), 0.0), shininess) * (1.0 - rough);
    float metallic = clamp(ubo.params.x, 0.0, 1.0);
    float specStrength = mix(0.04, 1.0, metallic);

    vec3 albedo = vColor.rgb;
    vec3 lit = albedo * (ubo.ambient.rgb + ubo.lightColor.rgb * diff);
    vec3 color = lit + ubo.lightColor.rgb * spec * specStrength + ubo.emissive.rgb;

    // Premultiplied alpha so it composes correctly with the Avalonia surface.
    float a = clamp(vColor.a, 0.0, 1.0);
    outColor = vec4(clamp(color, 0.0, 1.0) * a, a);
}
";
}
