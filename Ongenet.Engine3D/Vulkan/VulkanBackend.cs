using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Ongenet.Engine3D.Abstractions;
using Ongenet.Engine3D.Renderer;
using Ongenet.Engine3D.Rhi;
using Silk.NET.Core.Native;
using Silk.NET.Shaderc;
using Silk.NET.Vulkan;

namespace Ongenet.Engine3D.Vulkan;

/// <summary>
/// The Vulkan implementation of the RHI seam. Owns the shared device objects (instance, logical device,
/// graphics queue, command pool, render pass, lit pipeline, descriptor layout) and a cache of uploaded
/// meshes. Runs natively on Windows/Linux and on macOS through MoltenVK (enabled via the portability
/// instance/device extensions). Offscreen only - no swapchain or window surface is created.
/// </summary>
internal sealed unsafe class VulkanBackend : IRenderBackend
{
    internal Vk Vk { get; private set; } = null!;
    internal Instance Instance;
    internal PhysicalDevice PhysicalDevice;
    internal Device Device;
    internal Queue Queue;
    internal uint QueueFamily;
    internal CommandPool CommandPool;
    internal RenderPass RenderPass;
    internal PipelineLayout PipelineLayout;
    internal Pipeline Pipeline;
    internal DescriptorSetLayout DescriptorSetLayout;
    internal ulong UniformStride { get; private set; }

    /// <summary>Serialises queue submits/waits across render targets that share this backend's single queue.</summary>
    internal readonly object SubmitLock = new();

    internal const Format ColorFormat = Format.B8G8R8A8Unorm;
    internal const Format DepthFormat = Format.D32Sfloat;

    private ShaderModule _vertModule;
    private ShaderModule _fragModule;
    private readonly Dictionary<int, GpuMesh> _meshes = new();
    private readonly object _meshLock = new();
    private readonly ILogger? _log;
    private bool _disposed;

    public VulkanBackend(ILogger? log = null) => _log = log;

    public string Name { get; private set; } = "Vulkan";
    public bool IsInitialized { get; private set; }

    public bool TryInitialize()
    {
        try
        {
            // Quiet MoltenVK's verbose [mvk-info] banner (errors only). Must be set before instance creation.
            if (OperatingSystem.IsMacOS())
                Environment.SetEnvironmentVariable("MVK_CONFIG_LOG_LEVEL", "1");

            Vk = Vk.GetApi();
            CreateInstance();
            PickPhysicalDevice();
            CreateLogicalDevice();
            CreateCommandPool();
            CreateDescriptorLayout();
            CreateRenderPass();
            CreatePipeline();
            IsInitialized = true;
            _log?.LogInformation("3D engine: {Backend} initialised.", Name);
            return true;
        }
        catch (Exception ex)
        {
            _log?.LogWarning("3D engine: Vulkan init failed ({Message}). 3D controls will show a placeholder.", ex.Message);
            Dispose();
            return false;
        }
    }

    // The instance/device extensions the driver actually advertises (so we never request a missing one,
    // which would fail device creation - the portability extensions in particular vary by driver/loader).
    private HashSet<string> EnumerateInstanceExtensions()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        uint count = 0;
        if (Vk.EnumerateInstanceExtensionProperties((byte*)null, ref count, null) != Result.Success || count == 0)
            return set;
        var props = new ExtensionProperties[count];
        fixed (ExtensionProperties* p = props)
        {
            if (Vk.EnumerateInstanceExtensionProperties((byte*)null, ref count, p) != Result.Success) return set;
            for (var i = 0; i < count; i++)
            {
                var name = Marshal.PtrToStringAnsi((nint)p[i].ExtensionName);
                if (name is not null) set.Add(name);
            }
        }

        return set;
    }

    private HashSet<string> EnumerateDeviceExtensions(PhysicalDevice device)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        uint count = 0;
        if (Vk.EnumerateDeviceExtensionProperties(device, (byte*)null, ref count, null) != Result.Success || count == 0)
            return set;
        var props = new ExtensionProperties[count];
        fixed (ExtensionProperties* p = props)
        {
            if (Vk.EnumerateDeviceExtensionProperties(device, (byte*)null, ref count, p) != Result.Success) return set;
            for (var i = 0; i < count; i++)
            {
                var name = Marshal.PtrToStringAnsi((nint)p[i].ExtensionName);
                if (name is not null) set.Add(name);
            }
        }

        return set;
    }

    public IRenderTarget? CreateRenderTarget(int width, int height)
    {
        if (!IsInitialized) return null;
        try
        {
            return new VulkanRenderTarget(this, Math.Max(1, width), Math.Max(1, height));
        }
        catch
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- instance

    private void CreateInstance()
    {
        var appName = (byte*)SilkMarshal.StringToPtr("Ongenet");
        var engineName = (byte*)SilkMarshal.StringToPtr("Ongenet.Engine3D");
        var appInfo = new ApplicationInfo
        {
            SType = StructureType.ApplicationInfo,
            PApplicationName = appName,
            ApplicationVersion = Vk.Version10,
            PEngineName = engineName,
            EngineVersion = Vk.Version10,
            ApiVersion = Vk.Version11
        };

        var createInfo = new InstanceCreateInfo
        {
            SType = StructureType.InstanceCreateInfo,
            PApplicationInfo = &appInfo
        };

        // Enable the portability extensions only when the loaded driver advertises them (MoltenVK does), so
        // we never trip VK_ERROR_EXTENSION_NOT_PRESENT across loader/MoltenVK/version differences.
        var available = EnumerateInstanceExtensions();
        var extensions = new List<string>();
        if (OperatingSystem.IsMacOS())
        {
            Name = "Vulkan (MoltenVK)";
            if (available.Contains("VK_KHR_portability_enumeration"))
            {
                extensions.Add("VK_KHR_portability_enumeration");
                createInfo.Flags = InstanceCreateFlags.EnumeratePortabilityBitKhr;
            }

            if (available.Contains("VK_KHR_get_physical_device_properties2"))
                extensions.Add("VK_KHR_get_physical_device_properties2");
        }

        var extPtr = extensions.Count > 0 ? (byte**)SilkMarshal.StringArrayToPtr(extensions) : null;
        createInfo.EnabledExtensionCount = (uint)extensions.Count;
        createInfo.PpEnabledExtensionNames = extPtr;

        try
        {
            Check(Vk.CreateInstance(in createInfo, null, out Instance), "CreateInstance");
        }
        finally
        {
            SilkMarshal.Free((nint)appName);
            SilkMarshal.Free((nint)engineName);
            if (extPtr != null) SilkMarshal.Free((nint)extPtr);
        }
    }

    private void PickPhysicalDevice()
    {
        uint count = 0;
        Check(Vk.EnumeratePhysicalDevices(Instance, ref count, null), "EnumeratePhysicalDevices");
        if (count == 0) throw new InvalidOperationException("No Vulkan physical devices.");

        var devices = stackalloc PhysicalDevice[(int)count];
        Check(Vk.EnumeratePhysicalDevices(Instance, ref count, devices), "EnumeratePhysicalDevices");

        PhysicalDevice chosen = default;
        var chosenScore = -1;
        for (var i = 0; i < count; i++)
        {
            var dev = devices[i];
            if (!TryFindGraphicsQueue(dev, out var family)) continue;
            Vk.GetPhysicalDeviceProperties(dev, out var props);
            var score = props.DeviceType == PhysicalDeviceType.DiscreteGpu ? 2
                : props.DeviceType == PhysicalDeviceType.IntegratedGpu ? 1 : 0;
            if (score > chosenScore)
            {
                chosenScore = score;
                chosen = dev;
                QueueFamily = family;
            }
        }

        if (chosenScore < 0) throw new InvalidOperationException("No Vulkan device with a graphics queue.");
        PhysicalDevice = chosen;

        Vk.GetPhysicalDeviceProperties(PhysicalDevice, out var p);
        var align = p.Limits.MinUniformBufferOffsetAlignment;
        if (align == 0) align = 256;
        var size = (ulong)DrawUniforms.SizeInBytes;
        UniformStride = ((size + align - 1) / align) * align;
    }

    private bool TryFindGraphicsQueue(PhysicalDevice dev, out uint family)
    {
        family = 0;
        uint count = 0;
        Vk.GetPhysicalDeviceQueueFamilyProperties(dev, ref count, null);
        if (count == 0) return false;
        var props = stackalloc QueueFamilyProperties[(int)count];
        Vk.GetPhysicalDeviceQueueFamilyProperties(dev, ref count, props);
        for (uint i = 0; i < count; i++)
        {
            if ((props[i].QueueFlags & QueueFlags.GraphicsBit) != 0)
            {
                family = i;
                return true;
            }
        }

        return false;
    }

    private void CreateLogicalDevice()
    {
        var priority = 1.0f;
        var queueInfo = new DeviceQueueCreateInfo
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = QueueFamily,
            QueueCount = 1,
            PQueuePriorities = &priority
        };

        var features = new PhysicalDeviceFeatures();
        var createInfo = new DeviceCreateInfo
        {
            SType = StructureType.DeviceCreateInfo,
            QueueCreateInfoCount = 1,
            PQueueCreateInfos = &queueInfo,
            PEnabledFeatures = &features
        };

        // VK_KHR_portability_subset must be enabled if (and only if) the device exposes it (a portability
        // requirement satisfied by MoltenVK); enabling it when absent - or omitting it when present - both fail.
        var deviceAvailable = EnumerateDeviceExtensions(PhysicalDevice);
        var deviceExtList = new List<string>();
        if (deviceAvailable.Contains("VK_KHR_portability_subset"))
            deviceExtList.Add("VK_KHR_portability_subset");

        var extPtr = deviceExtList.Count > 0 ? (byte**)SilkMarshal.StringArrayToPtr(deviceExtList) : null;
        createInfo.EnabledExtensionCount = (uint)deviceExtList.Count;
        createInfo.PpEnabledExtensionNames = extPtr;

        try
        {
            Check(Vk.CreateDevice(PhysicalDevice, in createInfo, null, out Device), "CreateDevice");
        }
        finally
        {
            if (extPtr != null) SilkMarshal.Free((nint)extPtr);
        }

        Vk.GetDeviceQueue(Device, QueueFamily, 0, out Queue);
    }

    private void CreateCommandPool()
    {
        var info = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = QueueFamily,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit
        };
        Check(Vk.CreateCommandPool(Device, in info, null, out CommandPool), "CreateCommandPool");
    }

    private void CreateDescriptorLayout()
    {
        var binding = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.UniformBufferDynamic,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit
        };
        var info = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &binding
        };
        Check(Vk.CreateDescriptorSetLayout(Device, in info, null, out DescriptorSetLayout), "CreateDescriptorSetLayout");

        var layout = DescriptorSetLayout;
        var pipeInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &layout
        };
        Check(Vk.CreatePipelineLayout(Device, in pipeInfo, null, out PipelineLayout), "CreatePipelineLayout");
    }

    private void CreateRenderPass()
    {
        var attachments = stackalloc AttachmentDescription[2];
        attachments[0] = new AttachmentDescription
        {
            Format = ColorFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.TransferSrcOptimal
        };
        attachments[1] = new AttachmentDescription
        {
            Format = DepthFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.DontCare,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.DepthStencilAttachmentOptimal
        };

        var colorRef = new AttachmentReference { Attachment = 0, Layout = ImageLayout.ColorAttachmentOptimal };
        var depthRef = new AttachmentReference { Attachment = 1, Layout = ImageLayout.DepthStencilAttachmentOptimal };

        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorRef,
            PDepthStencilAttachment = &depthRef
        };

        var deps = stackalloc SubpassDependency[2];
        deps[0] = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
            SrcAccessMask = 0,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit
        };
        deps[1] = new SubpassDependency
        {
            SrcSubpass = 0,
            DstSubpass = Vk.SubpassExternal,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstStageMask = PipelineStageFlags.TransferBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstAccessMask = AccessFlags.TransferReadBit
        };

        var info = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 2,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 2,
            PDependencies = deps
        };
        Check(Vk.CreateRenderPass(Device, in info, null, out RenderPass), "CreateRenderPass");
    }

    private void CreatePipeline()
    {
        var vertSpirv = ShaderCompiler.Compile(ShaderSource.Vertex, ShaderKind.VertexShader, "lit.vert");
        var fragSpirv = ShaderCompiler.Compile(ShaderSource.Fragment, ShaderKind.FragmentShader, "lit.frag");
        _vertModule = CreateShaderModule(vertSpirv);
        _fragModule = CreateShaderModule(fragSpirv);

        var entry = (byte*)SilkMarshal.StringToPtr("main");
        var stages = stackalloc PipelineShaderStageCreateInfo[2];
        stages[0] = new PipelineShaderStageCreateInfo
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.VertexBit,
            Module = _vertModule,
            PName = entry
        };
        stages[1] = new PipelineShaderStageCreateInfo
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.FragmentBit,
            Module = _fragModule,
            PName = entry
        };

        var binding = new VertexInputBindingDescription
        {
            Binding = 0,
            Stride = (uint)Abstractions.Vertex.SizeInBytes,
            InputRate = VertexInputRate.Vertex
        };
        var attrs = stackalloc VertexInputAttributeDescription[3];
        attrs[0] = new VertexInputAttributeDescription { Location = 0, Binding = 0, Format = Format.R32G32B32Sfloat, Offset = (uint)Abstractions.Vertex.PositionOffset };
        attrs[1] = new VertexInputAttributeDescription { Location = 1, Binding = 0, Format = Format.R32G32B32Sfloat, Offset = (uint)Abstractions.Vertex.NormalOffset };
        attrs[2] = new VertexInputAttributeDescription { Location = 2, Binding = 0, Format = Format.R32G32B32A32Sfloat, Offset = (uint)Abstractions.Vertex.ColorOffset };

        var vertexInput = new PipelineVertexInputStateCreateInfo
        {
            SType = StructureType.PipelineVertexInputStateCreateInfo,
            VertexBindingDescriptionCount = 1,
            PVertexBindingDescriptions = &binding,
            VertexAttributeDescriptionCount = 3,
            PVertexAttributeDescriptions = attrs
        };

        var inputAssembly = new PipelineInputAssemblyStateCreateInfo
        {
            SType = StructureType.PipelineInputAssemblyStateCreateInfo,
            Topology = PrimitiveTopology.TriangleList
        };

        var viewportState = new PipelineViewportStateCreateInfo
        {
            SType = StructureType.PipelineViewportStateCreateInfo,
            ViewportCount = 1,
            ScissorCount = 1
        };

        var raster = new PipelineRasterizationStateCreateInfo
        {
            SType = StructureType.PipelineRasterizationStateCreateInfo,
            PolygonMode = PolygonMode.Fill,
            CullMode = CullModeFlags.None,
            FrontFace = FrontFace.CounterClockwise,
            LineWidth = 1.0f
        };

        var multisample = new PipelineMultisampleStateCreateInfo
        {
            SType = StructureType.PipelineMultisampleStateCreateInfo,
            RasterizationSamples = SampleCountFlags.Count1Bit
        };

        var depthStencil = new PipelineDepthStencilStateCreateInfo
        {
            SType = StructureType.PipelineDepthStencilStateCreateInfo,
            DepthTestEnable = true,
            DepthWriteEnable = true,
            DepthCompareOp = CompareOp.LessOrEqual
        };

        var blendAttachment = new PipelineColorBlendAttachmentState
        {
            BlendEnable = true,
            SrcColorBlendFactor = BlendFactor.One, // premultiplied output
            DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
            ColorBlendOp = BlendOp.Add,
            SrcAlphaBlendFactor = BlendFactor.One,
            DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
            AlphaBlendOp = BlendOp.Add,
            ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit
        };
        var blend = new PipelineColorBlendStateCreateInfo
        {
            SType = StructureType.PipelineColorBlendStateCreateInfo,
            AttachmentCount = 1,
            PAttachments = &blendAttachment
        };

        var dynamicStates = stackalloc DynamicState[2] { DynamicState.Viewport, DynamicState.Scissor };
        var dynamic = new PipelineDynamicStateCreateInfo
        {
            SType = StructureType.PipelineDynamicStateCreateInfo,
            DynamicStateCount = 2,
            PDynamicStates = dynamicStates
        };

        var info = new GraphicsPipelineCreateInfo
        {
            SType = StructureType.GraphicsPipelineCreateInfo,
            StageCount = 2,
            PStages = stages,
            PVertexInputState = &vertexInput,
            PInputAssemblyState = &inputAssembly,
            PViewportState = &viewportState,
            PRasterizationState = &raster,
            PMultisampleState = &multisample,
            PDepthStencilState = &depthStencil,
            PColorBlendState = &blend,
            PDynamicState = &dynamic,
            Layout = PipelineLayout,
            RenderPass = RenderPass,
            Subpass = 0
        };

        try
        {
            Check(Vk.CreateGraphicsPipelines(Device, default, 1, in info, null, out var pipeline), "CreateGraphicsPipelines");
            Pipeline = pipeline;
        }
        finally
        {
            SilkMarshal.Free((nint)entry);
        }
    }

    private ShaderModule CreateShaderModule(byte[] spirv)
    {
        fixed (byte* code = spirv)
        {
            var info = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)spirv.Length,
                PCode = (uint*)code
            };
            Check(Vk.CreateShaderModule(Device, in info, null, out var module), "CreateShaderModule");
            return module;
        }
    }

    // ---------------------------------------------------------------- shared helpers (used by render target)

    internal uint FindMemoryType(uint typeBits, MemoryPropertyFlags properties)
    {
        Vk.GetPhysicalDeviceMemoryProperties(PhysicalDevice, out var memProps);
        for (uint i = 0; i < memProps.MemoryTypeCount; i++)
        {
            if ((typeBits & (1u << (int)i)) != 0 &&
                (memProps.MemoryTypes[(int)i].PropertyFlags & properties) == properties)
                return i;
        }

        throw new InvalidOperationException("No suitable Vulkan memory type.");
    }

    internal (Silk.NET.Vulkan.Buffer Buffer, DeviceMemory Memory) CreateBuffer(
        ulong size, BufferUsageFlags usage, MemoryPropertyFlags properties)
    {
        var info = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = usage,
            SharingMode = SharingMode.Exclusive
        };
        Check(Vk.CreateBuffer(Device, in info, null, out var buffer), "CreateBuffer");

        Vk.GetBufferMemoryRequirements(Device, buffer, out var req);
        var alloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = req.Size,
            MemoryTypeIndex = FindMemoryType(req.MemoryTypeBits, properties)
        };
        Check(Vk.AllocateMemory(Device, in alloc, null, out var memory), "AllocateMemory(buffer)");
        Check(Vk.BindBufferMemory(Device, buffer, memory, 0), "BindBufferMemory");
        return (buffer, memory);
    }

    internal GpuMesh GetOrUploadMesh(MeshData mesh)
    {
        lock (_meshLock)
        {
            if (_meshes.TryGetValue(mesh.Id, out var existing))
            {
                // Dynamic mesh: its geometry changed since we last uploaded -> re-copy into the same buffer.
                var rev = mesh.Revision;
                if (existing.Revision != rev)
                {
                    var size = (ulong)(mesh.VertexCount * Abstractions.Vertex.SizeInBytes);
                    void* map;
                    Check(Vk.MapMemory(Device, existing.VertexMemory, 0, size, 0, &map), "MapMemory(vb update)");
                    fixed (void* src = mesh.Vertices) System.Buffer.MemoryCopy(src, map, size, size);
                    Vk.UnmapMemory(Device, existing.VertexMemory);
                    existing.Revision = rev;
                    _meshes[mesh.Id] = existing;
                }

                return existing;
            }

            var vbSize = (ulong)(mesh.VertexCount * Abstractions.Vertex.SizeInBytes);
            var ibSize = (ulong)(mesh.IndexCount * sizeof(uint));
            var (vb, vbMem) = CreateBuffer(vbSize, BufferUsageFlags.VertexBufferBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
            var (ib, ibMem) = CreateBuffer(ibSize, BufferUsageFlags.IndexBufferBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            void* dst;
            Check(Vk.MapMemory(Device, vbMem, 0, vbSize, 0, &dst), "MapMemory(vb)");
            fixed (void* src = mesh.Vertices) System.Buffer.MemoryCopy(src, dst, vbSize, vbSize);
            Vk.UnmapMemory(Device, vbMem);

            Check(Vk.MapMemory(Device, ibMem, 0, ibSize, 0, &dst), "MapMemory(ib)");
            fixed (void* src = mesh.Indices) System.Buffer.MemoryCopy(src, dst, ibSize, ibSize);
            Vk.UnmapMemory(Device, ibMem);

            var gpu = new GpuMesh
            {
                VertexBuffer = vb,
                VertexMemory = vbMem,
                IndexBuffer = ib,
                IndexMemory = ibMem,
                IndexCount = (uint)mesh.IndexCount,
                Revision = mesh.Revision
            };
            _meshes[mesh.Id] = gpu;
            return gpu;
        }
    }

    internal static void Check(Result result, string what)
    {
        if (result != Result.Success)
            throw new InvalidOperationException($"Vulkan {what} failed: {result}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        IsInitialized = false;

        if (Vk is null) return;
        if (Device.Handle != 0)
        {
            Vk.DeviceWaitIdle(Device);

            lock (_meshLock)
            {
                foreach (var m in _meshes.Values) m.Dispose(Vk, Device);
                _meshes.Clear();
            }

            if (Pipeline.Handle != 0) Vk.DestroyPipeline(Device, Pipeline, null);
            if (PipelineLayout.Handle != 0) Vk.DestroyPipelineLayout(Device, PipelineLayout, null);
            if (DescriptorSetLayout.Handle != 0) Vk.DestroyDescriptorSetLayout(Device, DescriptorSetLayout, null);
            if (RenderPass.Handle != 0) Vk.DestroyRenderPass(Device, RenderPass, null);
            if (_vertModule.Handle != 0) Vk.DestroyShaderModule(Device, _vertModule, null);
            if (_fragModule.Handle != 0) Vk.DestroyShaderModule(Device, _fragModule, null);
            if (CommandPool.Handle != 0) Vk.DestroyCommandPool(Device, CommandPool, null);
            Vk.DestroyDevice(Device, null);
        }

        if (Instance.Handle != 0) Vk.DestroyInstance(Instance, null);
        Vk.Dispose();
    }
}

/// <summary>Uploaded GPU buffers for one mesh, cached by <see cref="MeshData.Id"/> on the backend.</summary>
internal struct GpuMesh
{
    public Silk.NET.Vulkan.Buffer VertexBuffer;
    public DeviceMemory VertexMemory;
    public Silk.NET.Vulkan.Buffer IndexBuffer;
    public DeviceMemory IndexMemory;
    public uint IndexCount;
    public int Revision;

    public unsafe void Dispose(Vk vk, Device device)
    {
        if (VertexBuffer.Handle != 0) vk.DestroyBuffer(device, VertexBuffer, null);
        if (VertexMemory.Handle != 0) vk.FreeMemory(device, VertexMemory, null);
        if (IndexBuffer.Handle != 0) vk.DestroyBuffer(device, IndexBuffer, null);
        if (IndexMemory.Handle != 0) vk.FreeMemory(device, IndexMemory, null);
    }
}
