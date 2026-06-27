using System;
using System.Numerics;
using Ongenet.Engine3D.Abstractions;
using Ongenet.Engine3D.Renderer;
using Ongenet.Engine3D.Rhi;
using Silk.NET.Vulkan;

namespace Ongenet.Engine3D.Vulkan;

/// <summary>
/// A per-control offscreen Vulkan render target: color (BGRA) + depth images and their framebuffer, a
/// host-visible staging buffer for readback, a growable dynamic uniform buffer (one slot per draw), a
/// descriptor set, and a reusable command buffer + fence. Renders a scene snapshot, then copies the color
/// image into the staging buffer so <see cref="Readback"/> can hand BGRA pixels to the CPU presenter.
/// </summary>
internal sealed unsafe class VulkanRenderTarget : IRenderTarget
{
    private readonly VulkanBackend _b;
    private readonly Vk _vk;

    public int Width { get; private set; }
    public int Height { get; private set; }

    // Size-dependent resources.
    private Image _colorImage;
    private DeviceMemory _colorMem;
    private ImageView _colorView;
    private Image _depthImage;
    private DeviceMemory _depthMem;
    private ImageView _depthView;
    private Framebuffer _framebuffer;
    private Silk.NET.Vulkan.Buffer _staging;
    private DeviceMemory _stagingMem;
    private void* _stagingMapped;
    private ulong _stagingSize;

    // Persistent resources.
    private CommandPool _pool;
    private CommandBuffer _cmd;
    private Fence _fence;
    private DescriptorPool _descPool;
    private DescriptorSet _descSet;
    private Silk.NET.Vulkan.Buffer _uniform;
    private DeviceMemory _uniformMem;
    private void* _uniformMapped;
    private int _uniformCapacitySlots;

    private bool _disposed;
    private bool _hasFrame;

    public VulkanRenderTarget(VulkanBackend backend, int width, int height)
    {
        _b = backend;
        _vk = backend.Vk;
        Width = width;
        Height = height;

        AllocateCommandResources();
        EnsureUniformCapacity(64);
        AllocateSizeResources();
    }

    public void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == Width && height == Height) return;
        _vk.DeviceWaitIdle(_b.Device);
        DestroySizeResources();
        Width = width;
        Height = height;
        _hasFrame = false;
        AllocateSizeResources();
    }

    // ---------------------------------------------------------------- allocation

    private void AllocateCommandResources()
    {
        // Each target has its own command pool: pools must be externally synchronised, and each target
        // records on its own render-loop thread, so a shared pool would be a data race.
        var poolInfo2 = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = _b.QueueFamily,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit
        };
        VulkanBackend.Check(_vk.CreateCommandPool(_b.Device, in poolInfo2, null, out _pool), "CreateCommandPool(target)");

        var alloc = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _pool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1
        };
        VulkanBackend.Check(_vk.AllocateCommandBuffers(_b.Device, in alloc, out _cmd), "AllocateCommandBuffers");

        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
        VulkanBackend.Check(_vk.CreateFence(_b.Device, in fenceInfo, null, out _fence), "CreateFence");

        var poolSize = new DescriptorPoolSize { Type = DescriptorType.UniformBufferDynamic, DescriptorCount = 1 };
        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = 1,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize
        };
        VulkanBackend.Check(_vk.CreateDescriptorPool(_b.Device, in poolInfo, null, out _descPool), "CreateDescriptorPool");

        var layout = _b.DescriptorSetLayout;
        var setAlloc = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _descPool,
            DescriptorSetCount = 1,
            PSetLayouts = &layout
        };
        VulkanBackend.Check(_vk.AllocateDescriptorSets(_b.Device, in setAlloc, out _descSet), "AllocateDescriptorSets");
    }

    private void EnsureUniformCapacity(int slots)
    {
        if (slots <= _uniformCapacitySlots) return;
        if (_uniformMapped != null)
        {
            _vk.UnmapMemory(_b.Device, _uniformMem);
            _vk.DestroyBuffer(_b.Device, _uniform, null);
            _vk.FreeMemory(_b.Device, _uniformMem, null);
        }

        _uniformCapacitySlots = Math.Max(slots, 64);
        var size = _b.UniformStride * (ulong)_uniformCapacitySlots;
        (_uniform, _uniformMem) = _b.CreateBuffer(size, BufferUsageFlags.UniformBufferBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        void* mapped;
        VulkanBackend.Check(_vk.MapMemory(_b.Device, _uniformMem, 0, size, 0, &mapped), "MapMemory(uniform)");
        _uniformMapped = mapped;

        // Point the dynamic descriptor at the uniform buffer (range = one slot).
        var bufferInfo = new DescriptorBufferInfo { Buffer = _uniform, Offset = 0, Range = _b.UniformStride };
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _descSet,
            DstBinding = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.UniformBufferDynamic,
            PBufferInfo = &bufferInfo
        };
        _vk.UpdateDescriptorSets(_b.Device, 1, in write, 0, null);
    }

    private void AllocateSizeResources()
    {
        (_colorImage, _colorMem, _colorView) = CreateImage(VulkanBackend.ColorFormat,
            ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit, ImageAspectFlags.ColorBit);
        (_depthImage, _depthMem, _depthView) = CreateImage(VulkanBackend.DepthFormat,
            ImageUsageFlags.DepthStencilAttachmentBit, ImageAspectFlags.DepthBit);

        var attachments = stackalloc ImageView[2] { _colorView, _depthView };
        var fbInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = _b.RenderPass,
            AttachmentCount = 2,
            PAttachments = attachments,
            Width = (uint)Width,
            Height = (uint)Height,
            Layers = 1
        };
        VulkanBackend.Check(_vk.CreateFramebuffer(_b.Device, in fbInfo, null, out _framebuffer), "CreateFramebuffer");

        _stagingSize = (ulong)(Width * Height * 4);
        (_staging, _stagingMem) = _b.CreateBuffer(_stagingSize, BufferUsageFlags.TransferDstBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        void* mapped;
        VulkanBackend.Check(_vk.MapMemory(_b.Device, _stagingMem, 0, _stagingSize, 0, &mapped), "MapMemory(staging)");
        _stagingMapped = mapped;
    }

    private (Image, DeviceMemory, ImageView) CreateImage(Format format, ImageUsageFlags usage, ImageAspectFlags aspect)
    {
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = format,
            Extent = new Extent3D((uint)Width, (uint)Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };
        VulkanBackend.Check(_vk.CreateImage(_b.Device, in imageInfo, null, out var image), "CreateImage");

        _vk.GetImageMemoryRequirements(_b.Device, image, out var req);
        var alloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = req.Size,
            MemoryTypeIndex = _b.FindMemoryType(req.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        VulkanBackend.Check(_vk.AllocateMemory(_b.Device, in alloc, null, out var memory), "AllocateMemory(image)");
        VulkanBackend.Check(_vk.BindImageMemory(_b.Device, image, memory, 0), "BindImageMemory");

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = format,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = aspect,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };
        VulkanBackend.Check(_vk.CreateImageView(_b.Device, in viewInfo, null, out var view), "CreateImageView");
        return (image, memory, view);
    }

    // ---------------------------------------------------------------- render

    public void Render(SceneSnapshot scene)
    {
        if (_disposed) return;
        var items = scene.Items;
        EnsureUniformCapacity(Math.Max(1, items.Count));

        WriteUniforms(scene);
        RecordAndSubmit(scene);
        _hasFrame = true;
    }

    private void WriteUniforms(SceneSnapshot scene)
    {
        var aspect = Height > 0 ? (float)Width / Height : 1f;
        var view = scene.Camera.View;
        var proj = scene.Camera.ProjectionMatrix(aspect);
        proj.M22 = -proj.M22; // Vulkan clip-space Y points down

        var lightDir = new Vector4(scene.LightDirection, 0f);
        var lightColor = new Vector4(scene.LightColor, 0f);
        var ambient = new Vector4(scene.Ambient, 0f);
        var camPos = new Vector4(scene.Camera.Position, 0f);

        var items = scene.Items;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var model = item.World;
            Matrix4x4.Invert(model, out var inv);
            var u = new DrawUniforms
            {
                Mvp = model * view * proj,
                Model = model,
                NormalMat = Matrix4x4.Transpose(inv),
                BaseColor = item.BaseColor,
                Emissive = new Vector4(item.Emissive, 0f),
                LightDir = lightDir,
                LightColor = lightColor,
                Ambient = ambient,
                CamPos = camPos,
                Params = new Vector4(item.Metallic, item.Roughness, 0f, 0f)
            };
            *(DrawUniforms*)((byte*)_uniformMapped + (ulong)i * _b.UniformStride) = u;
        }
    }

    private void RecordAndSubmit(SceneSnapshot scene)
    {
        var begin = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };
        VulkanBackend.Check(_vk.BeginCommandBuffer(_cmd, in begin), "BeginCommandBuffer");

        var clearValues = stackalloc ClearValue[2];
        var c = scene.ClearColor;
        clearValues[0] = new ClearValue
        {
            Color = new ClearColorValue(c.X * c.W, c.Y * c.W, c.Z * c.W, c.W)
        };
        clearValues[1] = new ClearValue { DepthStencil = new ClearDepthStencilValue(1.0f, 0) };

        var rpBegin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _b.RenderPass,
            Framebuffer = _framebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)Width, (uint)Height)),
            ClearValueCount = 2,
            PClearValues = clearValues
        };
        _vk.CmdBeginRenderPass(_cmd, in rpBegin, SubpassContents.Inline);

        var viewport = new Viewport { X = 0, Y = 0, Width = Width, Height = Height, MinDepth = 0, MaxDepth = 1 };
        _vk.CmdSetViewport(_cmd, 0, 1, in viewport);
        var scissor = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)Width, (uint)Height));
        _vk.CmdSetScissor(_cmd, 0, 1, in scissor);

        _vk.CmdBindPipeline(_cmd, PipelineBindPoint.Graphics, _b.Pipeline);

        var items = scene.Items;
        for (var i = 0; i < items.Count; i++)
        {
            var mesh = _b.GetOrUploadMesh(items[i].Mesh);
            var vb = mesh.VertexBuffer;
            ulong offset = 0;
            _vk.CmdBindVertexBuffers(_cmd, 0, 1, in vb, in offset);
            _vk.CmdBindIndexBuffer(_cmd, mesh.IndexBuffer, 0, IndexType.Uint32);

            var dynOffset = (uint)((ulong)i * _b.UniformStride);
            var set = _descSet;
            _vk.CmdBindDescriptorSets(_cmd, PipelineBindPoint.Graphics, _b.PipelineLayout, 0, 1, in set, 1, in dynOffset);

            _vk.CmdDrawIndexed(_cmd, mesh.IndexCount, 1, 0, 0, 0);
        }

        _vk.CmdEndRenderPass(_cmd);

        // Copy the rendered color image into the host-visible staging buffer for readback.
        var region = new BufferImageCopy
        {
            BufferOffset = 0,
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                MipLevel = 0,
                BaseArrayLayer = 0,
                LayerCount = 1
            },
            ImageOffset = new Offset3D(0, 0, 0),
            ImageExtent = new Extent3D((uint)Width, (uint)Height, 1)
        };
        _vk.CmdCopyImageToBuffer(_cmd, _colorImage, ImageLayout.TransferSrcOptimal, _staging, 1, in region);

        VulkanBackend.Check(_vk.EndCommandBuffer(_cmd), "EndCommandBuffer");

        var cmd = _cmd;
        var submit = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &cmd
        };
        lock (_b.SubmitLock)
        {
            VulkanBackend.Check(_vk.QueueSubmit(_b.Queue, 1, in submit, _fence), "QueueSubmit");
            _vk.WaitForFences(_b.Device, 1, in _fence, true, ulong.MaxValue);
            _vk.ResetFences(_b.Device, 1, in _fence);
        }
    }

    public int Readback(byte[] destination)
    {
        var stride = Width * 4;
        if (!_hasFrame || _stagingMapped == null) return stride;
        var bytes = (ulong)Math.Min(destination.Length, (int)_stagingSize);
        fixed (byte* dst = destination)
            System.Buffer.MemoryCopy(_stagingMapped, dst, (ulong)destination.Length, bytes);
        return stride;
    }

    // ---------------------------------------------------------------- teardown

    private void DestroySizeResources()
    {
        if (_stagingMapped != null) { _vk.UnmapMemory(_b.Device, _stagingMem); _stagingMapped = null; }
        if (_staging.Handle != 0) { _vk.DestroyBuffer(_b.Device, _staging, null); _staging = default; }
        if (_stagingMem.Handle != 0) { _vk.FreeMemory(_b.Device, _stagingMem, null); _stagingMem = default; }
        if (_framebuffer.Handle != 0) { _vk.DestroyFramebuffer(_b.Device, _framebuffer, null); _framebuffer = default; }
        if (_colorView.Handle != 0) { _vk.DestroyImageView(_b.Device, _colorView, null); _colorView = default; }
        if (_colorImage.Handle != 0) { _vk.DestroyImage(_b.Device, _colorImage, null); _colorImage = default; }
        if (_colorMem.Handle != 0) { _vk.FreeMemory(_b.Device, _colorMem, null); _colorMem = default; }
        if (_depthView.Handle != 0) { _vk.DestroyImageView(_b.Device, _depthView, null); _depthView = default; }
        if (_depthImage.Handle != 0) { _vk.DestroyImage(_b.Device, _depthImage, null); _depthImage = default; }
        if (_depthMem.Handle != 0) { _vk.FreeMemory(_b.Device, _depthMem, null); _depthMem = default; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _vk.DeviceWaitIdle(_b.Device);

        DestroySizeResources();

        if (_uniformMapped != null) { _vk.UnmapMemory(_b.Device, _uniformMem); _uniformMapped = null; }
        if (_uniform.Handle != 0) _vk.DestroyBuffer(_b.Device, _uniform, null);
        if (_uniformMem.Handle != 0) _vk.FreeMemory(_b.Device, _uniformMem, null);
        if (_descPool.Handle != 0) _vk.DestroyDescriptorPool(_b.Device, _descPool, null);
        if (_fence.Handle != 0) _vk.DestroyFence(_b.Device, _fence, null);
        if (_pool.Handle != 0) _vk.DestroyCommandPool(_b.Device, _pool, null); // frees the command buffer too
    }
}
