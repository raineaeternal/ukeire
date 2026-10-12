using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using System.IO;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Ukeire.Game;
using Ukeire.Windows;

namespace Ukeire;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ISigScanner SigScanner { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IGameConfig GameConfig { get; private set; } = null!;

    private const string CommandName = "/ukeire";

    public Configuration Configuration { get; init; }
    public Coach Coach { get; init; }

    public readonly WindowSystem WindowSystem = new("Ukeire");
    private ConfigWindow ConfigWindow { get; init; }
    private CoachWindow CoachWindow { get; init; }

    private bool wasAtTable;

    public Plugin()
    {
        // Setup of FFXIVClientStructs has to be as early as possible due to possible hooks or calls to functions that may not be in the shipped version of Dalamud and due to using custom.
        FFXIVClientStructs.Interop.Generated.Addresses.Register();
        InteropGenerator.Runtime.Resolver.GetInstance.Setup(SigScanner.SearchBase, DataManager.GameData.Repositories["ffxiv"].Version, new FileInfo(Path.Combine(PluginInterface.ConfigDirectory.FullName, "cs.json")));
        InteropGenerator.Runtime.Resolver.GetInstance.Resolve();

        // Nothing above this line may touch ClientStructs; the Coach only reads the table from Framework.Update.
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        Coach = new Coach(Configuration, Framework, Log, new GameHistory(PluginInterface.ConfigDirectory, Log));
        ConfigWindow = new ConfigWindow(this);
        CoachWindow = new CoachWindow(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(CoachWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Doman Mahjong tutor. \"/ukeire config\" opens the settings."
        });

        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        CoachWindow.Dispose();
        Coach.Dispose();
        Coach.History.Flush();

        CommandManager.RemoveHandler(CommandName);
    }

    private void DrawUi()
    {
        var atTable = Coach.Snapshot.IsActive;
        if (atTable && !wasAtTable && Configuration.AutoOpen) CoachWindow.IsOpen = true;
        wasAtTable = atTable;
        WindowSystem.Draw();
    }

    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals("config", System.StringComparison.OrdinalIgnoreCase)) ToggleConfigUi();
        else CoachWindow.Toggle();
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => CoachWindow.Toggle();
}
