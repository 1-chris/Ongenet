using System;
using System.Runtime.InteropServices;
using System.Text;
using Silk.NET.Shaderc;

namespace Ongenet.Engine3D.Vulkan;

/// <summary>
/// Compiles GLSL (Vulkan dialect) to SPIR-V at runtime via libshaderc (bundled by Silk.NET.Shaderc), so
/// the engine ships no precompiled binaries and needs no build-time glslang. Used once per session start.
/// </summary>
internal static unsafe class ShaderCompiler
{
    public static byte[] Compile(string source, ShaderKind kind, string name)
    {
        var api = Shaderc.GetApi();
        var compiler = api.CompilerInitialize();
        if (compiler == null) throw new InvalidOperationException("shaderc: failed to initialise compiler.");

        var options = api.CompileOptionsInitialize();
        try
        {
            var size = (nuint)Encoding.UTF8.GetByteCount(source);
            var result = api.CompileIntoSpv(compiler, source, size, kind, name, "main", options);
            if (result == null) throw new InvalidOperationException($"shaderc: null result compiling {name}.");

            try
            {
                var status = api.ResultGetCompilationStatus(result);
                if (status != CompilationStatus.Success)
                {
                    var msgPtr = api.ResultGetErrorMessage(result);
                    var msg = msgPtr == null ? "(no message)" : Marshal.PtrToStringAnsi((nint)msgPtr) ?? "(no message)";
                    throw new InvalidOperationException($"shaderc: failed to compile {name}: {msg}");
                }

                var len = (int)api.ResultGetLength(result);
                var bytes = api.ResultGetBytes(result);
                var spirv = new byte[len];
                fixed (byte* dst = spirv)
                    Buffer.MemoryCopy(bytes, dst, len, len);
                return spirv;
            }
            finally
            {
                api.ResultRelease(result);
            }
        }
        finally
        {
            api.CompileOptionsRelease(options);
            api.CompilerRelease(compiler);
        }
    }
}
