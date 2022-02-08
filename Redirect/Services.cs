using Dalamud.Data;
using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Party;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Game.Gui;

namespace Redirect { 
    public class Services {
        [PluginService]
        public static DalamudPluginInterface Interface { get; private set; } = null!;

        [PluginService]
        public static CommandManager CommandManager { get; private set; } = null!;

        [PluginService]
        public static DataManager DataManager { get; private set; } = null!;

        [PluginService]
        public static SigScanner SigScanner { get; private set; } = null!;

        [PluginService]
        public static PartyList PartyMembers { get; private set; } = null!;

        [PluginService]
        public static ClientState ClientState { get; private set; } = null!;

        [PluginService]
        public static TargetManager TargetManager { get; private set; } = null!;

        [PluginService]
        public static ObjectTable ObjectTable { get; private set; } = null!;

        [PluginService]
        public static GameGui GameGui { get; private set; } = null!;

        public static void Initialize(DalamudPluginInterface i) {
            i.Create<Services>();
        }
    }
}
