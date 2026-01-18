using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;
using RenderCore;
using System;
using System.Reflection;

namespace KittenExtensions;

public class FinalPostRenderer : RenderTechnique
{
    private readonly VkRenderPass finalRenderPass;
    private VkExtent2D extent;

    private readonly DescriptorSetLayoutEx bindingLayout;
    private readonly VkDescriptorSet bindingSet;

    // The image that will be sampled by the post shader (set by caller)
    // Typically this will be the final color attachment from the main render.
    public Framebuffer.FramebufferAttachment Source { get; private set; }


    internal static unsafe VkRenderPass createRenderPass(Renderer renderer)
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

        return renderer.Device.CreateRenderPass(in createInfo, (VkAllocator)null);
    }


    public unsafe FinalPostRenderer(
      Renderer renderer,
      VkRenderPass finalRenderPass,
      VkExtent2D extent,
      ShaderReference vert,
      ShaderEx frag)
      : base(nameof(FinalPostRenderer), renderer, Program.MainPass, [vert, frag])
    {
        this.finalRenderPass = finalRenderPass;
        this.extent = extent;

        var device = renderer.Device;

        // Descriptor pool / set for the fragment shader's input sampler (binding = 0)
        DescriptorPool = frag.CreateDescriptorPool(device, VkDescriptorType.CombinedImageSampler);
        bindingLayout = frag.CreateDescriptorSetLayout(device, new VkDescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = VkDescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags = VkShaderStageFlags.FragmentBit,
        });
        bindingSet = device.AllocateDescriptorSet(DescriptorPool, bindingLayout);

        // Pipeline layout includes global bindings (set 0) and our sampler binding (set 1)
        PipelineLayout = device.CreatePipelineLayout(
          [GlobalShaderBindings.DescriptorSetLayout, bindingLayout],
          [], null);

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

    //// Call this when the final render pass or swapchain extent changes (e.g. on resize).
    //public unsafe void Rebuild(VkRenderPass newFinalRenderPass, VkExtent2D newExtent)
    //{
    //  Renderer.Device.WaitIdle();

    //  if (newFinalRenderPass.VkHandle != finalRenderPass.VkHandle)
    //    throw new InvalidOperationException("FinalPostRenderer was created for a different render pass. Create a new instance for a different final render pass.");

    //  extent = newExtent;
    //  RebuildFrameResources();
    //}

    // Update which image will be sampled by the post shader.
    // Caller should pass the final color attachment they want post-processed.
    public unsafe void UpdateSource(Framebuffer.FramebufferAttachment source)
    {
        Source = source;

        VkDescriptorImageInfo* inputInfo = stackalloc VkDescriptorImageInfo[1];
        inputInfo[0] = new VkDescriptorImageInfo
        {
            ImageLayout = VkImageLayout.ShaderReadOnlyOptimal,
            ImageView = source.ImageView,
            Sampler = Program.LinearClampedSampler,
        };

        Renderer.Device.UpdateDescriptorSets([
          new VkWriteDescriptorSet
      {
        DescriptorType = VkDescriptorType.CombinedImageSampler,
        DstBinding = 0,
        DescriptorCount = 1,
        DstSet = bindingSet,
        ImageInfo = inputInfo,
      }
        ], []);
    }

    // Render the fullscreen post-process. The provided framebuffer must be compatible
    // with the VkRenderPass that this renderer was constructed with (typically the swapchain framebuffer).
    // dynamicOffset is forwarded to the global descriptor set (viewport/view uniform); if unused pass 0.
    public unsafe void RenderShader(CommandBuffer commandBuffer, VkFramebuffer destFramebuffer, int dynamicOffset = 0)
    {
        //if (Source.Handle == 0)
        //  throw new InvalidOperationException("Source image not set. Call UpdateSource(...) before Render().");

        // Make sure source is readable by the shader
        commandBuffer.TransitionImage(
          Source, VkImageLayout.ColorAttachmentOptimal, VkImageLayout.ShaderReadOnlyOptimal);

        var rect = new VkRect2D(extent);
        commandBuffer.BeginRenderPass(new VkRenderPassBeginInfo
        {
            RenderPass = finalRenderPass,
            Framebuffer = destFramebuffer,
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
}