using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;
using RenderCore;
using System;

namespace KittenExtensions;

public class FinalPostRenderer : RenderTechnique
{
    private readonly VkRenderPass finalRenderPass;
    private VkExtent2D extent;

    private readonly DescriptorSetLayoutEx bindingLayout;
    private readonly VkDescriptorSet bindingSet;
    private Framebuffer.FramebufferAttachment Source;

    // The image that will be sampled by the post shader (set by caller)
    // Typically this will be the final color attachment from the main render.
    //public Framebuffer.FramebufferAttachment Source { get; private set; }

    #region renderpasses
    /// <summary>
    /// Creates a single render pass with multiple subpasses, where each subpass
    /// takes as input the output of the previous subpass via input attachments
    /// </summary>
    internal static unsafe RenderPassState CreateMultiRenderPass(Renderer renderer, int subpassCount)
    {
        VkSubpassDescription* subpasses =
            stackalloc VkSubpassDescription[subpassCount];

        VkAttachmentReference* colorRefs =
            stackalloc VkAttachmentReference[subpassCount];

        VkAttachmentReference* inputRefs =
            stackalloc VkAttachmentReference[subpassCount];

        VkSubpassDependency* dependencies =
            stackalloc VkSubpassDependency[subpassCount];

        for (int i = 0; i < subpassCount; i++)
        {
            colorRefs[i] = new VkAttachmentReference
            {
                Attachment = (i + 1),
                Layout = VkImageLayout.ColorAttachmentOptimal
            };

            inputRefs[i] = new VkAttachmentReference
            {
                Attachment = i,
                Layout = VkImageLayout.AttachmentOptimal  // ← Changed from ColorAttachmentOptimal
            };

            subpasses[i] = new VkSubpassDescription
            {
                PipelineBindPoint = VkPipelineBindPoint.Graphics,
                ColorAttachmentCount = 1,
                ColorAttachments = &colorRefs[i],
                InputAttachmentCount = 1,
                InputAttachments = &inputRefs[i],
            };

            dependencies[i] = new VkSubpassDependency
            {
                SrcSubpass = i - 1,
                DstSubpass = i,
                SrcStageMask = VkPipelineStageFlags.ColorAttachmentOutputBit,
                DstStageMask = VkPipelineStageFlags.FragmentShaderBit,
                SrcAccessMask = VkAccessFlags.ColorAttachmentWriteBit,
                DstAccessMask = VkAccessFlags.InputAttachmentReadBit,
                DependencyFlags = VkDependencyFlags.ByRegionBit
            };
        }

        dependencies[0] = new VkSubpassDependency
        {
            SrcSubpass = VK.SUBPASS_EXTERNAL,
            DstSubpass = 0,
            SrcStageMask = VkPipelineStageFlags.FragmentShaderBit,
            DstStageMask = VkPipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = VkAccessFlags.ShaderReadBit,
            DstAccessMask = VkAccessFlags.ColorAttachmentWriteBit,
            DependencyFlags = VkDependencyFlags.ByRegionBit
        };

        VkAttachmentDescription* attachments =
            stackalloc VkAttachmentDescription[subpassCount + 1];

        // Attachment 0: Input attachment from external source
        attachments[0] = new VkAttachmentDescription
        {
            Format = renderer.ColorFormat,
            Samples = VkSampleCountFlags._1Bit,
            LoadOp = VkAttachmentLoadOp.Load,
            StoreOp = VkAttachmentStoreOp.DontCare,
            StencilLoadOp = VkAttachmentLoadOp.DontCare,
            StencilStoreOp = VkAttachmentStoreOp.DontCare,
            InitialLayout = VkImageLayout.ShaderReadOnlyOptimal,
            FinalLayout = VkImageLayout.ShaderReadOnlyOptimal
        };

        // Attachments 1+: Intermediate and final color outputs
        for (int i = 1; i <= subpassCount; i++)
        {
            attachments[i] = new VkAttachmentDescription
            {
                Format = renderer.ColorFormat,
                Samples = VkSampleCountFlags._1Bit,
                LoadOp = VkAttachmentLoadOp.Clear,
                StoreOp = VkAttachmentStoreOp.Store,
                StencilLoadOp = VkAttachmentLoadOp.DontCare,
                StencilStoreOp = VkAttachmentStoreOp.DontCare,

                // Let render pass handle transitions between subpasses
                InitialLayout = VkImageLayout.Undefined,

                // FINAL layout is irrelevant for intermediate attachments,
                // but MUST be something compatible with last usage
                FinalLayout = VkImageLayout.ShaderReadOnlyOptimal
            };
        }

        VkRenderPassCreateInfo createInfo = new VkRenderPassCreateInfo();
        createInfo.AttachmentCount = subpassCount + 1;
        createInfo.Attachments = attachments;
        createInfo.SubpassCount = subpassCount;
        createInfo.Subpasses = subpasses;
        createInfo.DependencyCount = subpassCount;
        createInfo.Dependencies = dependencies;

        VkRenderPass renderPass = renderer.Device.CreateRenderPass(in createInfo, null);

        return new RenderPassState
        {
            Pass = renderPass,
            SampleCount = VkSampleCountFlags._1Bit,
        };
    }

    /// <summary>
    /// Creates a renderpass for a shader that uses a sampler2d input instead of an input attachment and thus needs its own renderpass.
    /// </summary>
    internal static unsafe RenderPassState CreateSingleRenderPass(Renderer renderer)
    {
        VkAttachmentDescription colorAttachment = new VkAttachmentDescription();
        colorAttachment.Format = renderer.ColorFormat;
        colorAttachment.Samples = VkSampleCountFlags._1Bit;

        colorAttachment.LoadOp = VkAttachmentLoadOp.Load;
        colorAttachment.StoreOp = VkAttachmentStoreOp.Store;

        colorAttachment.StencilLoadOp = VkAttachmentLoadOp.Load;
        colorAttachment.StencilStoreOp = VkAttachmentStoreOp.Store;

        colorAttachment.InitialLayout = VkImageLayout.ColorAttachmentOptimal;
        colorAttachment.FinalLayout = VkImageLayout.PresentSrcKHR;

        VkAttachmentReference colorRef = new VkAttachmentReference();
        colorRef.Attachment = 0;
        colorRef.Layout = VkImageLayout.ColorAttachmentOptimal;

        VkSubpassDescription subpass = new VkSubpassDescription();
        subpass.PipelineBindPoint = VkPipelineBindPoint.Graphics;
        subpass.ColorAttachmentCount = 1;
        subpass.ColorAttachments = &colorRef;

        VkSubpassDependency dependency = new VkSubpassDependency();
        dependency.SrcSubpass = -1;
        dependency.DstSubpass = 0;
        dependency.SrcStageMask = VkPipelineStageFlags.ColorAttachmentOutputBit;
        dependency.DstStageMask = VkPipelineStageFlags.ColorAttachmentOutputBit;
        dependency.SrcAccessMask = VkAccessFlags.ColorAttachmentWriteBit;
        dependency.DstAccessMask =
            VkAccessFlags.ColorAttachmentReadBit |
            VkAccessFlags.ColorAttachmentWriteBit;
        dependency.DependencyFlags = VkDependencyFlags.None;

        VkAttachmentDescription* attachments =
            stackalloc VkAttachmentDescription[1];
        attachments[0] = colorAttachment;

        VkRenderPassCreateInfo createInfo = new VkRenderPassCreateInfo();
        createInfo.AttachmentCount = 1;
        createInfo.Attachments = attachments;
        createInfo.SubpassCount = 1;
        createInfo.Subpasses = &subpass;
        createInfo.DependencyCount = 1;
        createInfo.Dependencies = &dependency;

        VkRenderPass renderPass = renderer.Device.CreateRenderPass(in createInfo, (VkAllocator)null);

        return new RenderPassState
        {
            Pass = renderPass,
            SampleCount = VkSampleCountFlags._1Bit,
        };
    }
    #endregion

    public unsafe FinalPostRenderer(
      Renderer renderer,
      Framebuffer.FramebufferAttachment source,
      RenderPassState finalRenderPass,
      VkExtent2D extent,
      ShaderReference vert,
      ShaderEx frag,
      bool uniqueRenderpass = false,
      int subPass = 0)
      : base(nameof(FinalPostRenderer), renderer, finalRenderPass, [vert, frag])
    {
        this._subpassIndex = subPass;

        this.Source = source;
        this.finalRenderPass = finalRenderPass.Pass;
        this.extent = extent;

        var device = renderer.Device;

        // Descriptor pool / set for the fragment shader's input sampler (binding = 0)
        VkDescriptorType descriptorType = uniqueRenderpass ? VkDescriptorType.CombinedImageSampler : VkDescriptorType.InputAttachment;
        //descriptorType = VkDescriptorType.CombinedImageSampler;

        DescriptorPool = frag.CreateDescriptorPool(device, descriptorType);

        bindingLayout = frag.CreateDescriptorSetLayout(
            device,
            new VkDescriptorSetLayoutBinding
            {
                Binding = 0,
                DescriptorType = descriptorType,
                DescriptorCount = 1,
                StageFlags = VkShaderStageFlags.FragmentBit,
            });
        bindingSet = device.AllocateDescriptorSet(DescriptorPool, bindingLayout);

        VkDescriptorImageInfo* inputInfo = stackalloc VkDescriptorImageInfo[1];
        if (uniqueRenderpass)
            inputInfo[0] = new VkDescriptorImageInfo
            {
                ImageView = source.ImageView,
                ImageLayout = VkImageLayout.ShaderReadOnlyOptimal,
                Sampler = Program.LinearClampedSampler,
            };
        else
            inputInfo[0] = new VkDescriptorImageInfo
            {
                // For input attachments: sampler must be null. set ImageLayout to InputAttachmentOptimal to make intent explicit.
                ImageView = source.ImageView,
                ImageLayout = VkImageLayout.AttachmentOptimal,
                Sampler = default,
            };


        frag.UpdateDescriptorSets(device, new VkWriteDescriptorSet
        {
            DstBinding = 0,
            DescriptorType = descriptorType,
            DescriptorCount = 1,
            DstSet = bindingSet,
            ImageInfo = inputInfo,
        });

        // Pipeline layout includes global bindings (set 0) and our sampler binding (set 1)
        PipelineLayout = device.CreatePipelineLayout(
          [GlobalShaderBindings.DescriptorSetLayout, bindingLayout], [], null);

        RebuildFrameResources();
    }

    protected override VertexInput MakeVertexInput() => null;

    protected override void OnRebuildFrameResources() => CreatePipeline(
      finalRenderPass,
      VkPrimitiveTopology.TriangleStrip,
      VkCullModeFlags.BackBit,
      VkFrontFace.CounterClockwise,
      VkPolygonMode.Fill,
      RenderingPresets.ReverseZDepthStencil.NoDepthTest,
      Presets.BlendState.BlendNone,
      out Pipeline
    );


    public unsafe void RenderSinglePass(
        CommandBuffer commandBuffer,
        RenderPassState singleRenderPass,
        VkFramebuffer blurFrameBuffer,
        int dynamicOffset = 0
    )
    {
        ReadOnlySpan<KSA.Rendering.ImageTransition> transitions = stackalloc KSA.Rendering.ImageTransition[]
        {
            new KSA.Rendering.ImageTransition(
                inImage: Source.Image,
                inSrc: KSA.Rendering.ImageBarrierInfo.Presets.ColorAttachment,
                inDst: KSA.Rendering.ImageBarrierInfo.Presets.ShaderReadOnlyFragment
            )
        };
        commandBuffer.TransitionImages2(transitions);

        var rect = new VkRect2D(extent);
        commandBuffer.BeginRenderPass(new VkRenderPassBeginInfo
        {
            RenderPass = singleRenderPass.Pass,
            Framebuffer = blurFrameBuffer,
            RenderArea = rect,
        }, VkSubpassContents.Inline);

        commandBuffer.BindPipeline(VkPipelineBindPoint.Graphics, Pipeline);

        commandBuffer.SetViewport(0, [new VkViewport
            {
                Width = extent.Width,
                Height = extent.Height,
                MinDepth = 0f,
                MaxDepth = 1f,
            }]);
        commandBuffer.SetScissor(0, [rect]);

        // bind global set (set 0) and our set (set 1)
        commandBuffer.BindDescriptorSets(
          VkPipelineBindPoint.Graphics, PipelineLayout, 0,
          [GlobalShaderBindings.DescriptorSet, bindingSet],
          [GlobalShaderBindings.DynamicOffset(dynamicOffset)]);

        commandBuffer.Draw(4, 1, 0, 0);

        commandBuffer.EndRenderPass();
    }


    public unsafe void RenderSubpass(
        CommandBuffer commandBuffer,
        int dynamicOffset = 0)
    {
        commandBuffer.BindPipeline(
            VkPipelineBindPoint.Graphics,
            Pipeline);

        commandBuffer.SetViewport(0, [new VkViewport
        {
            Width = extent.Width,
            Height = extent.Height,
            MinDepth = 0f,
            MaxDepth = 1f,
        }]);
        var rect = new VkRect2D(extent);
        commandBuffer.SetScissor(0, [rect]);

        commandBuffer.BindDescriptorSets(
            VkPipelineBindPoint.Graphics,
            PipelineLayout,
            0,
            [GlobalShaderBindings.DescriptorSet, bindingSet],
            [GlobalShaderBindings.DynamicOffset(dynamicOffset)]);

        commandBuffer.Draw(4, 1, 0, 0);
    }
}