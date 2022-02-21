using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Hooking;
using Dalamud.Logging;
using FFXIVClientStructs.FFXIV.Client.Game;
using Dalamud.Game.Gui.Toast;

namespace Redirect { 
    internal class GameHooks : IDisposable {

        private const string UIMOSig = "E8 ?? ?? ?? ?? 48 8B 6C 24 58 48 8B 5C 24 50 4C 8B 7C";
        private const string ActionResourceSig = "E8 ?? ?? ?? ?? 4C 8B E8 48 85 C0 0F 84 ?? ?? ?? ?? 41 83 FE 04";
        private const string GroundActionCheckSig = "E8 ?? ?? ?? ?? 44 8B 83 ?? ?? ?? ?? 4C 8D 4C 24 60";
        private const string GetGroundPlacementSig = "E8 ?? ?? ?? ?? 84 C0 0F 84 ?? ?? ?? ?? 4c 8B C3 48 8D 54";

        private Configuration Configuration;
        private PartyList PartyMembers => Services.PartyMembers;
        private ClientState ClientState => Services.ClientState;
        private TargetManager TargetManager => Services.TargetManager;
        private SigScanner SigScanner => Services.SigScanner;
        private ToastGui ToastGui => Services.ToastGui;

        // param is the same in both functions,
        // 65535 can be observed for older food,
        // for teleports it is aertheryte ID,
        // generally 0

        private unsafe delegate bool TryActionDelegate(IntPtr tp, ActionType t, uint id, ulong target, uint param, uint origin, uint unk, void* l);
        private unsafe delegate bool UseActionDelegate(IntPtr tp, ActionType t, uint id, ulong target, Vector3* l, uint param = 0);
        private unsafe delegate void GroundActionValidDelegate(IntPtr tp, uint id, ActionType t, bool* results);
        private unsafe delegate bool GetGroundPlacementDelegate(IntPtr tp, uint id, ActionType t, Vector3* where, bool* results);
        private delegate uint ActionValidDelegate(uint id, IntPtr src, IntPtr dst);
        private delegate IntPtr GetActionResourceDelegate(int id);
        private delegate void MouseoverEntityDelegate(IntPtr t, IntPtr entity);

        private Hook<TryActionDelegate> TryActionHook = null!;
        private Hook<MouseoverEntityDelegate> MouseoverHook = null!;
        private UseActionDelegate UseAction = null!;
        private GetActionResourceDelegate GetActionResource = null!;
        private GroundActionValidDelegate GroundActionValid = null!;
        private GetGroundPlacementDelegate GetGroundPlacement = null!;
        private ActionValidDelegate ActionValid = null!;
        private volatile GameObject? CurrentUIMouseover = null!;

        public GameHooks(Configuration config) {
            Configuration = config;

            var uimo_ptr = SigScanner.ScanModule(UIMOSig);
            var actionres_ptr = SigScanner.ScanModule(ActionResourceSig);
            var groundaction_ptr = SigScanner.ScanModule(GroundActionCheckSig);
            var gp_ptr = SigScanner.ScanModule(GetGroundPlacementSig);

            if (uimo_ptr == IntPtr.Zero || actionres_ptr == IntPtr.Zero || groundaction_ptr == IntPtr.Zero) {
                PluginLog.Error("Error during game hook initialization, plugin functionality is disabled.");
                return;
            }

            var actionres_offset = Dalamud.Memory.MemoryHelper.Read<int>(actionres_ptr + 1);
            var actionres_fn_ptr = actionres_ptr + 5 + actionres_offset;
            GetActionResource = Marshal.GetDelegateForFunctionPointer<GetActionResourceDelegate>(actionres_fn_ptr);

            var groundaction_offset = Dalamud.Memory.MemoryHelper.Read<int>(groundaction_ptr + 1);
            var groundaction_fn_ptr = groundaction_ptr + 5 + groundaction_offset;
            GroundActionValid = Marshal.GetDelegateForFunctionPointer<GroundActionValidDelegate>(groundaction_fn_ptr);


            var gp_offset = Dalamud.Memory.MemoryHelper.Read<int>(gp_ptr + 1);
            var gp_fn_ptr = gp_ptr + 5 + gp_offset;
            GetGroundPlacement = Marshal.GetDelegateForFunctionPointer<GetGroundPlacementDelegate>(gp_fn_ptr);

            var uimo_offset = Dalamud.Memory.MemoryHelper.Read<int>(uimo_ptr + 1);
            var uimo_hook_ptr = uimo_ptr + 5 + uimo_offset;
            MouseoverHook = new Hook<MouseoverEntityDelegate>(uimo_hook_ptr, OnMouseoverEntityCallback);

            unsafe {
                TryActionHook = new Hook<TryActionDelegate>((IntPtr) ActionManager.fpUseAction, TryActionCallback);
                UseAction = Marshal.GetDelegateForFunctionPointer<UseActionDelegate>((IntPtr) ActionManager.fpUseActionLocation);
                ActionValid = Marshal.GetDelegateForFunctionPointer<ActionValidDelegate>((IntPtr) ActionManager.fpGetActionInRangeOrLoS);
            }

            UpdateSprintQueueing(Configuration.QueueSprint);
            UpdatePotionQueueing(Configuration.QueuePotions);

            TryActionHook.Enable();
            MouseoverHook.Enable();
        }

        // ActionResource:

        // 0x08:    IconID
        // 0x0A:    Cast VFX
        // 0x0C:    ActionTimeline
        // 0x0E:    Cost
        // 0x14:    Cast
        // 0x16:    Recast
        // 0x1E:    ActionTimeline
        // 0x20:    ActionCategory
        // 0x40:    Name

        public void UpdateSprintQueueing(bool enable) {
            var res = GetActionResource(3);
            var type = enable ? ActionType.Ability : ActionType.MainCommand;
            Dalamud.SafeMemory.Write(res + 0x20, (byte) type);
        }

        public void UpdatePotionQueueing(bool enable) {
            var res = GetActionResource(0x34E);
            var type = enable ? ActionType.Ability : ActionType.General;
            Dalamud.SafeMemory.Write(res + 0x20, (byte)type);
        }

        private bool TryQueueAction(IntPtr action_manager, uint id, uint param, ActionType action_type, ulong target_id) {
            Dalamud.SafeMemory.Read(action_manager + 0x68, out int queue_full);

            if (queue_full > 0) {
                return false;
            }

            // This is how the game queues actions within the "TryAction" function
            // There is no separate function for it, it simply updates variables
            // within the ActionManager during the call

            Dalamud.SafeMemory.Write(action_manager + 0x68, 1);
            Dalamud.SafeMemory.Write(action_manager + 0x6C, (byte) action_type);
            Dalamud.SafeMemory.Write(action_manager + 0x70, id);
            Dalamud.SafeMemory.Write(action_manager + 0x78, target_id);
            Dalamud.SafeMemory.Write(action_manager + 0x80, 0); // "Origin", for whatever reason
            Dalamud.SafeMemory.Write(action_manager + 0x84, param);
            return true;
        }

        private bool ValidateRange(Lumina.Excel.GeneratedSheets.Action action, GameObject? target, bool place_at_cursor = false) {

            if(!Configuration.SilentRangeFailure) {
                return true;
            } 
            else if (target == null && !place_at_cursor) {
                return false;
            }

            if (place_at_cursor) {
                bool[] results = new bool[4];
                unsafe {
                    fixed (bool* p = results) {
                        GroundActionValid((IntPtr)ActionManager.Instance(), action.RowId, ActionType.Spell, p);
                    }
                    var success = results[0] && results[1];
                    return success;
                }
            }

            return ActionValid(action.RowId, ClientState.LocalPlayer!.Address, target!.Address) == 0;
        }


        private GameObject? RedirectTarget(Lumina.Excel.GeneratedSheets.Action original, Lumina.Excel.GeneratedSheets.Action upgraded, ref bool place_at_cursor) {

            var id = original.RowId;

            // Global fallbacks

            if (!Configuration.Redirections.ContainsKey(id)) {
                
                if(Configuration.DefaultMouseoverFriendly && upgraded.CanTargetFriendly()) {
                    
                    if (CurrentUIMouseover != null && ValidateRange(upgraded, CurrentUIMouseover)) {
                        return CurrentUIMouseover;
                    }
                    else if (Configuration.DefaultModelMouseoverFriendly && TargetManager.MouseOverTarget != null && ValidateRange(upgraded, TargetManager.MouseOverTarget)) {
                        return TargetManager.MouseOverTarget;
                    }
                    else if (Configuration.DefaultCursorMouseover && upgraded.TargetArea && !upgraded.IsActionBlocked()) {
                        place_at_cursor = true;
                        return null;
                    }
                }
                else if(Configuration.DefaultMouseoverHostile && upgraded.CanTargetHostile) {

                    if (CurrentUIMouseover != null && ValidateRange(upgraded, CurrentUIMouseover)) {
                        return CurrentUIMouseover;
                    }
                    else if (Configuration.DefaultModelMouseoverHostile && ValidateRange(upgraded, TargetManager.MouseOverTarget)) {
                        return TargetManager.MouseOverTarget;
                    }
                }

                return null;
            }

            // Individual spells

            foreach (var t in Configuration.Redirections[id].Priority) {
                var nt = ResolveTarget(t, ref place_at_cursor);
                var range_ok = ValidateRange(upgraded, nt, place_at_cursor);

                if (range_ok && (nt != null || place_at_cursor)) {
                    return nt;
                }
            };

            return null;
        }

        public GameObject? ResolveTarget(string target, ref bool place_at_cursor) {

            place_at_cursor = false;

            switch (target) {
                case "Cursor":
                    place_at_cursor = true;
                    return null;
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

        private unsafe bool TryActionCallback(IntPtr this_ptr, ActionType action_type, uint id, ulong target, uint param, uint origin, uint unk, void* location) {
            
            // Potion dequeueing
            if(Configuration.QueuePotions && action_type == ActionType.Item && origin == 1) {
                param = 65535;
            }

            // Special sprint handling
            if (Configuration.QueueSprint && action_type == ActionType.General && id == 4) {
                return TryActionHook.Original(this_ptr, ActionType.Spell, 3, target, param, origin, unk, location);
            }

            // This is NOT the same classification as the item's resource, but a more generic version
            // Every spell, ability, and weaponskill (even sprint, which is a "main command"), gets called with
            // the designation of "Spell" (1). Thus, we can avoid most tomfoolery by returning early

            if (action_type != ActionType.Spell) {
                return TryActionHook.Original(this_ptr, action_type, id, target, param, origin, unk, location);
            }

            var original_res = Actions.GetRow(id)!;

            if(original_res != null && original_res.IsPvP) {
                return TryActionHook.Original(this_ptr, action_type, id, target, param, origin, unk, location);
            }

            // Macro queueing

            origin = origin == 2 && Configuration.EnableMacroQueueing ? 0 : origin;

            // Actions placed on bars try to use their base action, so we need to get the upgraded version

            var temp_id = ActionManager.fpGetAdjustedActionId((ActionManager*) this_ptr, id);
            var use_res = original_res!;

            var adjusted_res = Actions.GetRow(temp_id)!;

            if (adjusted_res.IsPlayerAction) {
                use_res = adjusted_res!;
            }

            bool place_at_cursor = false;
            var new_target = RedirectTarget(original_res, adjusted_res, ref place_at_cursor);

            // Ground targeting actions at the cursor

            if (place_at_cursor) {
                var status = ActionManager.fpGetActionStatus((ActionManager*) this_ptr, action_type, id, (uint) target, 1, 1);

                if (status != 0 && status != 0x244) {
                    return TryActionHook.Original(this_ptr, action_type, id, target, param, origin, unk, location);
                }

                Dalamud.SafeMemory.Read(this_ptr + 0x08, out float animation_timer);

                if(status == 0x244 || animation_timer > 0) {
                    if(!Configuration.QueueGroundActions) {
                        ToastGui.ShowError("Cannot use while casting.");
                        return false;
                    }

                    return TryQueueAction(this_ptr, id, param, action_type, target);
                }

                bool[] results  = new bool[4];

                fixed (bool* p = results) {
                    GroundActionValid(this_ptr, use_res.RowId, action_type, p);
                }

                Vector3 v;
                bool[] results2 = new bool[4];

                fixed (bool* p = results2) {
                    bool success = GetGroundPlacement(this_ptr, use_res.RowId, action_type, &v, p);
                }

                if (results[1]) {
                    return UseAction(this_ptr, action_type, use_res.RowId, target, &v, param);
                }

                if(results[2]) {
                    ToastGui.ShowError("Target is not in range.");
                }
                else {
                    ToastGui.ShowError("Invalid target.");
                }

                return false;
            }

            // Successfully changed target

            if (new_target != null) {

                if (!use_res.TargetArea) {
                    return TryActionHook.Original(this_ptr, action_type, use_res.RowId, new_target.ObjectId, param, origin, unk, location);
                }

                // Ground placed action at specific game object

                var status = ActionManager.fpGetActionStatus((ActionManager*) this_ptr, action_type, use_res.RowId, (uint) target, 1, 1);

                if (status != 0 && status != 0x244) {
                    return TryActionHook.Original(this_ptr, action_type, id, target, param, origin, unk, location);
                }

                Dalamud.SafeMemory.Read(this_ptr + 0x08, out float animation_timer);

                if (status == 0x244 || animation_timer > 0) {
                    if (!Configuration.QueueGroundActions) {
                        ToastGui.ShowError("Cannot use while casting.");
                        return false;
                    }

                    return TryQueueAction(this_ptr, use_res.RowId, param, action_type, target);
                }

                var result = ActionValid(id, ClientState.LocalPlayer!.Address, new_target.Address);

                if (result == 0) {
                    var new_location = new_target.Position;
                    return UseAction(this_ptr, action_type, use_res.RowId, new_target.ObjectId, &new_location);
                }

                if(result == 0x236) {
                    ToastGui.ShowError("Target is not in range.");
                }
                else {
                    ToastGui.ShowError("Invalid target.");
                }

                return false;
            }

            // Use the action normally

            return TryActionHook.Original(this_ptr, action_type, id, target, param, origin, unk, location);
        }

        private void OnMouseoverEntityCallback(IntPtr this_ptr, IntPtr entity) {
            MouseoverHook.Original(this_ptr, entity);
            CurrentUIMouseover = entity == IntPtr.Zero ? null : Services.ObjectTable.CreateObjectReference(entity);
        }

        public void Dispose() {
            TryActionHook?.Dispose();
            MouseoverHook?.Dispose();
            UpdatePotionQueueing(false);
            UpdateSprintQueueing(false);
        }
    }
}
