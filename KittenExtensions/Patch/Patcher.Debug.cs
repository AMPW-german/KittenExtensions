
using System;
using System.Collections.Generic;
using System.Xml;
using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;
using RenderCore;

namespace KittenExtensions.Patch;

public static partial class XmlPatcher
{
  private class PatchDebugPopup : Popup
  {
    private readonly string title;
    private readonly char[] buffer = new char[65536];

    private readonly IEnumerator<XmlElement> patches;
    private readonly HashSet<XmlNode> patchPath = [];
    private bool newPatch = true;

    public PatchDebugPopup()
    {
      title = "PatchDebug####" + PopupId;
      patches = GetPatches().GetEnumerator();
      NextPatch();
    }

    private void NextPatch()
    {
      patches.MoveNext();
      patchPath.Clear();
      var cur = CurPatch;
      while (cur != null)
      {
        patchPath.Add(cur);
        cur = cur.ParentNode as XmlElement;
      }
    }

    private ImDrawListPtr parentDl;
    protected override void OnDrawUi()
    {
      var padding = new float2(50, 50);
      var size = float2.Unpack(Program.GetWindow().Size) - padding * 2;

      var patched = false;

      ImGui.SetNextWindowSize(size, ImGuiCond.Always);
      ImGui.SetNextWindowPos(ImGui.GetMainViewport().Pos + padding, ImGuiCond.Always);
      ImGui.OpenPopup(title);
      ImGui.BeginPopup(
        title, ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.Popup);

      ImGui.Text("KittenExtensions Patch Debug");
      ImGui.Separator();

      parentDl = ImGui.GetWindowDrawList();

      if (CurPatch != null)
      {
        if (ImGui.Button("Run Patch"))
        {
          RunPatch(CurPatch);
          NextPatch();
          patched = true;
        }
      }
      else
      {
        if (ImGui.Button("Start Game"))
          Active = false;
      }

      var spacing = ImGui.GetStyle().ItemSpacing;

      var avail = ImGui.GetContentRegionAvail();
      var childSz = new float2((avail.X - spacing.X) / 2, avail.Y);

      var childCursor = ImGui.GetCursorScreenPos();

      ImGui.SetNextWindowPos(childCursor);
      ImGui.BeginChild("XmlTree", childSz, ImGuiChildFlags.Borders);
      DrawNode(RootNode, 0, true, false);
      ImGui.EndChild();

      ImGui.SetNextWindowPos(childCursor + new float2(childSz.X + spacing.X, 0));
      ImGui.BeginChild("Info", childSz, ImGuiChildFlags.Borders);
      ImGui.Text("TEST");
      ImGui.EndChild();

      parentDl = null;

      ImGui.EndPopup();
      newPatch = patched;
    }

    private const ImGuiTreeNodeFlags TREE_FLAGS = ImGuiTreeNodeFlags.DrawLinesFull;
    private const ImGuiTreeNodeFlags LEAF_FLAGS =
      TREE_FLAGS | ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;

    private void DrawNode(XmlNode node, int depth, bool onPath, bool inPatch)
    {
      var xml = new XmlDisplayBuilder(buffer);
      if (node is XmlElement el)
      {
        onPath = onPath && patchPath.Contains(el);
        inPatch = inPatch || (onPath && el == CurPatch);
        var children = el.ChildNodes;

        var start = ImGui.GetCursorScreenPos();

        if (ShouldInline(el))
        {
          xml.ElementInline(el);
          ImGui.TreeNodeEx(xml.Line, LEAF_FLAGS);
        }
        else
        {
          xml.ElementOpen(el, !el.HasChildNodes);

          if ((onPath || inPatch) && newPatch)
            ImGui.SetNextItemOpen(true, ImGuiCond.Always);
          if (ImGui.TreeNodeEx(xml.Line, TREE_FLAGS))
          {
            for (var i = 0; i < children.Count; i++)
            {
              ImGui.PushID(i);
              DrawNode(children[i], depth + 1, onPath, inPatch);
              ImGui.PopID();
            }
            ImGui.TreePop();

            Indent();
            xml.Reset();
            xml.ElementClose(el.Name);
            ImGui.Text(xml.Line);
            Unindent();
          }
        }

        if (el == CurPatch)
        {
          var childDl = ImGui.GetWindowDrawList();
          parentDl.PushClipRect(childDl.GetClipRectMin(), childDl.GetClipRectMax());

          var endCursor = ImGui.GetCursorScreenPos();
          var end = new float2(endCursor.X + ImGui.GetContentRegionAvail().X, endCursor.Y);

          var cr = new ImColor8(0, 0, 64);
          parentDl.AddRectFilled(start, end, cr);

          parentDl.PopClipRect();
        }
      }
      else
      {
        ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
        ImGui.PushStyleColor(ImGuiCol.Tab, NodeColor(node));

        xml.NodeInline(node);
        ImGui.TreeNodeEx(xml.Line, LEAF_FLAGS);

        ImGui.PopStyleColor();
        ImGui.PopTextWrapPos();
      }
    }

    private static bool ShouldInline(XmlElement el)
    {
      var children = el.ChildNodes;
      if (!el.HasChildNodes)
        return true;
      if (children.Count > 1)
        return false;
      if (children[0] is not (XmlText or XmlComment))
        return false;
      return !children[0].Value.Contains('\n');
    }

    private static ImColor8 NodeColor(XmlNode node) => node switch
    {
      XmlComment => new(180, 255, 180),
      XmlProcessingInstruction => new(180, 180, 180),
      _ => ImColor8.White,
    };

    private void Indent() => ImGui.Indent(ImGui.GetTreeNodeToLabelSpacing());
    private void Unindent() => ImGui.Unindent(ImGui.GetTreeNodeToLabelSpacing());
  }

  // copied from SelectSystem
  private class PatchTask : SetupTaskBase
  {
    private readonly Renderer renderer = Program.GetRenderer();
    private readonly PatchDebugPopup popup = new();

    public bool Show => popup.Active;

    public void DrawUi()
    {
      if (!Show)
        return;
      ImGuiHelper.BlankBackground();
      Popup.DrawAll();
    }

    public unsafe void OnFrame()
    {
      if (!Program.IsMainThread())
        return;
      Glfw.PollEvents();
      if (Program.GetWindow().ShouldClose)
      {
        Environment.Exit(0);
      }
      else
      {
        ImGuiBackend.NewFrame();
        ImGui.NewFrame();
        ImGuiHelper.StartFrame();
        DrawUi();
        ImGui.Render();
        if (ImGui.GetIO().ConfigFlags.HasFlag(ImGuiConfigFlags.ViewportsEnable))
        {
          ImGui.UpdatePlatformWindows();
          ImGui.RenderPlatformWindowsDefault(IntPtr.Zero, IntPtr.Zero);
        }
        (FrameResult result, AcquiredFrame acquiredFrame1) = renderer.TryAcquireNextFrame();
        AcquiredFrame acquiredFrame2 = acquiredFrame1;
        if (result != FrameResult.Success)
          PartialRebuild();
        else
        {
          acquiredFrame1 = acquiredFrame2;
          (FrameResources resources, CommandBuffer commandBuffer) = acquiredFrame1;
          VkSubpassContents contents = VkSubpassContents.Inline;
          VkRenderPassBeginInfo pRenderPassBegin = new()
          {
            RenderPass = Program.MainPass.Pass,
            Framebuffer = resources.Framebuffer,
            RenderArea = new VkRect2D(renderer.Extent),
            ClearValues = (VkClearValue*)Program.MainPass.ClearValues.Ptr,
            ClearValueCount = 2
          };
          commandBuffer.Reset();
          commandBuffer.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);
          commandBuffer.BeginRenderPass(in pRenderPassBegin, contents);
          ImGuiBackend.Vulkan.RenderDrawData(commandBuffer);
          commandBuffer.EndRenderPass();
          commandBuffer.End();
          if (renderer.TrySubmitFrame() == 0)
            return;
          PartialRebuild();
        }
      }
    }

    public void PartialRebuild()
    {
      renderer.Rebuild(GameSettings.GetPresentMode());
      renderer.Device.WaitIdle();
      Program.MainPass.Pass = renderer.MainRenderPass;
      Program.ScheduleRendererRebuild();
    }
  }
}