using System;
using Dalamud.Hooking;
using Dalamud.Logging;
using FFXIVClientStructs.FFXIV.Client.Game;
using Dalamud.Game.ClientState.Party;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState;
using Dalamud.IoC;

namespace Redirect
{
    internal class GameHooks : IDisposable
    {
        private const string OnActionSignature = "E8 ?? ?? ?? ?? EB 64 B1 01";
        private const string ResolveTextSignature = "E8 ?? ?? ?? ?? 48 8B 5C 24 ?? EB 0C";

        [PluginService] internal static PartyList PartyMembers { get; private set; } = null!;
        [PluginService] internal static ClientState ClientState { get; private set; } = null!;
        [PluginService] internal static TargetManager TargetManager { get; private set; } = null!;

        private delegate bool OnAction(IntPtr thisptr, ActionType p1, uint p2, uint p3, uint p4, uint p5, uint p6, ulong p7);
        private delegate void OnMouseoverEntity(IntPtr thisptr, IntPtr entity);
        private IntPtr resolvetext_thisptr = IntPtr.Zero;
        private delegate IntPtr ResolveText(IntPtr thisptr, string text, byte param3, byte param4);

        private Hook<OnAction> action_hook;
        private Hook<ResolveText> resolve_hook;
        private Hook<OnMouseoverEntity> mouseover_hook;

        private Configuration Configuration;


        public GameHooks(Configuration config)
        {
            this.Configuration = config;
            var action_loc = Plugin.SigScanner.ScanModule(OnActionSignature);
            //var resolve_loc = Plugin.SigScanner.ScanModule(ResolveTextSignature);

            if(action_loc == IntPtr.Zero)
            {
                PluginLog.Error("Unable to initialize game hooks");
                return;
            }

            var action_offset = Dalamud.Memory.MemoryHelper.Read<int>(action_loc + 1);
            var action_hook_address = action_loc + 5 + action_offset;

            //var resolve_offset = Dalamud.Memory.MemoryHelper.Read<int>(resolve_loc + 1);
            //var resolve_hook_address = resolve_loc + 5 + resolve_offset;

            PluginLog.Information($"Hooking action use @ {action_hook_address}");
            //PluginLog.Information($"Hooking text resolver @ {resolve_hook_address}");

            action_hook = new Hook<OnAction>(action_hook_address, new OnAction(OnActionCallback));
            //resolve_hook = new Hook<ResolveText>(resolve_hook_address, new ResolveText(ResolveTextCallback));

            action_hook.Enable();
            resolve_hook.Enable();
        }

        private bool OnActionCallback(IntPtr thisptr, ActionType actionType, uint actionID, uint targetID = 0xE000_0000, uint a4 = 0, uint a5 = 0, uint a6 = 0, ulong a7 = 0)
        {
            PluginLog.Log($"Action Callback[{thisptr}], {actionType}, {actionID}, {targetID}, {a4}, {a5}, {a6}");
            return this.action_hook.Original(thisptr, actionType, actionID, targetID, a4, a5, a6, a7);
        }

        //private IntPtr ResolveTextCallback(IntPtr thisptr, string text, byte param3, byte param4)
        //{
        //    this.resolvetext_thisptr = thisptr;
        //    PluginLog.Log($"Resolve Text Callback[{thisptr}], {text}, {param3}, {param4}");
        //    return this.resolve_hook.Original(thisptr, text, param3, param4);
        //}

        public IntPtr ResolveTarget(string target)
        {
            switch(target)
            {
                case "Self":
                    return ClientState.LocalPlayer!.Address;
                case "Target":
                    return TargetManager.Target == null ? IntPtr.Zero : TargetManager.Target.Address;
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
            this.action_hook?.Disable();
            this.resolve_hook?.Disable();
            this.mouseover_hook = null!;
        }
    }
}
