using System;
using Dalamud.Hooking;
using Dalamud.Logging;
using FFXIVClientStructs.FFXIV.Client.Game;
using Dalamud.Game.ClientState.Party;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState;
using Dalamud.Game;

namespace Redirect
{
    internal class GameHooks : IDisposable
    {
        private const string OnActionSignature = "E8 ?? ?? ?? ?? EB 64 B1 01";
        private const string OnUIMOSignature = "E8 ?? ?? ?? ?? 48 8B 6C 24 58 48 8B 5C 24 50 4C 8B 7C";

        private PartyList PartyMembers => Services.PartyMembers;
        private ClientState ClientState => Services.ClientState;
        private TargetManager TargetManager => Services.TargetManager;
        private SigScanner SigScanner => Services.SigScanner;

        private delegate bool OnAction(IntPtr thisptr, ActionType p1, uint p2, uint p3, uint p4, uint p5, uint p6, ulong p7);
        private delegate void OnMouseoverEntity(IntPtr thisptr, IntPtr entity);

        private volatile IntPtr CurrentUIMouseover = IntPtr.Zero;

        private Hook<OnAction> action_hook = null!;
        private Hook<OnMouseoverEntity> mouseover_hook = null!;

        private Configuration Configuration;


        public GameHooks(Configuration config)
        {
            this.Configuration = config;
            var action_loc = SigScanner.ScanModule(OnActionSignature);
            var uimo_loc = SigScanner.ScanModule(OnUIMOSignature);

            if(action_loc == IntPtr.Zero || uimo_loc == IntPtr.Zero)
            {
                PluginLog.Error("Unable to initialize game hooks");
                return;
            }

            var action_offset = Dalamud.Memory.MemoryHelper.Read<int>(action_loc + 1);
            var action_hook_address = action_loc + 5 + action_offset;

            var uimo_offset = Dalamud.Memory.MemoryHelper.Read<int>(uimo_loc + 1);
            var uimo_hook_address = uimo_loc + 5 + uimo_offset;

            PluginLog.Information($"Hooking action use @ {action_hook_address}");
            PluginLog.Information($"Hooking UI mouseover @ {uimo_hook_address}");

            action_hook = new Hook<OnAction>(action_hook_address, new OnAction(OnActionCallback));
            mouseover_hook = new Hook<OnMouseoverEntity>(uimo_hook_address, new OnMouseoverEntity(OnMouseoverEntityCallback));

            action_hook.Enable();
            mouseover_hook.Enable();
        }

        private bool OnActionCallback(IntPtr thisptr, ActionType actionType, uint actionID, uint targetID = 0xE000_0000, uint a4 = 0, uint a5 = 0, uint a6 = 0, ulong a7 = 0)
        {
            return this.action_hook.Original(thisptr, actionType, actionID, targetID, a4, a5, a6, a7);
        }

        private void OnMouseoverEntityCallback(IntPtr thisptr, IntPtr entity)
        {
            this.CurrentUIMouseover = entity;
            this.mouseover_hook.Original(thisptr, entity);
        }

        public IntPtr ResolveTarget(string target)
        {
            switch(target)
            {
                case "UI Mouseover":
                    return CurrentUIMouseover;
                case "Field Mousover":
                    return TargetManager.MouseOverTarget == null ? IntPtr.Zero : TargetManager.MouseOverTarget.Address;
                case "Self":
                    return ClientState.LocalPlayer == null ? IntPtr.Zero : ClientState.LocalPlayer.Address;
                case "Target":
                    return TargetManager.Target == null ? IntPtr.Zero : TargetManager.Target.Address;
                case "Focus":
                    return TargetManager.FocusTarget == null ? IntPtr.Zero : TargetManager.FocusTarget.Address;
                case "<2>":
                    return PartyMembers.Length > 1 ? PartyMembers[1]!.Address : IntPtr.Zero;
                case "<3>":
                    return PartyMembers.Length > 2 ? PartyMembers[2]!.Address : IntPtr.Zero;
                case "<4>":
                    return PartyMembers.Length > 3 ? PartyMembers[3]!.Address : IntPtr.Zero;
                case "<5>":
                    return PartyMembers.Length > 4 ? PartyMembers[4]!.Address : IntPtr.Zero;
                case "<6>":
                    return PartyMembers.Length > 5 ? PartyMembers[5]!.Address : IntPtr.Zero;
                case "<7>":
                    return PartyMembers.Length > 6 ? PartyMembers[6]!.Address : IntPtr.Zero;
                case "<8>":
                    return PartyMembers.Length > 7 ? PartyMembers[7]!.Address : IntPtr.Zero;
                default:
                    return IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            PluginLog.Information("Uninstalling game hooks");
            this.action_hook?.Dispose();
            this.mouseover_hook?.Dispose();
            this.mouseover_hook = null!;
        }
    }
}
