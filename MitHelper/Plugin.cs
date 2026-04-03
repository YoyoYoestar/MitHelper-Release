using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using System.IO;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using MitHelper.Windows;

namespace MitHelper;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/mithelper";

    internal static Configuration Configuration { get; private set; } = null!;

    public readonly WindowSystem WindowSystem = new("MitHelper");
    private ConfigWindow   ConfigWindow   { get; init; }
    private MainWindow     MainWindow     { get; init; }
    private TankMitWindow  TankMitWindow  { get; init; }
    private EditorWindow   EditorWindow   { get; init; }

    public Plugin(IDataManager dataManager)
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        Data.AbilityExtraInfoData.Initialize(dataManager);

        var sheetLoader = new Data.MitSheetLoader(PluginInterface, Log);

        ConfigWindow  = new ConfigWindow();
        MainWindow    = new MainWindow(this, sheetLoader, Log);
        TankMitWindow = new TankMitWindow(MainWindow);
        EditorWindow  = new EditorWindow(PluginInterface, sheetLoader, Log, TextureProvider);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(TankMitWindow);
        WindowSystem.AddWindow(EditorWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage =
                "/mithelper          – Toggle the main window\n" +
                "/mithelper next     – Advance to the next phase\n" +
                "/mithelper prev     – Go back to the previous phase\n" +
                "/mithelper settings – Open the settings window\n" +
                "/mithelper tanks    – Toggle the separate tank mit window\n" +
                "/mithelper edit     – Toggle the sheet editor"
        });

        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Log.Information("===MitHelper loaded===");
    }

    private void Draw()
    {
        // Set tank window open state before drawing so it takes effect this frame
        if (Configuration.TankMitSeparateWindow && Configuration.ShowTankMits && MainWindow.IsOpen)
            TankMitWindow.IsOpen = true;
        else if (!Configuration.TankMitSeparateWindow || !Configuration.ShowTankMits)
            TankMitWindow.IsOpen = false;

        WindowSystem.Draw();
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        MainWindow.Dispose();
        TankMitWindow.Dispose();
        EditorWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        var arg = args.Trim().ToLowerInvariant();
        switch (arg)
        {
            case "next":
                MainWindow.IsOpen = true;
                MainWindow.NextPhase();
                break;

            case "prev":
            case "previous":
                MainWindow.IsOpen = true;
                MainWindow.PrevPhase();
                break;

            case "settings":
            case "config":
                ToggleConfigUi();
                break;

            case "tanks":
                TankMitWindow.Toggle();
                break;

            case "edit":
                EditorWindow.Toggle();
                break;

            default:
                ToggleMainUi();
                break;
        }
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi()   => MainWindow.Toggle();
}
