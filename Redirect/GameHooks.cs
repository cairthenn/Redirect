using Dalamud.Game;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Hooking;
using Dalamud.Logging;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Lumina.Excel.GeneratedSheets;
using Lumina.Excel.GeneratedSheets2;

namespace Redirect {
    internal class GameHooks : IDisposable {

        private const int SprintID = 3;
        private const int PotionID = 846;
        private const uint DefaultTarget = 0xE0000000;
        private const string UIMOSig = "E8 ?? ?? ?? ?? 48 8B 7C 24 50 4C 8B 74 24 58 83 FD 02";
        private const string ActionResourceSig = "E8 ?? ?? ?? ?? 80 FB 12";

        private Configuration Configuration { get; } = null!;
        private Actions Actions { get; } = null!;
        private static ITargetManager TargetManager => Services.TargetManager;
        private static ISigScanner SigScanner => Services.SigScanner;
        private static IToastGui ToastGui => Services.ToastGui;

        private unsafe delegate bool TryActionDelegate(IntPtr tp, ActionType t, uint id, ulong target, uint param, uint origin, uint unk, Vector3* l);
        private unsafe delegate bool UseActionDelegate(IntPtr tp, ActionType t, uint id, ulong target, Vector3* l, uint param = 0);
        private delegate IntPtr GetActionRowPtrDelegate(int id);
        private delegate void MouseoverEntityDelegate(IntPtr t, IntPtr entity);
        private readonly Hook<TryActionDelegate> TryActionHook = null!;
        private readonly Hook<MouseoverEntityDelegate> MouseoverHook = null!;
        private readonly UseActionDelegate UseAction = null!;
        private readonly GetActionRowPtrDelegate GetActionRowPtr = null!;
        private volatile IGameObject? CurrentUIMouseover = null!;

        private const byte AbilityActionCategory = 4;
        private static byte SprintActionCategory = 0;
        private static byte ItemActionCategory = 0;

        public GameHooks(Configuration config, Actions actions) {
            Configuration = config;
            Actions = actions;

            var uimo_ptr = SigScanner.ScanModule(UIMOSig);
            var actionres_ptr = SigScanner.ScanModule(ActionResourceSig);

            if (uimo_ptr == IntPtr.Zero || actionres_ptr == IntPtr.Zero) {
                Services.PluginLog.Error("Error during game hook initialization, plugin functionality is disabled.");
                return;
            }

            var uimo_offset = Dalamud.Memory.MemoryHelper.Read<int>(uimo_ptr + 1);
            var uimo_hook_ptr = uimo_ptr + 5 + uimo_offset;
            MouseoverHook = Services.InteropProvider.HookFromAddress<MouseoverEntityDelegate>(uimo_hook_ptr, OnMouseoverEntityCallback);

            var actionres_offset = Dalamud.Memory.MemoryHelper.Read<int>(actionres_ptr + 1);
            var actionres_fn_ptr = actionres_ptr + 5 + actionres_offset;
            GetActionRowPtr = Marshal.GetDelegateForFunctionPointer<GetActionRowPtrDelegate>(actionres_fn_ptr);

            unsafe {
                TryActionHook = Services.InteropProvider.HookFromAddress<TryActionDelegate>((IntPtr)ActionManager.MemberFunctionPointers.UseAction, TryActionCallback);
                UseAction = Marshal.GetDelegateForFunctionPointer<UseActionDelegate>((IntPtr)ActionManager.MemberFunctionPointers.UseActionLocation);
            }

            // Get Sprint's default ActionCategory
            var srow = GetActionRowPtr(SprintID);
            Dalamud.SafeMemory.Read(srow + 0x20, out SprintActionCategory);
            // Get Potion's default ActionCategory
            var irow = GetActionRowPtr(PotionID);
            Dalamud.SafeMemory.Read(irow + 0x20, out ItemActionCategory);

            UpdateSprintQueueing(Configuration.QueueSprint);
            UpdatePotionQueueing(Configuration.QueuePotions);

            TryActionHook.Enable();
            MouseoverHook.Enable();
        }

        // ActionRow:

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
            if (SprintActionCategory == 0) {
                return;
            }

            var row = GetActionRowPtr(SprintID);
            var type = enable ? AbilityActionCategory : SprintActionCategory;
            Dalamud.SafeMemory.Write(row + 0x20, type);
        }

        public void UpdatePotionQueueing(bool enable) {
            if (ItemActionCategory == 0) {
                return;
            }

            var row = GetActionRowPtr(PotionID);
            var type = enable ? AbilityActionCategory : ItemActionCategory;

            Dalamud.SafeMemory.Write(row + 0x20, type);
        }

        private static bool TryQueueAction(IntPtr action_manager, uint id, uint param, ActionType action_type, ulong target_id) {
            Dalamud.SafeMemory.Read(action_manager + 0x68, out int queue_full);

            if (queue_full > 0) {
                return false;
            }

            Services.PluginLog.Info("Queueing Action");

            // This is how the game queues actions within the "TryAction" function
            // There is no separate function for it, it simply updates variables
            // within the ActionManager during the call

            Dalamud.SafeMemory.Write(action_manager + 0x68, 1);
            Dalamud.SafeMemory.Write(action_manager + 0x6C, (byte)action_type);
            Dalamud.SafeMemory.Write(action_manager + 0x70, id);
            Dalamud.SafeMemory.Write(action_manager + 0x78, target_id);
            Dalamud.SafeMemory.Write(action_manager + 0x80, 0); // "Origin", for whatever reason
            Dalamud.SafeMemory.Write(action_manager + 0x84, param);
            return true;
        }

        private unsafe IGameObject? ResolvePlaceholder(string ph) {
            try {
                var fw = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance();
                var ui = fw->UIModule;
                var pm = ui->GetPronounModule();
                var p = (IntPtr)pm->ResolvePlaceholder(ph, 0, 0);
                return Services.ObjectTable.CreateObjectReference(p);
            } catch (Exception ex){
                Services.PluginLog.Error($"Unable to resolve pronoun ({ph}): {ex.Message}");
                return null;
            }
        }

        public IGameObject? ResolveTarget(string target) {
            return target switch {
                "UI Mouseover" => CurrentUIMouseover,
                "Model Mouseover" => TargetManager.MouseOverTarget,
                "Self" => Services.ClientState.LocalPlayer,
                "Target" => TargetManager.Target,
                "Focus" => TargetManager.FocusTarget,
                "Target of Target" => TargetManager.Target is { } ? TargetManager.Target.TargetObject : null,
                "Soft Target" => TargetManager.SoftTarget,
                "<2>" => ResolvePlaceholder("<2>"),
                "<3>" => ResolvePlaceholder("<3>"),
                "<4>" => ResolvePlaceholder("<4>"),
                "<5>" => ResolvePlaceholder("<5>"),
                "<6>" => ResolvePlaceholder("<6>"),
                "<7>" => ResolvePlaceholder("<7>"),
                "<8>" => ResolvePlaceholder("<8>"),
                _ => null,
            };
        }

        public double DistanceFromPlayer(Vector3 v) {

            var player = Services.ClientState.LocalPlayer;
            if (player is null) {
                return double.PositiveInfinity;
            }

            return Vector3.Distance(player.Position, v);
        }

        private unsafe bool TryActionCallback(IntPtr action_manager, ActionType type, uint id, ulong target, uint param, uint origin, uint unk, Vector3* location) {     
            
            
            // Potion dequeueing
            // This param (-1) picks the item from any available slot, which is the default hotbar method
            if (Configuration.QueuePotions && type == ActionType.Item && origin == 1) {
                param = 65535;
            }

            // Special sprint handling
            if (Configuration.QueueSprint && type == ActionType.GeneralAction && id == 4) {
                return TryActionHook.Original(action_manager, ActionType.Action, SprintID, target, param, origin, unk, location);
            }

            // This is NOT the same classification as the action's ActionCategory
            if (type != ActionType.Action) {
                return TryActionHook.Original(action_manager, type, id, target, param, origin, unk, location);
            }

            // The action row for the originating ID
            var original_row = Actions.GetRow(id);

            // The row should never be null here, unless the function somehow gets a bad ID
            // Regardless, this makes the compiler happy and we can avoid PVP handling at the same time
            if (original_row is null || original_row.IsPvP) {
                return TryActionHook.Original(action_manager, type, id, target, param, origin, unk, location);
            }

            // Macro queueing
            // Known origins : 0 - bar, 1 - queue, 2 - macro
            origin = origin == 2 && Configuration.EnableMacroQueueing ? 0 : origin;

            // Actions placed on bars try to use their base action, so we need to get the upgraded version
            var adjusted_id = ActionManager.MemberFunctionPointers.GetAdjustedActionId((ActionManager*)action_manager, id);

            // The action id to match against what's stored in the user config
            var conf_id = original_row!.RowId;

            // The actual action that will be used
            var adjusted_row = Actions.GetRow(adjusted_id);

            if (adjusted_row is null || !adjusted_row.HasOptionalTargeting()) {
                return TryActionHook.Original(action_manager, type, id, target, param, origin, unk, location);
            }

            // Retain queued actions calculated target
            if (origin == 1) {
                if (adjusted_row.TargetArea && !adjusted_row.IsActionBlocked()) {

                    // Ground targeted actions should not normally reach the queue
                    // Assume cursor placement is intended if no target is specified

                    if(target == DefaultTarget) {
                        Vector3 loc;
                        var success = ActionManager.MemberFunctionPointers.GetGroundPositionForCursor((ActionManager*)action_manager, &loc);
                        if(success) {
                            return GroundActionAtCursor(action_manager, type, id, target, param, origin, unk, &loc);
                        }
                    }
                    else {
                        IGameObject target_obj = Services.ObjectTable.SearchById(target)!;
                        return GroundActionAtTarget(action_manager, type, id, target_obj, param, origin, unk, location);
                    }
                }

                return TryActionHook.Original(action_manager, type, id, target, param, origin, unk, location);
            }

            // Only actions where "IsPlayerAction" is true are allowed into the config
            if (adjusted_row.IsPlayerAction) {
                conf_id = adjusted_row!.RowId;
            }

            if (Configuration.Redirections.ContainsKey(conf_id)) {

                bool suppress_target_ring = false;

                foreach (var t in Configuration.Redirections[conf_id].Priority) {

                    if (t == "Cursor" && adjusted_row.TargetArea) {
                        suppress_target_ring = true;
                        Vector3 loc;
                        var success = ActionManager.MemberFunctionPointers.GetGroundPositionForCursor((ActionManager*)action_manager, &loc);
                        if (success) {
                            return GroundActionAtCursor(action_manager, type, id, target, param, origin, unk, &loc);
                        }
                    }
                    else {
                        IGameObject? nt = ResolveTarget(t);
                        if (nt is not null) {
                            bool ok = adjusted_row.TargetInRangeAndLOS(nt, out var err);
                            bool tt_ok = adjusted_row.TargetTypeValid(nt);
                            if (ok && tt_ok) {
                                if (adjusted_row.TargetArea) {
                                    return GroundActionAtTarget(action_manager, type, id, nt, param, origin, unk, location);
                                }
                                return TryActionHook.Original(action_manager, type, id, nt.GameObjectId, param, origin, unk, location);
                            }
                            else if (!Configuration.IgnoreErrors) {
                                switch (err) {
                                    case 566:
                                        ToastGui.ShowError("Target not in line of sight.");
                                        break;
                                    case 562:
                                        ToastGui.ShowError("Target is not in range.");
                                        break;
                                    default:
                                        ToastGui.ShowError("Invalid target.");
                                        break;
                                }
                                return false;
                            }
                        }
                    }
                }

                if (adjusted_row.TargetArea && suppress_target_ring) {
                    ToastGui.ShowError("Unable to place action at cursor location.");
                    return false;
                }

                return TryActionHook.Original(action_manager, type, id, target, param, origin, unk, location);

            }
            else {
                IGameObject? nt = null;
                var friendly = adjusted_row.CanTargetFriendly();
                var hostile = adjusted_row.CanTargetHostile && !friendly;
                var ground = adjusted_row.TargetArea && !adjusted_row.IsActionBlocked();
                var mo = ground ? Configuration.DefaultMouseoverGround : friendly ? Configuration.DefaultMouseoverFriendly : hostile && Configuration.DefaultMouseoverHostile;
                var model_mo = friendly && mo ? Configuration.DefaultModelMouseoverFriendly : hostile && mo && Configuration.DefaultModelMouseoverHostile;

                if (TargetManager.MouseOverNameplateTarget is not null && mo) {
                    bool ok = adjusted_row.TargetInRangeAndLOS(TargetManager.MouseOverNameplateTarget, out var err);
                    bool tt_ok = adjusted_row.TargetTypeValid(TargetManager.MouseOverNameplateTarget);
                    if (ok && tt_ok) {
                        nt = TargetManager.MouseOverNameplateTarget;
                    }
                    else if (!Configuration.IgnoreErrors) {
                        ToastGui.ShowError(ok ? "Invalid target." : "Target is not in range.");
                        return false;
                    }
                }
                else if (TargetManager.MouseOverTarget is not null && model_mo) {
                    bool ok = adjusted_row.TargetInRangeAndLOS(TargetManager.MouseOverTarget, out var err);
                    bool tt_ok = adjusted_row.TargetTypeValid(TargetManager.MouseOverTarget);
                    if (ok && tt_ok) {
                        nt = TargetManager.MouseOverTarget;
                    }
                    else if (!Configuration.IgnoreErrors) {
                        ToastGui.ShowError(ok ? "Invalid target." : "Target is not in range.");
                        return false;
                    }
                }

                if (nt is not null) {
                    if (adjusted_row.TargetArea) {
                        return GroundActionAtTarget(action_manager, type, id, nt, param, origin, unk, location);
                    }

                    return TryActionHook.Original(action_manager, type, id, nt.GameObjectId, param, origin, unk, location);
                }

                if (Configuration.DefaultCursorMouseover && ground) {
                    Vector3 loc;
                    var success = ActionManager.MemberFunctionPointers.GetGroundPositionForCursor((ActionManager*)action_manager, &loc);
                    if (success) {
                        return GroundActionAtCursor(action_manager, type, id, target, param, origin, unk, &loc);
                    }

                    ToastGui.ShowError("Unable to place action at cursor location.");
                    return false;
                }
            }

            return TryActionHook.Original(action_manager, type, id, target, param, origin, unk, location);
        }

        private unsafe bool GroundActionAtCursor(IntPtr action_manager, ActionType type, uint action, ulong target, uint param, uint origin, uint unk, Vector3* location) {
            var status = ActionManager.MemberFunctionPointers.GetActionStatus((ActionManager*)action_manager, type, action, (uint)target, true, true, null);

            if (status != 0 && status != 0x244) {
                return TryActionHook.Original(action_manager, type, action, target, param, origin, unk, location);
            }

            Dalamud.SafeMemory.Read(action_manager + 0x08, out float animation_timer);
            var action_row = Actions.GetRow(action)!;

            if (status == 0x244 || animation_timer > 0) {
                if (!Configuration.QueueGroundActions || action_row.ActionCategory.Value?.RowId != AbilityActionCategory) {
                    ToastGui.ShowError("Cannot use while casting.");
                    return false;
                }

                return TryQueueAction(action_manager, action, param, type, DefaultTarget);
            }

            var distance = DistanceFromPlayer(*location);
       
            if (distance > action_row.Range) {
                ToastGui.ShowError("Target is not in range.");
                return false;
            }

            return UseAction(action_manager, type, action, target, location, param);
        }

        private unsafe bool GroundActionAtTarget(IntPtr action_manager, ActionType type, uint action, IGameObject target, uint param, uint origin, uint unk, Vector3* location) {


            var status = ActionManager.MemberFunctionPointers.GetActionStatus((ActionManager*)action_manager, type, action, target.GameObjectId, true, true, null);

            if (status != 0 && status != 0x244) {
                return TryActionHook.Original(action_manager, type, action, target.GameObjectId, param, origin, unk, location);
            }

            Dalamud.SafeMemory.Read(action_manager + 0x08, out float animation_timer);
            var action_row = Actions.GetRow(action)!;

            if (status == 0x244 || animation_timer > 0) {

                if (!Configuration.QueueGroundActions || action_row.ActionCategory.Value?.RowId != AbilityActionCategory) {
                    ToastGui.ShowError("Cannot use while casting.");
                    return false;
                }

                return TryQueueAction(action_manager, action, param, type, target.GameObjectId);
            }

            var new_location = target.Position;
            var distance = DistanceFromPlayer(new_location);

            if (distance > action_row.Range) {
                ToastGui.ShowError("Target is not in range.");
                return false;
            }

            return UseAction(action_manager, type, action, target.GameObjectId, &new_location);
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
