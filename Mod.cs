
using System;
using HarmonyLib;
using KSA;
using StarMap.API;

namespace KittenExtensions;

[StarMapMod]
public class KxMod
{
  [StarMapBeforeMain]
  public void Setup()
  {
    var harmony = new Harmony("KittenExtensions");
    harmony.PatchAll();
  }
}

[KxAsset("ShaderEx")]
[KxAssetInject(typeof(GaugeComponent), nameof(GaugeComponent.FragmentShader), "FragmentEx")]
public class ShaderEx : ShaderReference
{
  public override void OnDataLoad(Mod mod)
  {
    base.OnDataLoad(mod);
    Console.WriteLine($"ShaderEx Loaded");
  }
}