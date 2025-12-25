
using System;
using System.Collections.Generic;
using System.Reflection;
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