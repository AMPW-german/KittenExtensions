using Brutal.VulkanApi;
using Core;
using KSA;
using System;

namespace KittenExtensions
{
    // Small helper to own the FinalPostRenderer instance and expose a simple RenderNow helper.
    internal static class FinalPostIntegration
    {
        public static FinalPostRenderer Instance { get; private set; }

        // Call from Program.RebuildRenderer postfix so the instance is recreated when swapchain / renderpass / extent change.
        public static void Rebuild(Renderer renderer)
        {
            try
            {
                // Replace "FinalPostFrag" with the fragment shader asset name you want to use.
                var vert = ModLibrary.Get<ShaderReference>("ScreenspaceVert");
                var frag = ModLibrary.Get<ShaderEx>("GEffectFrag2");

                OffscreenTarget t = Program.OffscreenTarget;

                // Dispose old instance if you want (RenderTechnique may provide disposal in your codebase).
                //Instance = new FinalPostRenderer(renderer, FinalPostRenderer.createRenderPass(renderer), renderer.Extent, vert, frag);
                Instance = new FinalPostRenderer(renderer, renderer.MainRenderPass, renderer.Extent, vert, frag);

                // Use the game's offscreen target as the source (this is the typical final color image)
                //if (Program.OffscreenTarget != null)
                //Instance.UpdateSource(Program.OffscreenTarget.ColorImage);
                Instance.UpdateSource(Patches.offscreenTarget2.ColorImage);
                //Instance.UpdateSource(Program.);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create FinalPostRenderer: {ex.Message}", "FinalPostIntegration.Rebuild", "KittenExtensions/FinalPostIntegration.cs", 1);
                Instance = null;
            }
        }


        static bool reset = false;
        // Call from the point in Program.RenderGame where you still have the commandBuffer and framebuffer
        public static void RenderNow(CommandBuffer commandBuffer, VkFramebuffer destFramebuffer, int dynamicOffset = 0)
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


            // Ensure source is up-to-date in case someone re-created targets
            //if (Program.OffscreenTarget != null)
            //    Instance.UpdateSource(Program.OffscreenTarget.ColorImage);

            Instance.RenderShader(commandBuffer, destFramebuffer, dynamicOffset);
        }
    }
}