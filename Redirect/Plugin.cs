using Dalamud.Data;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using System;
using System.Threading.Tasks;

namespace Redirect
{
    public class Plugin : IDalamudPlugin, IDisposable
    {
        public string Name => "Redirect";

        private const string commandName = "/redirect";

        private Configuration Configuration { get; set; }
        private PluginUI PluginUi { get; set; }

        public DalamudPluginInterface Interface => Services.Interface;
        public DataManager DataManager => Services.DataManager;
        public CommandManager CommandManager => Services.CommandManager;

        private GameHooks Hooks;

        public Plugin([RequiredVersion("1.0")] DalamudPluginInterface i)
        {
            Services.Initialize(i);

            this.Configuration = Interface.GetPluginConfig() as Configuration ?? new Configuration();
            this.PluginUi = new PluginUI(this, this.Configuration);
            this.Hooks = new GameHooks(this.Configuration);

            CommandManager.AddHandler(commandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Adjust targeting priority for any action"
            });

            Task.Factory.StartNew(Actions.Initialize);
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
