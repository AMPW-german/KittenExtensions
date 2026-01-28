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
                    renderPass = FinalPostRenderer.CreateMultiRenderPass(Program.GetRenderer(), subPassCount); // two subpasses for testing
                return renderPass;
            }
        }

        private static RenderPassState renderPass2;
        public static RenderPassState RenderPass2
        {
            get
            {
                if (renderPass2 == null)
                    renderPass2 = FinalPostRenderer.CreateMultiRenderPass(Program.GetRenderer(), 1); // two subpasses for testing
                return renderPass2;
            }
        }

        private static RenderPassState singleRenderPass;
        public static RenderPassState SingleRenderPass
        {
            get
            {
                if (singleRenderPass == null)
                    singleRenderPass = FinalPostRenderer.CreateSingleRenderPass(Program.GetRenderer()); // two subpasses for testing
                return singleRenderPass;
            }
        }

        private static int subPassCount = 2;
        private static FramebufferAttachment[] framebufferAttachments;
        private static FramebufferAttachment[] framebufferAttachments2;
        private static FramebufferAttachment? blurAttachment;
        private static FramebufferAttachment? tintAttachment;
        private static VkFramebuffer postProcessFramebuffer;
        private static VkFramebuffer blurFrameBuffer;
        private static VkFramebuffer TintFrameBuffer;
        private static VkFramebuffer postProcessFramebuffer2;

        public static FinalPostRenderer Instance { get; private set; }
        public static FinalPostRenderer Instance2 { get; private set; }
        public static FinalPostRenderer Instance3 { get; private set; }
        public static FinalPostRenderer Instance4 { get; private set; }
        public static FinalPostRenderer Instance5 { get; private set; }

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
                            VkImageUsageFlags.TransferSrcBit |
                            VkImageUsageFlags.SampledBit
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

            if (blurAttachment == null)
            {
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
                            VkImageUsageFlags.TransferSrcBit |
                            VkImageUsageFlags.SampledBit
                };

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
                blurAttachment = attachment;
            }

            if (tintAttachment == null)
            {
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
                            VkImageUsageFlags.TransferSrcBit |
                            VkImageUsageFlags.SampledBit
                };

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
                tintAttachment = attachment;
            }

            if (framebufferAttachments2 == null)
            {
                framebufferAttachments2 = new FramebufferAttachment[2];
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
                            VkImageUsageFlags.TransferSrcBit |
                            VkImageUsageFlags.SampledBit
                };

                framebufferAttachments2[0] = tintAttachment!.Value;

                for (int i = 1; i <= 1; i++)
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
                    framebufferAttachments2[i] = attachment;
                }
            }

            if (postProcessFramebuffer.IsNull())
            {
                VkImageView* views = stackalloc VkImageView[subPassCount + 1];
                for (int i = 0; i <= subPassCount; i++) views[i] = framebufferAttachments[i].ImageView;

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

            if (blurFrameBuffer.IsNull())
            {
                VkImageView* blurViews = stackalloc VkImageView[1];
                blurViews[0] = blurAttachment!.Value.ImageView;
                VkFramebufferCreateInfo fbInfo = new VkFramebufferCreateInfo
                {
                    RenderPass = SingleRenderPass.Pass,
                    AttachmentCount = 1,
                    Attachments = blurViews,
                    Width = renderer.Extent.Width,
                    Height = renderer.Extent.Height,
                    Layers = 1
                };
                blurFrameBuffer = renderer.Device.CreateFramebuffer(fbInfo, null);
            }

            if (TintFrameBuffer.IsNull())
            {
                VkImageView* blurViews = stackalloc VkImageView[1];
                blurViews[0] = tintAttachment!.Value.ImageView;
                VkFramebufferCreateInfo fbInfo = new VkFramebufferCreateInfo
                {
                    RenderPass = SingleRenderPass.Pass,
                    AttachmentCount = 1,
                    Attachments = blurViews,
                    Width = renderer.Extent.Width,
                    Height = renderer.Extent.Height,
                    Layers = 1
                };
                TintFrameBuffer = renderer.Device.CreateFramebuffer(fbInfo, null);
            }

            if (postProcessFramebuffer2.IsNull())
            {
                VkImageView* views = stackalloc VkImageView[2];
                for (int i = 0; i <= 1; i++) views[i] = framebufferAttachments2[i].ImageView;

                VkFramebufferCreateInfo fbInfo = new VkFramebufferCreateInfo
                {
                    RenderPass = RenderPass2.Pass,
                    AttachmentCount = 2,
                    Attachments = views,
                    Width = renderer.Extent.Width,
                    Height = renderer.Extent.Height,
                    Layers = 1
                };

                postProcessFramebuffer2 = renderer.Device.CreateFramebuffer(fbInfo, null);
            }


            try
            {
                var vert = ModLibrary.Get<ShaderReference>("ScreenspaceVert");
                var frag = ModLibrary.Get<ShaderEx>("GEffectFrag");
                var frag2 = ModLibrary.Get<ShaderEx>("GEffectFrag2");
                var frag3 = ModLibrary.Get<ShaderEx>("BlurFrag");
                var frag4 = ModLibrary.Get<ShaderEx>("TintFrag");
                var frag5 = ModLibrary.Get<ShaderEx>("GreyScaleFrag");

                Instance?.Dispose();
                Instance2?.Dispose();
                Instance3?.Dispose();
                Instance4?.Dispose();
                Instance5?.Dispose();

                // keep uniqueRenderpass = false so we use input attachments (attachment 0 is the offscreen target)
                Instance = new FinalPostRenderer(renderer, framebufferAttachments[0], RenderPass, renderer.Extent, vert, frag, subPass: 0);
                Instance2 = new FinalPostRenderer(renderer, framebufferAttachments[1], RenderPass, renderer.Extent, vert, frag2, subPass: 1);
                Instance3 = new FinalPostRenderer(renderer, framebufferAttachments[2], SingleRenderPass, renderer.Extent, vert, frag3, uniqueRenderpass: true);
                Instance4 = new FinalPostRenderer(renderer, blurAttachment!.Value, SingleRenderPass, renderer.Extent, vert, frag4, uniqueRenderpass: true);
                Instance5 = new FinalPostRenderer(renderer, tintAttachment!.Value, RenderPass2, renderer.Extent, vert, frag5);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create FinalPostRenderer: {ex.Message}", "FinalPostIntegration.Rebuild", "KittenExtensions/FinalPostIntegration.cs", 1);
                Instance = null;
            }
        }



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
            VkExtent2D extent = renderer.Extent;


            commandBuffer.PipelineBarrier(
                srcStageMask: VkPipelineStageFlags.ColorAttachmentOutputBit,
                dstStageMask: VkPipelineStageFlags.ColorAttachmentOutputBit,
                dependencyFlags: VkDependencyFlags.None,
                pMemoryBarriers: ReadOnlySpan<VkMemoryBarrier>.Empty,
                pBufferMemoryBarriers: ReadOnlySpan<VkBufferMemoryBarrier>.Empty,
                pImageMemoryBarriers: new VkImageMemoryBarrier[]
                {
                    new VkImageMemoryBarrier
                    {
                        SrcAccessMask = VkAccessFlags.ColorAttachmentWriteBit,
                        DstAccessMask = VkAccessFlags.ShaderReadBit,
                        OldLayout = VkImageLayout.ColorAttachmentOptimal,
                        NewLayout = VkImageLayout.ShaderReadOnlyOptimal,
                        SrcQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED,
                        DstQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED,
                        Image = Patches.offscreenTarget2.ColorImage.Image,
                        SubresourceRange = Patches.offscreenTarget2.ColorImage.SubresourceRange
                    }
                }
            );


            // Begin render pass using the temporary framebuffer that contains both attachments.
            commandBuffer.BeginRenderPass(new VkRenderPassBeginInfo
            {
                RenderPass = RenderPass.Pass,
                Framebuffer = postProcessFramebuffer,
                RenderArea = new VkRect2D(extent),
            }, VkSubpassContents.Inline);

            List<FinalPostRenderer> subpasses = [Instance, Instance2];

            for (int i = 0; i < subpasses.Count; i++)
            {
                subpasses[i].RenderSubpass(commandBuffer, dynamicOffset);

                if (i < subpasses.Count - 1)
                    commandBuffer.NextSubpass(VkSubpassContents.Inline);
            }

            commandBuffer.EndRenderPass();

            commandBuffer.PipelineBarrier(
                srcStageMask: VkPipelineStageFlags.ColorAttachmentOutputBit,
                dstStageMask: VkPipelineStageFlags.ColorAttachmentOutputBit,
                dependencyFlags: VkDependencyFlags.None,
                pMemoryBarriers: ReadOnlySpan<VkMemoryBarrier>.Empty,
                pBufferMemoryBarriers: ReadOnlySpan<VkBufferMemoryBarrier>.Empty,
                pImageMemoryBarriers: new VkImageMemoryBarrier[]
                {
                    new VkImageMemoryBarrier
                    {
                        SrcAccessMask = VkAccessFlags.ColorAttachmentWriteBit,
                        DstAccessMask = VkAccessFlags.ShaderReadBit,
                        OldLayout = VkImageLayout.ColorAttachmentOptimal,
                        NewLayout = VkImageLayout.ShaderReadOnlyOptimal,
                        SrcQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED,
                        DstQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED,
                        Image = framebufferAttachments[subPassCount].Image,
                        SubresourceRange = framebufferAttachments[subPassCount].SubresourceRange
                    }
                }
            );

            Instance3.RenderSinglePass(commandBuffer, SingleRenderPass, blurFrameBuffer, dynamicOffset);

            commandBuffer.PipelineBarrier(
                srcStageMask: VkPipelineStageFlags.ColorAttachmentOutputBit,
                dstStageMask: VkPipelineStageFlags.ColorAttachmentOutputBit,
                dependencyFlags: VkDependencyFlags.None,
                pMemoryBarriers: ReadOnlySpan<VkMemoryBarrier>.Empty,
                pBufferMemoryBarriers: ReadOnlySpan<VkBufferMemoryBarrier>.Empty,
                pImageMemoryBarriers: new VkImageMemoryBarrier[]
                {
                    new VkImageMemoryBarrier
                    {
                        SrcAccessMask = VkAccessFlags.ColorAttachmentWriteBit,
                        DstAccessMask = VkAccessFlags.ShaderReadBit,
                        OldLayout = VkImageLayout.ColorAttachmentOptimal,
                        NewLayout = VkImageLayout.TransferSrcOptimal,
                        SrcQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED,
                        DstQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED,
                        Image = blurAttachment!.Value.Image,
                        SubresourceRange = blurAttachment!.Value.SubresourceRange
                    }
                }
            );

            Instance4.RenderSinglePass(commandBuffer, SingleRenderPass, TintFrameBuffer, dynamicOffset);

            commandBuffer.PipelineBarrier(
                srcStageMask: VkPipelineStageFlags.ColorAttachmentOutputBit,
                dstStageMask: VkPipelineStageFlags.ColorAttachmentOutputBit,
                dependencyFlags: VkDependencyFlags.None,
                pMemoryBarriers: ReadOnlySpan<VkMemoryBarrier>.Empty,
                pBufferMemoryBarriers: ReadOnlySpan<VkBufferMemoryBarrier>.Empty,
                pImageMemoryBarriers: new VkImageMemoryBarrier[]
                {
                    new VkImageMemoryBarrier
                    {
                        SrcAccessMask = VkAccessFlags.ColorAttachmentWriteBit,
                        DstAccessMask = VkAccessFlags.ShaderReadBit,
                        OldLayout = VkImageLayout.ColorAttachmentOptimal,
                        NewLayout = VkImageLayout.TransferSrcOptimal,
                        SrcQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED,
                        DstQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED,
                        Image = tintAttachment!.Value.Image,
                        SubresourceRange = tintAttachment!.Value.SubresourceRange
                    }
                }
            );

            commandBuffer.BeginRenderPass(new VkRenderPassBeginInfo
            {
                RenderPass = RenderPass2.Pass,
                Framebuffer = postProcessFramebuffer2,
                RenderArea = new VkRect2D(extent),
            }, VkSubpassContents.Inline);

            List<FinalPostRenderer> subpasses2 = [Instance5];

            for (int i = 0; i < subpasses2.Count; i++)
            {
                subpasses2[i].RenderSubpass(commandBuffer, dynamicOffset);

                if (i < subpasses2.Count - 1)
                    commandBuffer.NextSubpass(VkSubpassContents.Inline);
            }

            commandBuffer.EndRenderPass();

            commandBuffer.PipelineBarrier(
                srcStageMask: VkPipelineStageFlags.ColorAttachmentOutputBit,
                dstStageMask: VkPipelineStageFlags.ColorAttachmentOutputBit,
                dependencyFlags: VkDependencyFlags.None,
                pMemoryBarriers: ReadOnlySpan<VkMemoryBarrier>.Empty,
                pBufferMemoryBarriers: ReadOnlySpan<VkBufferMemoryBarrier>.Empty,
                pImageMemoryBarriers: new VkImageMemoryBarrier[]
                {
                    new VkImageMemoryBarrier
                    {
                        SrcAccessMask = VkAccessFlags.ColorAttachmentWriteBit,
                        DstAccessMask = VkAccessFlags.ShaderReadBit,
                        OldLayout = VkImageLayout.ColorAttachmentOptimal,
                        NewLayout = VkImageLayout.ShaderReadOnlyOptimal,
                        SrcQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED,
                        DstQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED,
                        Image = framebufferAttachments2[1].Image,
                        SubresourceRange = framebufferAttachments2[1].SubresourceRange
                    }
                }
            );

            // Copy the result to the destination framebuffer's color attachment
            //FramebufferAttachment lastAttachment = tintAttachment.Value;
            FramebufferAttachment lastAttachment = framebufferAttachments2.Last();
            //FramebufferAttachment lastAttachment = Patches.offscreenTarget2.ColorImage;

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