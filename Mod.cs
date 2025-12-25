
using HarmonyLib;
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