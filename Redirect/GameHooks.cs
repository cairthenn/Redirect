using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Hooking;
using Dalamud.Logging;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
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

        private delegate bool OnAction(IntPtr tp, ActionType t, uint id, uint target = 0xE000_0000, uint p4 = 0, uint p5 = 0, uint p6 = 0, IntPtr l = default);
        private delegate bool OnPlacedAction(IntPtr tp, ActionType t, uint id, uint target = 0xE000_0000, IntPtr l = default, uint p5 = 0);

        private delegate void OnMouseoverEntity(IntPtr t, IntPtr entity);

        private volatile GameObject CurrentUIMouseover = null!;

        private Hook<OnAction> ActionHook = null!;
        private Hook<OnPlacedAction> PlacedActionHook = null!;
        private Hook<OnMouseoverEntity> MouseoverHook = null!;

        private Configuration Configuration;


        public GameHooks(Configuration config)
        {
            this.Configuration = config;
            var action_loc = SigScanner.ScanModule(OnActionSignature);
            var placed_action_loc = SigScanner.ScanModule(OnPlacedActionSignature);
            var uimo_loc = SigScanner.ScanModule(OnUIMOSignature);

            if (action_loc == IntPtr.Zero || uimo_loc == IntPtr.Zero || placed_action_loc == IntPtr.Zero)
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

            PlacedActionHook = new Hook<OnPlacedAction>(placed_action_hook_address, new OnPlacedAction(OnPlacedActionCallback));
            ActionHook = new Hook<OnAction>(action_hook_address, new OnAction(OnActionCallback));
            MouseoverHook = new Hook<OnMouseoverEntity>(uimo_hook_address, new OnMouseoverEntity(OnMouseoverEntityCallback));

            ActionHook.Enable();
            PlacedActionHook.Enable();
            MouseoverHook.Enable();
        }

        private GameObject? RedirectTarget(uint action_id)
        {
            if (!Configuration.Redirections.ContainsKey(action_id))
            {
                return null;
            }

            foreach (var t in Configuration.Redirections[action_id].Priority)
            {
                var nt = ResolveTarget(t);
                if (nt != null)
                {
                    return nt;
                }
            };

            return null;
        }

        private bool OnActionCallback(IntPtr this_ptr, ActionType action_type, uint id, uint target = 0xE000_0000, uint unk_4 = 0, uint unk_5 = 0, uint unk_6 = 0, IntPtr location = default)
        {          
            // This handles automatic upgrading of action bar slots
            // If the player can't place the action on their bar, use the base action for target resolution

            var adj_id = id;
            unsafe
            {
                var temp_id = ActionManager.fpGetAdjustedActionId(ActionManager.Instance(), id);
                var res = Actions.GetRow(temp_id);
                if(res != null && res.IsPlayerAction)
                {
                    adj_id = temp_id;
                }
            }

            var new_target = RedirectTarget(adj_id);
            if (new_target != null)
            {
                var res = Actions.GetRow(adj_id)!;
                if (res.TargetArea)
                {
                    // TODO: For some reason the vector becomes out of range, do some debugging and figure out how to not pepega

                    //var gch = GCHandle.Alloc(new_target.Position, GCHandleType.Normal);
                    //var new_location = (IntPtr) gch;
                    //var result = this.PlacedActionHook.Original(this_ptr, action_type, id, new_target.ObjectId, new_location);
                } 
                else
                {
                    return this.ActionHook.Original(this_ptr, action_type, id, new_target.ObjectId, unk_4, unk_5, unk_6, location);
                }
            }

            return this.ActionHook.Original(this_ptr, action_type, id, target, unk_4, unk_5, unk_6, location);
        }
        private unsafe bool OnPlacedActionCallback(IntPtr this_ptr, ActionType action_type, uint id, uint target = 0xE000_0000, IntPtr location = default, uint unk_5 = 0)
        {
            return this.PlacedActionHook.Original(this_ptr, action_type, id, target, location, unk_5);
        }

        private void OnMouseoverEntityCallback(IntPtr this_ptr, IntPtr entity)
        {
            this.MouseoverHook.Original(this_ptr, entity);

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
                    return PartyMembers.Length > 1 ? PartyMembers[1]!.GameObject : null;
                case "<3>":
                    return PartyMembers.Length > 2 ? PartyMembers[2]!.GameObject : null;
                case "<4>":
                    return PartyMembers.Length > 3 ? PartyMembers[3]!.GameObject : null;
                case "<5>":
                    return PartyMembers.Length > 4 ? PartyMembers[4]!.GameObject : null;
                case "<6>":
                    return PartyMembers.Length > 5 ? PartyMembers[5]!.GameObject : null;
                case "<7>":
                    return PartyMembers.Length > 6 ? PartyMembers[6]!.GameObject : null;
                case "<8>":
                    return PartyMembers.Length > 7 ? PartyMembers[7]!.GameObject : null;
                default:
                    return null;
            }
        }

        public void Dispose()
        {
            PluginLog.Information("Uninstalling game hooks");
            this.ActionHook?.Dispose();
            this.PlacedActionHook?.Dispose();
            this.MouseoverHook?.Dispose();
        }
    }
}
