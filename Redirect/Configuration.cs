using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace Redirect {

    [Serializable]
    public class Configuration : IPluginConfiguration {
        public int Version { get; set; } = 1;
        public bool DefaultMouseoverFriendly { get; set; } = false;
        public bool DefaultModelMouseoverFriendly { get; set; } = false;
        public bool DefaultMouseoverHostile { get; set; } = false;
        public bool DefaultModelMouseoverHostile { get; set; } = false;
        public bool DefaultMouseoverGround { get; set; } = false;
        public bool DefaultCursorMouseover { get; set; } = false;
        public bool EnableMacroQueueing { get; set; } = false;
        public bool QueueGroundActions { get; set; } = false;
        public bool IgnoreErrors { get; set; } = true;
        public string DefaultRedirection { get; set; } = "UI Mouseover";
        public Dictionary<uint, Redirection> Redirections { get; set; } = new();

        public void Save() {
            Services.Interface.SavePluginConfig(this);
        }
    }
}
