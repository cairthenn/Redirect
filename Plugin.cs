using Dalamud.Data;
using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Logging;
using System.Reflection;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Redirect
{
    using Log = Dalamud.Logging.PluginLog;
    public class Plugin : IDalamudPlugin, IDisposable
    {
        public string Name => "Redirect";

        private const string commandName = "/redirect";

        [PluginService] 
        internal static DalamudPluginInterface Interface { get; private set; } = null!;

        [PluginService]
        internal static CommandManager CommandManager { get; private set; } = null!;

        [PluginService]
        internal static DataManager DataManager { get; private set; } = null!;

        [PluginService]
        internal static SigScanner SigScanner { get; private set; } = null!;

        private Configuration Configuration { get; set; }
        private PluginUI PluginUi { get; set; }

        private GameHooks Hooks;

        public Plugin()
        { 
            this.Configuration = Interface.GetPluginConfig() as Configuration ?? new Configuration();
            this.Configuration.Initialize(Interface);
            this.PluginUi = new PluginUI(this, this.Configuration);
            this.Hooks = new GameHooks(this.Configuration);

            CommandManager.AddHandler(commandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Adjust targeting priority for any action"
            });

            Task.Factory.StartNew(CActions.Initialize);

            PluginLog.Information($"{this.Hooks.ResolveTarget("<me>")}");
        }


        public void Dispose()
        {
            this.Hooks.Dispose();
            this.PluginUi.Dispose();
            this.Configuration.Save();
            CommandManager.RemoveHandler(commandName);
        }

        private void OnCommand(string command, string args)
        {
            this.PluginUi.MainWindowVisible = true;
        }

    }
}
