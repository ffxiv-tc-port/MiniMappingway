using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin;
using ImGuiNET;
using MiniMappingway.Api;
using MiniMappingway.Manager;
using MiniMappingway.Properties;
using MiniMappingway.Service;
using System.Globalization;

namespace MiniMappingway;

public sealed class Plugin : IDalamudPlugin
{
    internal static string Name => "Mini-Mappingway";

    private const string CommandName = "/mmway";
    private const string CommandNameDebug = "/mmwaydebug";

    public delegate void OnMessageDelegate(XivChatType type, uint senderId, ref SeString sender, ref SeString message, ref bool isHandled);

    public Plugin(
        IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<ServiceManager>();

        // Must run before any window is constructed so localized window titles resolve correctly.
        ConfigureLanguage(pluginInterface.UiLanguage);
        pluginInterface.LanguageChanged += ConfigureLanguage;

        ServiceManager.Configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        ServiceManager.Configuration.Initialize();

        #region Initialise Managers

        ServiceManager.NaviMapManager = new NaviMapManager();
        ServiceManager.PluginUi = new PluginUi();
        ServiceManager.WindowManager = new WindowManager();
        ServiceManager.ApiController = new ApiController();

        #endregion

        #region Initialise Services

        ServiceManager.FinderService = new FinderService();

        #endregion

        ServiceManager.WindowManager.AddWindowsToWindowSystem();

        #region Setup Commands and Actions

        ServiceManager.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = Resources.CommandHelpMessage
        });

        ServiceManager.CommandManager.AddHandler(CommandNameDebug, new CommandInfo(OnCommand));

        ServiceManager.DalamudPluginInterface.UiBuilder.Draw += ServiceManager.WindowSystem.Draw;
        ServiceManager.DalamudPluginInterface.UiBuilder.OpenConfigUi += DrawConfigUi;

        ServiceManager.ClientState.TerritoryChanged += TerritoryChanged;

        #endregion
    }

    private static void ConfigureLanguage(string langCode)
    {
        // TC quirk: the TC client's Dalamud reports UiLanguage "tw" — but "tw" is the
        // ISO 639-1 code for Twi, so new CultureInfo("tw") resolves to a culture with no
        // satellite assembly and every resx lookup silently falls back to English.
        // Map all Chinese/Taiwan language codes onto the shipped zh-Hant satellite.
        try
        {
            Resources.Culture = langCode.ToLowerInvariant() switch
            {
                "tw" or "zh" or "zh-tw" or "zh-hant" or "zh-cn" or "zh-hans" => new CultureInfo("zh-Hant"),
                _ => new CultureInfo(langCode),
            };
        }
        catch (CultureNotFoundException)
        {
            Resources.Culture = CultureInfo.InvariantCulture;
        }
    }

    public void Dispose()
    {
        ServiceManager.DalamudPluginInterface.LanguageChanged -= ConfigureLanguage;
        ServiceManager.CommandManager.RemoveHandler(CommandName);
        ServiceManager.CommandManager.RemoveHandler(CommandNameDebug);
        ServiceManager.DalamudPluginInterface.UiBuilder.Draw -= ServiceManager.WindowSystem.Draw;
        ServiceManager.DalamudPluginInterface.UiBuilder.OpenConfigUi -= DrawConfigUi;
        ServiceManager.ClientState.TerritoryChanged -= TerritoryChanged;
        ServiceManager.Dispose();

    }

    private void TerritoryChanged(ushort _)
    {
        ServiceManager.NaviMapManager.UpdateMap();

        foreach (var dict in ServiceManager.NaviMapManager.PersonDict)
        {
            ServiceManager.NaviMapManager.ClearPersonBag(dict.Key);
        }
    }

    private void OnCommand(string? command, string args)
    {
        ServiceManager.Log.Verbose("Command received");

        if (command is "/mmway")
        {
            ServiceManager.WindowManager.SettingsWindow.Toggle();
        }
        if (command is CommandNameDebug)
        {
            ServiceManager.NaviMapManager.DebugMode = !ServiceManager.NaviMapManager.DebugMode;
            if (ServiceManager.NaviMapManager.DebugMode)
            {
                ServiceManager.WindowManager.NaviMapWindow.Flags &= ~ImGuiWindowFlags.NoBackground;
            }
            else
            {
                ServiceManager.WindowManager.NaviMapWindow.Flags |= ImGuiWindowFlags.NoBackground;

            }
        }
        // in response to the slash command, just display our main ui

    }

    private void DrawConfigUi()
    {
        ServiceManager.WindowManager.SettingsWindow.Toggle();
    }
}
