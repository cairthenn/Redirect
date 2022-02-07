using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace Redirect
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 1;

        public bool DisplayPVP { get; set; } = false;

        public bool EnableMacroQueueing { get; set; } = false;

        // public bool InstantGroundActions { get; set; } = false;

        public bool QueueSprint { get; set; } = false;

        public string DefaultRedirection { get; set; } = "UI Mouseover";

        public Dictionary<uint, Redirection> Redirections { get; set; } = new();

        public void Save()
        {
            Services.Interface.SavePluginConfig(this);
        }
    }
}
