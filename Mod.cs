using System.Collections.Generic;
using System.Text.Json;
using Il2CppFusion;
using MelonLoader;
using OverTheHillCore;

[assembly: MelonInfo(typeof(WinchUnlimited.Mod), "WinchUnlimited", "1.0.0", "HillModding")]
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
        MaxExtension = c.CreateEntry("MaxExtension", 500f, description:
            "How far (m) the rope can be paid out once attached. Attaching still uses the game's normal range. 0 = game default.");

        Net.Register();
        WinchHooks.BeforeTick += WinchPatch.Prefix;
    }

    public override void OnUpdate()
    {
        HillNet.Tick();
        Net.Tick();
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
    static string lastSent;

    // Players without the mod (or who left) get the stock winch
    public static WinchSettings GetRemoteWinchSettings(int playerId) =>
        HillNet.PlayerHas(playerId, Channel) && remote.TryGetValue(playerId, out var s) ? s : null;

    internal static void Register()
    {
        HillNet.Register<WinchSettings>(Channel, OnReceive);
        HillNet.SessionReset += () => { remote.Clear(); lastSent = null; };
        HillNet.PlayerLeft += id => remote.Remove(id);
    }

    // Clients: (re)send whenever the host is ready or the config changes mid-session
    internal static void Tick()
    {
        var runner = HillNet.Runner;
        if (runner == null || runner.IsServer || !HillNet.HostHas(Channel)) return;
        var s = Mod.LocalWinchSettings();
        var json = JsonSerializer.Serialize(s);
        if (json == lastSent) return;
        lastSent = json;
        HillNet.SendToHost(Channel, s);
    }

    static void OnReceive(NetworkRunner runner, PlayerRef from, WinchSettings s)
    {
        if (runner.IsServer) remote[from.PlayerId] = s;
    }
}
