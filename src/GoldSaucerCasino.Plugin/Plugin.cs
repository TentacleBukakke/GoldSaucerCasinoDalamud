using Dalamud.Game.Command;
using Dalamud.Game.Inventory;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using GoldSaucerCasino.Plugin.Windows;

namespace GoldSaucerCasino.Plugin;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/casino";
    private readonly MainWindow mainWindow;
    private readonly Configuration configuration;

    [PluginService]
    private static IDalamudPluginInterface PluginInterface { get; set; } = null!;

    [PluginService]
    private static ICommandManager CommandManager { get; set; } = null!;

    [PluginService]
    private static IGameInventory GameInventory { get; set; } = null!;

    [PluginService]
    private static IObjectTable ObjectTable { get; set; } = null!;

    public Plugin()
    {
        this.configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.configuration.LastKnownGil = GetCurrentGil();
        PluginInterface.SavePluginConfig(this.configuration);

        this.mainWindow = new MainWindow(this.configuration, this.SaveConfiguration, GetCurrentGil, GetLocalPlayerName);

        PluginInterface.UiBuilder.Draw += this.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += this.OpenSettingsWindow;
        PluginInterface.UiBuilder.OpenMainUi += this.OpenMainWindow;

        CommandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open the Gold Saucer Casino table window.",
        });
    }

    public void Dispose()
    {
        this.mainWindow.Dispose();
        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.Draw -= this.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= this.OpenSettingsWindow;
        PluginInterface.UiBuilder.OpenMainUi -= this.OpenMainWindow;
    }

    private void OnCommand(string command, string arguments) => this.OpenMainWindow();

    private void OpenMainWindow() => this.mainWindow.IsOpen = true;

    private void OpenSettingsWindow() => this.mainWindow.IsSettingsOpen = true;

    private void Draw() => this.mainWindow.Draw();

    private void SaveConfiguration() => PluginInterface.SavePluginConfig(this.configuration);

    private static long GetCurrentGil()
    {
        try
        {
            var gil = 0L;
            foreach (var item in GameInventory.GetInventoryItems(GameInventoryType.Currency))
            {
                if (!item.IsEmpty && item.ItemId == 1)
                {
                    gil += item.Quantity;
                }
            }

            return gil;
        }
        catch
        {
            return 0;
        }
    }

    private static string GetLocalPlayerName()
    {
        try
        {
            return ObjectTable.LocalPlayer?.Name.TextValue ?? "You";
        }
        catch
        {
            return "You";
        }
    }
}
