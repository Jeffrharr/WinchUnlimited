using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace WinchUnlimited;

[BepInPlugin("com.deck.winchunlimited", "WinchUnlimited", "1.0.0")]
public class Plugin : BasePlugin
{
    internal static ManualLogSource Logger;
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<float> MinRopeLength;
    internal static ConfigEntry<bool> NoStall;
    internal static ConfigEntry<float> ReelSpeedMultiplier;

    public override void Load()
    {
        Logger = Log;
        Enabled = Config.Bind("General", "Enabled", true, "Master switch for the mod.");
        MinRopeLength = Config.Bind("General", "MinRopeLength", 0.3f,
            "Shortest the rope can be reeled in to, in metres. The game's default stops much earlier. Don't go to 0, the physics can explode.");
        NoStall = Config.Bind("General", "NoStall", true,
            "Stop the winch stalling or slipping under heavy load, so it keeps pulling no matter what.");
        ReelSpeedMultiplier = Config.Bind("General", "ReelSpeedMultiplier", 1f,
            "Multiplier for winch reel speed (1 = game default).");
        new Harmony("com.deck.winchunlimited").PatchAll(typeof(WinchPatch));
        Log.LogInfo("WinchUnlimited loaded.");
    }
}

[HarmonyPatch(typeof(VehicleWinchSystem), nameof(VehicleWinchSystem.FixedUpdateNetwork))]
static class WinchPatch
{
    static readonly System.Collections.Generic.Dictionary<IntPtr, float> defaultReelSpeed = new();

    static void Prefix(VehicleWinchSystem __instance)
    {
        try
        {
            if (!Plugin.Enabled.Value) return;
            // Only touch the local player's own winch
            if (__instance.Object == null || !__instance.Object.HasInputAuthority) return;

            __instance.minRopeLength = Plugin.MinRopeLength.Value;
            if (Plugin.NoStall.Value)
            {
                __instance.stallTension = 1e9f;
                __instance.maxTension = 1e9f;
                __instance.slipAboveMaxTension = false;
            }
            if (!defaultReelSpeed.TryGetValue(__instance.Pointer, out var baseSpeed))
                defaultReelSpeed[__instance.Pointer] = baseSpeed = __instance.winchReelSpeed;
            __instance.winchReelSpeed = baseSpeed * Plugin.ReelSpeedMultiplier.Value;
        }
        catch (Exception e) { Plugin.Logger.LogError(e); }
    }
}
