using System;
using System.Collections.Generic;
using Il2Cpp;

namespace WinchUnlimited;

public class WinchSettings
{
    public bool Enabled { get; set; }
    public float MinRopeLength { get; set; }
    public bool NoStall { get; set; }
    public float ReelSpeedMultiplier { get; set; }
    public float MaxExtension { get; set; }
}

// The game runs in host mode: the host simulates every car's winch, and clients may
// predict their own. So the host applies each modded player's settings to that
// player's winch, and clients apply their own settings locally for prediction.
// Runs at the start of each winch tick through JoofOTHFramework's shared winch hook.
static class WinchPatch
{
    class Defaults { public float MinRope, Stall, MaxTension, ReelSpeed; public bool Slip; }

    static readonly Dictionary<IntPtr, Defaults> defaults = new();

    internal static void Prefix(VehicleWinchSystem __instance)
    {
        try
        {
            var obj = __instance.Object;
            if (obj == null) return;

            WinchSettings s = null;
            if (obj.HasInputAuthority)
                s = Mod.LocalWinchSettings();
            else if (__instance.Runner != null && __instance.Runner.IsServer)
                s = Net.GetRemoteWinchSettings(obj.InputAuthority.PlayerId);

            if (!defaults.TryGetValue(__instance.Pointer, out var d))
            {
                defaults[__instance.Pointer] = d = new Defaults
                {
                    MinRope = __instance.minRopeLength,
                    Stall = __instance.stallTension,
                    MaxTension = __instance.maxTension,
                    Slip = __instance.slipAboveMaxTension,
                    ReelSpeed = __instance.winchReelSpeed,
                };
            }

            if (s == null || !s.Enabled)
            {
                // Restore stock values, e.g. after a modded player leaves or disables it
                __instance.minRopeLength = d.MinRope;
                __instance.stallTension = d.Stall;
                __instance.maxTension = d.MaxTension;
                __instance.slipAboveMaxTension = d.Slip;
                __instance.winchReelSpeed = d.ReelSpeed;
                return;
            }

            __instance.minRopeLength = Math.Max(0.05f, s.MinRopeLength);
            __instance.stallTension = s.NoStall ? 1e9f : d.Stall;
            __instance.maxTension = s.NoStall ? 1e9f : d.MaxTension;
            __instance.slipAboveMaxTension = !s.NoStall && d.Slip;
            __instance.winchReelSpeed = d.ReelSpeed * Math.Clamp(s.ReelSpeedMultiplier, 0.1f, 10f);

            // The game caps paying out at MaxRopeLength, which it sets on attach. Attaching
            // still uses the stock range; only the cap afterwards is raised. It's networked
            // state, so only the state authority (the host) writes it.
            if (s.MaxExtension > 0f && obj.HasStateAuthority
                && __instance.WinchState != VehicleWinchSystem.WinchStates.Disabled
                && __instance.MaxRopeLength < s.MaxExtension)
                __instance.MaxRopeLength = s.MaxExtension;
        }
        catch (Exception e) { Mod.Log.Error(e.ToString()); }
    }
}
