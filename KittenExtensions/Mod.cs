
using HarmonyLib;
using StarMap.API;
using KittenExtensions.Patch;

namespace KittenExtensions;

[StarMapMod]
public class KxMod
{
    [StarMapBeforeMain]
    public void Setup()
    {
        var logFile = PatchLogger.LogPath;
        if (File.Exists(logFile))
        {
            File.Delete(logFile);
        }

        var harmony = new Harmony("KittenExtensions");
        harmony.PatchAll();
    }
}