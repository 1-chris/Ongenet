using System.Numerics;
using System.Runtime.InteropServices;

namespace Ongenet.Engine3D.Renderer;

/// <summary>
/// CPU mirror of the shader's uniform block (std140). All members are 16-byte aligned (mat4 = 64, vec4 =
/// 16), so the sequential layout matches std140 with no extra padding. One of these is written per draw
/// into the dynamic uniform buffer.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct DrawUniforms
{
    public Matrix4x4 Mvp;
    public Matrix4x4 Model;
    public Matrix4x4 NormalMat;
    public Vector4 BaseColor;
    public Vector4 Emissive;
    public Vector4 LightDir;
    public Vector4 LightColor;
    public Vector4 Ambient;
    public Vector4 CamPos;
    public Vector4 Params; // x = metallic, y = roughness

    public static unsafe int SizeInBytes => sizeof(DrawUniforms);
}
