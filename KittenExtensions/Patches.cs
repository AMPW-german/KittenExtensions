using HarmonyLib;
using KittenExtensions.Patch;
using KSA;
using System;
using System.Reflection;

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


  [HarmonyPatch(typeof(ModLibrary), nameof(ModLibrary.PrepareAll)), HarmonyPrefix]
  internal static void ModLibrary_PrepareAll_Prefix()
  {
    // PrepareManifest is called by StarMap, so we shouldn't need to do this here, but try anyways
    if (!ModLibrary.PrepareManifest())
      return;
    XmlPatcher.OnPrepare();
  }
}


[HarmonyPatch]
internal static class ContentLoaderPatch
{
  private static readonly MethodInfo ReadSourceMethod = typeof(ContentLoader).GetMethod(
    nameof(ContentLoader.ReadSource),
    new[] { typeof(string), typeof(string), typeof(ModManifest), typeof(ContentErrors), typeof(IContentLoadProgress) }) ??
    throw new InvalidOperationException("Could not find ContentLoader.ReadSource method");

  [HarmonyTargetMethod]
  private static MethodBase TargetMethod() => ReadSourceMethod;

  [HarmonyPostfix]
  private static void Postfix(ContentSource __result) => XmlPatcher.OnContentSource(__result);
}


[HarmonyPatch]
internal static class GameContentTagsPatch
{
  private static readonly MethodInfo BuildMethod = (typeof(ModLibrary).Assembly
      .GetType("KSA.GameContentTags", throwOnError: true)
      .GetMethod("Build", BindingFlags.Static | BindingFlags.NonPublic)) ??
    throw new InvalidOperationException("Could not find GameContentTags.Build method");

  [HarmonyTargetMethod]
  private static MethodBase TargetMethod() => BuildMethod;

  [HarmonyPostfix]
  private static void Postfix(ContentTagTable __result) => AssetEx.OnTagTable(__result);
}
