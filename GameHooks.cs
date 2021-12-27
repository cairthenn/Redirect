using System;
using Dalamud.Hooking;
using Dalamud.Logging;
using FFXIVClientStructs.FFXIV.Client.Game;
using Dalamud.Game.ClientState.Party;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Types;
using System.Runtime.InteropServices;

namespace Redirect
{
    internal class GameHooks : IDisposable
    {
        private const string OnActionSignature = "E8 ?? ?? ?? ?? EB 64 B1 01";
        private const string OnPlacedActionSignature = "E8 ?? ?? ?? ?? 3C 01 0F 85 ?? ?? ?? ?? EB 46";
        private const string OnUIMOSignature = "E8 ?? ?? ?? ?? 48 8B 6C 24 58 48 8B 5C 24 50 4C 8B 7C";

        private PartyList PartyMembers => Services.PartyMembers;
        private ClientState ClientState => Services.ClientState;
        private TargetManager TargetManager => Services.TargetManager;
        private SigScanner SigScanner => Services.SigScanner;

        private delegate bool OnAction(IntPtr t, ActionType p1, uint p2, uint p3, uint p4, uint p5, uint p6, ulong p7);
        private delegate bool OnPlacedAction(IntPtr t, ActionType p1, uint p2, uint p3, ulong p4, uint p5);
        
        private delegate void OnMouseoverEntity(IntPtr t, IntPtr entity);

        private volatile GameObject CurrentUIMouseover = null!;

        private Hook<OnAction> action_hook = null!;
        private Hook<OnPlacedAction> placed_action_hook = null!;
        private Hook<OnMouseoverEntity> mouseover_hook = null!;

        private Configuration Configuration;


        public GameHooks(Configuration config)
        {
            this.Configuration = config;
            var action_loc = SigScanner.ScanModule(OnActionSignature);
            var placed_action_loc = SigScanner.ScanModule(OnPlacedActionSignature);
            var uimo_loc = SigScanner.ScanModule(OnUIMOSignature);

            if(action_loc == IntPtr.Zero || uimo_loc == IntPtr.Zero || placed_action_loc == IntPtr.Zero)
            {
                PluginLog.Error("Unable to initialize game hooks");
                return;
            }

            var action_offset = Dalamud.Memory.MemoryHelper.Read<int>(action_loc + 1);
            var action_hook_address = action_loc + 5 + action_offset;

            var placed_action_offset = Dalamud.Memory.MemoryHelper.Read<int>(placed_action_loc + 1);
            var placed_action_hook_address = placed_action_loc + 5 + placed_action_offset;

            var uimo_offset = Dalamud.Memory.MemoryHelper.Read<int>(uimo_loc + 1);
            var uimo_hook_address = uimo_loc + 5 + uimo_offset;

            PluginLog.Information($"Hooking action use @ {action_hook_address}");
            PluginLog.Information($"Hooking UI mouseover @ {uimo_hook_address}");

            placed_action_hook = new Hook<OnPlacedAction>(placed_action_hook_address, new OnPlacedAction(OnPlacedActionCallback));
            action_hook = new Hook<OnAction>(action_hook_address, new OnAction(OnActionCallback));
            mouseover_hook = new Hook<OnMouseoverEntity>(uimo_hook_address, new OnMouseoverEntity(OnMouseoverEntityCallback));

            action_hook.Enable();
            placed_action_hook.Enable();
            mouseover_hook.Enable();
        }

        private uint RedirectTarget(uint action_id)
        {
            if(!Configuration.Redirections.ContainsKey(action_id))
            {
                return 0;
            }

            foreach(var t in Configuration.Redirections[action_id].Priority)
            {
                var nt = ResolveTarget(t);
                if (nt != null)
                {
                    return nt.ObjectId;
                }
            };

            return 0;
        }

        private bool OnActionCallback(IntPtr this_ptr, ActionType action_type, uint id, uint target = 0xE000_0000, uint unk_4 = 0, uint unk_5 = 0, uint unk_6 = 0, ulong location = 0)
        {
            var new_target = RedirectTarget(id);

            if (new_target != 0)
            {
                PluginLog.Information($"Changing target to {new_target}");
                return this.action_hook.Original(this_ptr, action_type, id, new_target, unk_4, unk_5, unk_6, location);
            }

            return this.action_hook.Original(this_ptr, action_type, id, target, unk_4, unk_5, unk_6, location);
        }

        private bool OnPlacedActionCallback(IntPtr this_ptr, ActionType action_type, uint id, uint target = 0xE000_0000, ulong location = 0, uint unk_5 = 0)
        {
            return this.placed_action_hook.Original(this_ptr, action_type, id, target, location, unk_5);
        }

        private void OnMouseoverEntityCallback(IntPtr this_ptr, IntPtr entity)
        {
            this.mouseover_hook.Original(this_ptr, entity);

            if (entity == IntPtr.Zero)
            {
                this.CurrentUIMouseover = null!;
            } 
            else
            {
                this.CurrentUIMouseover = Services.ObjectTable.CreateObjectReference(entity)!;
            }
        }

        public GameObject? ResolveTarget(string target)
        {
            switch(target)
            {
                case "UI Mouseover":
                    return CurrentUIMouseover;
                case "Model Mouseover":
                    return TargetManager.MouseOverTarget;
                case "Self":
                    return ClientState.LocalPlayer;
                case "Target":
                    return TargetManager.Target;
                case "Focus":
                    return TargetManager.FocusTarget;
                case "<2>":
                    return PartyMembers.Length > 0 ? PartyMembers[1]!.GameObject : null;
                case "<3>":
                    return PartyMembers.Length > 1 ? PartyMembers[2]!.GameObject : null;
                case "<4>":
                    return PartyMembers.Length > 2 ? PartyMembers[3]!.GameObject : null;
                case "<5>":
                    return PartyMembers.Length > 3 ? PartyMembers[4]!.GameObject : null;
                case "<6>":
                    return PartyMembers.Length > 4 ? PartyMembers[5]!.GameObject : null;
                case "<7>":
                    return PartyMembers.Length > 5 ? PartyMembers[6]!.GameObject : null;
                case "<8>":
                    return PartyMembers.Length > 6 ? PartyMembers[7]!.GameObject : null;
                default:
                    return null;
            }
        }

        public void Dispose()
        {
            PluginLog.Information("Uninstalling game hooks");
            this.action_hook?.Dispose();
            this.placed_action_hook?.Dispose();
            this.mouseover_hook?.Dispose();
            this.mouseover_hook = null!;
        }
    }
}
