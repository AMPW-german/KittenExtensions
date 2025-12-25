
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using HarmonyLib;
using KSA;

namespace KittenExtensions;

[HarmonyPatch]
internal static class Patches
{
  [HarmonyPatch(typeof(Program), "Main"), HarmonyPrefix]
  internal static void Program_Main_Prefix()
  {
    // We want to load before main, but after all mod assemblies are loaded in
    AssetEx.Init();
  }
}

[HarmonyPatch]
internal static class GaugeRendererPatch
{
  [HarmonyTargetMethod]
  internal static MethodBase TargetMethod() =>
    typeof(GaugeRenderer).GetConstructor([
      typeof(GaugeCanvas), typeof(GaugeComponent), typeof(RendererContext), typeof(Span<ShaderReference>)
    ]);

  [HarmonyTranspiler]
  internal static IEnumerable<CodeInstruction> GaugeRenderer_Ctor_Tranpsile(
    IEnumerable<CodeInstruction> instructions)
  {
    var matcher = new CodeMatcher(instructions);

    Span<CodeInstruction> extraArgs = [
      new CodeInstruction(OpCodes.Ldarg_2) // add GaugeComponent arg
    ];
    var fr = new MatcherFindReplace(matcher, "GaugeRenderer.ctor", extraArgs);

    fr.FindReplace(
      typeof(DescriptorPoolExExtensions).GetMethod(nameof(DescriptorPoolExExtensions.CreateDescriptorPool)),
      typeof(ShaderEx).GetMethod(nameof(ShaderEx.GaugeCreateDescriptorPool))
    );

    fr.FindReplace(
      typeof(DescriptorSetLayoutExExtensions).GetMethod(
        nameof(DescriptorSetLayoutExExtensions.CreateDescriptorSetLayout)),
      typeof(ShaderEx).GetMethod(nameof(ShaderEx.GaugeCreateDescriptorSetLayout))
    );

    fr.FindReplace(
      typeof(VkDeviceExtensions).GetMethod(nameof(VkDeviceExtensions.UpdateDescriptorSets)),
      typeof(ShaderEx).GetMethod(nameof(ShaderEx.GaugeUpdateDescriptorSets))
    );

    return matcher.Instructions();
  }
}


[HarmonyPatch]
internal static class XmlLoaderPatch
{
  [HarmonyTargetMethods]
  internal static IEnumerable<MethodBase> TargetMethods()
  {
    yield return typeof(Mod).GetMethod("LoadAssetBundles");
    yield return typeof(Mod).GetMethod("LoadPlanetMeshes");
    yield return typeof(Mod).GetMethod("LoadSystems");
    yield return typeof(Mod).GetMethod("PrepareSystems");
  }

  private static readonly MethodInfo SourceMethod = typeof(XmlLoader).GetMethod(nameof(XmlLoader.Load)) ??
    throw new InvalidOperationException($"Could not find XmlLoader source method");

  private static readonly MethodInfo ReplacementMethod = typeof(AssetEx).GetMethod(nameof(AssetEx.Load)) ??
    throw new InvalidOperationException("Could not find AssetEx replacement method");

  [HarmonyTranspiler]
  internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
  {
    var matcher = new CodeMatcher(instructions);

    matcher.Start();
    while (matcher.IsValid)
    {
      var inst = matcher.Instruction;
      if (IsDeserializeCall(inst, out var type))
        inst.operand = ReplacementMethod.MakeGenericMethod(type);

      matcher.Advance();
    }

    return matcher.Instructions();
  }

  private static bool IsDeserializeCall(CodeInstruction inst, out Type type)
  {
    type = default;
    if (inst.operand is not MethodInfo method)
      return false;
    if (!method.IsConstructedGenericMethod)
      return false;
    if (method.GetGenericMethodDefinition() != SourceMethod)
      return false;
    type = method.GetGenericArguments()[0];
    return true;
  }
}

public readonly ref struct MatcherFindReplace(
  CodeMatcher matcher, string name, Span<CodeInstruction> extraArgs = default)
{
  private readonly CodeMatcher matcher = matcher;
  private readonly string name = name;
  private readonly Span<CodeInstruction> extraArgs = extraArgs;

  public void FindReplace(MethodInfo from, MethodInfo to)
  {
    matcher.Start();
    matcher.MatchStartForward(CodeMatch.Calls(from));
    matcher.ThrowIfInvalid($"could not find call to {from} in {name}");

    // replace call
    matcher.Instruction.operand = to;

    // insert extra args before
    foreach (var arg in extraArgs)
      matcher.InsertAndAdvance(new CodeInstruction(arg));
  }
}