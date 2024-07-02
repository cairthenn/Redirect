using Dalamud.Game;
using Dalamud.Game.ClientState.Objects;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface;

namespace Redirect {
    public class Services {
        [PluginService] public static IDalamudPluginInterface Interface { get; private set; } = null!;

        [PluginService] public static IPluginLog PluginLog { get; private set; } = null!;

        [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;

        [PluginService] public static IDataManager DataManager { get; private set; } = null!;

        [PluginService] public static ISigScanner SigScanner { get; private set; } = null!;

        [PluginService] public static IClientState ClientState { get; private set; } = null!;

        [PluginService] public static ITargetManager TargetManager { get; private set; } = null!;

        [PluginService] public static IObjectTable ObjectTable { get; private set; } = null!;

        [PluginService] public static IToastGui ToastGui { get; private set; } = null!;

        [PluginService] public static ITextureProvider TextureProvider { get; private set; } = null!;

        [PluginService] public static IGameInteropProvider InteropProvider { get; private set; } = null!;

        public static void Initialize(IDalamudPluginInterface i) {
            i.Create<Services>();
        }
    }
}
