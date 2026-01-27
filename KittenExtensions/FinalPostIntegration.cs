using Brutal;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;
using System;
using System.Collections.Generic;
using System.Linq;
using static KSA.Framebuffer;

namespace KittenExtensions
{
    // Small helper to own the FinalPostRenderer instance and expose a simple RenderNow helper.
    internal static class FinalPostIntegration
    {
        private static RenderPassState renderPass;
        public static RenderPassState RenderPass
        {
            get
            {
                if (renderPass == null)
                    renderPass = FinalPostRenderer.createRenderPass(Program.GetRenderer(), subPassCount); // two subpasses for testing
                return renderPass;
            }
        }

        private static int subPassCount = 2;

        public static FinalPostRenderer Instance { get; private set; }
        public static FinalPostRenderer Instance2 { get; private set; }
        public static FinalPostRenderer Instance3 { get; private set; }

        // Call from Program.RebuildRenderer postfix so the instance is recreated when swapchain / renderpass / extent change.
        public static unsafe void Rebuild(Renderer renderer)
        {
            if (framebufferAttachments == null)
            {
                framebufferAttachments = new FramebufferAttachment[subPassCount + 1];
                VkImageSubresourceRange subresourceRange1 = new VkImageSubresourceRange();
                subresourceRange1.AspectMask = VkImageAspectFlags.ColorBit;
                subresourceRange1.LevelCount = 1;
                subresourceRange1.LayerCount = 1;
                subresourceRange1.BaseMipLevel = 0;
                subresourceRange1.BaseArrayLayer = 0;
                VkImageSubresourceRange subresourceRange2 = subresourceRange1;

                VkImageCreateInfo imageCreateInfo = new VkImageCreateInfo
                {
                    ImageType = VkImageType._2D,
                    Flags = VkImageCreateFlags.None,
                    Format = renderer.ColorFormat,
                    Extent = new VkExtent3D
                    {
                        Width = renderer.Extent.Width,
                        Height = renderer.Extent.Height,
                        Depth = 1
                    },
                    MipLevels = 1,
                    ArrayLayers = 1,
                    Samples = VkSampleCountFlags._1Bit,
                    Usage = VkImageUsageFlags.ColorAttachmentBit |
                            VkImageUsageFlags.InputAttachmentBit |
                            VkImageUsageFlags.TransferSrcBit
                };

                framebufferAttachments[0] = Patches.offscreenTarget2.ColorImage;

                for (int i = 1; i <= subPassCount; i++)
                {
                    FramebufferAttachment attachment = new FramebufferAttachment()
                    {
                        Image = renderer.Device.CreateImage(imageCreateInfo, null),
                        Format = renderer.ColorFormat,
                    };

                    attachment.Memory = renderer.Device.AllocateMemory(renderer.PhysicalDevice, attachment.Image, VkMemoryPropertyFlags.DeviceLocalBit, null);
                    renderer.Device.BindImageMemory(attachment.Image, attachment.Memory, (ByteSize64)ByteSize.Zero);
                    attachment.SubresourceRange = new VkImageSubresourceRange()
                    {
                        AspectMask = VkImageAspectFlags.ColorBit,
                        BaseMipLevel = 0,
                        LevelCount = 1,
                        BaseArrayLayer = 0,
                        LayerCount = 1
                    };

                    VkImageViewCreateInfo imageViewCreateInfo = new VkImageViewCreateInfo
                    {
                        Image = attachment.Image,
                        ViewType = VkImageViewType._2D,
                        Format = renderer.ColorFormat,
                        SubresourceRange = subresourceRange2
                    };


                    attachment.ImageView = renderer.Device.CreateImageView(imageViewCreateInfo, null);
                    framebufferAttachments[i] = attachment;
                }
            }

            VkImageView* views = stackalloc VkImageView[subPassCount + 1];
            for (int i = 0; i <= subPassCount; i++) views[i] = framebufferAttachments[i].ImageView;


            if (postProcessFramebuffer.IsNull())
            {
                VkFramebufferCreateInfo fbInfo = new VkFramebufferCreateInfo
                {
                    RenderPass = RenderPass.Pass,
                    AttachmentCount = subPassCount + 1,
                    Attachments = views,
                    Width = renderer.Extent.Width,
                    Height = renderer.Extent.Height,
                    Layers = 1
                };

                postProcessFramebuffer = renderer.Device.CreateFramebuffer(fbInfo, null);
            }

            try
            {
                var vert = ModLibrary.Get<ShaderReference>("ScreenspaceVert");
                var frag = ModLibrary.Get<ShaderEx>("GEffectFrag");
                var frag2 = ModLibrary.Get<ShaderEx>("GEffectFrag2");
                //var frag3 = ModLibrary.Get<ShaderEx>("GEffectFrag2");

                Instance?.Dispose();
                Instance2?.Dispose();

                // keep uniqueRenderpass = false so we use input attachments (attachment 0 is the offscreen target)
                Instance = new FinalPostRenderer(renderer, framebufferAttachments[0], RenderPass, renderer.Extent, vert, frag, subPass: 0);
                Instance2 = new FinalPostRenderer(renderer, framebufferAttachments[1], RenderPass, renderer.Extent, vert, frag2, subPass: 1);
                //Instance3 = new FinalPostRenderer(renderer, framebufferAttachments[2], RenderPass, renderer.Extent, vert, frag3, subPass: 2);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create FinalPostRenderer: {ex.Message}", "FinalPostIntegration.Rebuild", "KittenExtensions/FinalPostIntegration.cs", 1);
                Instance = null;
            }
        }

        private static FramebufferAttachment[] framebufferAttachments;
        private static VkFramebuffer postProcessFramebuffer;

        private static bool reset = false;
        // Call from the point in Program.RenderGame where you still have the commandBuffer and framebuffer
        public static unsafe void RenderNow(CommandBuffer commandBuffer, FrameResources destFrameResources, int dynamicOffset = 0)
        {
            if (!reset)
            {
                Program.ScheduleRendererRebuild();
                reset = true;
            }

            if (Instance == null)
            {
                return;
            }

            Renderer renderer = Program.GetRenderer();


            // Begin render pass using the temporary framebuffer that contains both attachments.
            commandBuffer.BeginRenderPass(new VkRenderPassBeginInfo
            {
                RenderPass = RenderPass.Pass,
                Framebuffer = postProcessFramebuffer,
                RenderArea = new VkRect2D(renderer.Extent),
            }, VkSubpassContents.Inline);

            List<FinalPostRenderer> subpasses = [Instance, Instance2];

            for (int i = 0; i < subpasses.Count; i++)
            {
                subpasses[i].RenderSubpass(commandBuffer, dynamicOffset);

                if (i < subpasses.Count - 1)
                    commandBuffer.NextSubpass(VkSubpassContents.Inline);
            }

            commandBuffer.EndRenderPass();


            // Copy the result to the destination framebuffer's color attachment
            FramebufferAttachment lastAttachment = framebufferAttachments[subPassCount];

            commandBuffer.CopyImage(
                srcImage: lastAttachment.Image,
                srcImageLayout: VkImageLayout.ColorAttachmentOptimal,
                dstImage: destFrameResources.ColorImage,
                dstImageLayout: VkImageLayout.ColorAttachmentOptimal,
                pRegions: new ReadOnlySpan<VkImageCopy>(new VkImageCopy[]
                {
                    new VkImageCopy
                    {
                        SrcSubresource = new VkImageSubresourceLayers
                        {
                            AspectMask = VkImageAspectFlags.ColorBit,
                            MipLevel = 0,
                            BaseArrayLayer = 0,
                            LayerCount = 1
                        },
                        SrcOffset = new VkOffset3D(0, 0, 0),
                        DstSubresource = new VkImageSubresourceLayers
                        {
                            AspectMask = VkImageAspectFlags.ColorBit,
                            MipLevel = 0,
                            BaseArrayLayer = 0,
                            LayerCount = 1
                        },
                        DstOffset = new VkOffset3D(0, 0, 0),
                        Extent = new VkExtent3D
                        {
                            Width = renderer.Extent.Width,
                            Height = renderer.Extent.Height,
                            Depth = 1
                        }
                    }
                })
            );
        }
    }
}