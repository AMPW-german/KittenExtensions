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

    // The image that will be sampled by the post shader (set by caller)
    // Typically this will be the final color attachment from the main render.
    //public Framebuffer.FramebufferAttachment Source { get; private set; }


    internal static unsafe RenderPassState createRenderPass(Renderer renderer, int subpassCount)
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