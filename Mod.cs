using System.Collections.Generic;
using Il2CppFusion;
using MelonLoader;
using JoofOTHFramework;

[assembly: MelonInfo(typeof(WinchUnlimited.Mod), "WinchUnlimited", "1.1.0", "Joof")]
[assembly: MelonGame("Funselektor", "over the hill")]

namespace WinchUnlimited;

// Winch tweaks: reel in further, no stalling, faster reel, longer pay-out
public class Mod : MelonMod
{
    internal static MelonLogger.Instance Log;

    internal static MelonPreferences_Entry<bool> Enabled;
    internal static MelonPreferences_Entry<float> MinRopeLength;
    internal static MelonPreferences_Entry<bool> NoStall;
    internal static MelonPreferences_Entry<float> ReelSpeedMultiplier;
    internal static MelonPreferences_Entry<float> MaxExtension;

    public override void OnInitializeMelon()
    {
        Log = LoggerInstance;
        var c = MelonPreferences.CreateCategory("WinchUnlimited");
        Enabled = c.CreateEntry("Enabled", true, description: "Unlimited winch retraction.");
        MinRopeLength = c.CreateEntry("MinRopeLength", 0.3f, description:
            "Shortest the rope can be reeled in to, in metres. Don't go to 0, the physics can explode.");
        NoStall = c.CreateEntry("NoStall", true, description: "Stop the winch stalling or slipping under heavy load.");
        ReelSpeedMultiplier = c.CreateEntry("ReelSpeedMultiplier", 1f, description: "Multiplier for winch reel speed (1 = game default).");
        MaxExtension = c.CreateEntry("MaxExtension", 9000f, description:
            "How far (m) the rope can be paid out once attached. Attaching still uses the game's normal range. 0 = game default.");

        Net.Register();
        WinchHooks.BeforeTick += WinchPatch.Prefix;
    }

    public override void OnUpdate()
    {
        if (Core.Quitting) return;
        HillNet.Tick();
    }

    internal static WinchSettings LocalWinchSettings() => new()
    {
        Enabled = Enabled.Value,
        MinRopeLength = MinRopeLength.Value,
        NoStall = NoStall.Value,
        ReelSpeedMultiplier = ReelSpeedMultiplier.Value,
        MaxExtension = MaxExtension.Value,
    };
}

// The host simulates every car's winch, so each client sends the host its own settings
static class Net
{
    const string Channel = "WinchUnlimited";

    static readonly Dictionary<int, WinchSettings> remote = new();

    // Players without the mod (or who left) get the stock winch
    public static WinchSettings GetRemoteWinchSettings(int playerId) =>
        HillNet.PlayerHas(playerId, Channel) && remote.TryGetValue(playerId, out var s) ? s : null;

    internal static void Register()
    {
        HillNet.Register<WinchSettings>(Channel, OnReceive);
        HillNet.SessionReset += () => remote.Clear();
        HillNet.PlayerLeft += id => remote.Remove(id);
        // Clients: send when the host answers, and again whenever a setting changes mid-session
        HillNet.HostReady += Send;
        Mod.Enabled.OnEntryValueChanged.Subscribe((_, _) => Send());
        Mod.MinRopeLength.OnEntryValueChanged.Subscribe((_, _) => Send());
        Mod.NoStall.OnEntryValueChanged.Subscribe((_, _) => Send());
        Mod.ReelSpeedMultiplier.OnEntryValueChanged.Subscribe((_, _) => Send());
        Mod.MaxExtension.OnEntryValueChanged.Subscribe((_, _) => Send());
    }

    static void Send()
    {
        var runner = HillNet.Runner;
        if (runner == null || runner.IsServer || !HillNet.HostHas(Channel)) return;
        HillNet.SendToHost(Channel, Mod.LocalWinchSettings());
    }

    static void OnReceive(NetworkRunner runner, PlayerRef from, WinchSettings s)
    {
        if (runner.IsServer) remote[from.PlayerId] = s;
    }
}
