using Dalamud.Configuration;

namespace GoldSaucerCasino.Plugin;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public long LastKnownGil { get; set; }

    public string LastRoomCode { get; set; } = string.Empty;

    public string RelayUrl { get; set; } = "http://127.0.0.1:5217";

    public int DefaultBuyIn { get; set; } = 10000;

    public long TotalEarnings { get; set; }

    public long TotalLosses { get; set; }

    public long NetProfit => this.TotalEarnings - this.TotalLosses;
}
