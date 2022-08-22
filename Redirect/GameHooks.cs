using Dalamud.Game;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Gui.Toast;
using Dalamud.Hooking;
using Dalamud.Logging;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Redirect {
    internal class GameHooks : IDisposable {

        internal enum CursorStatus {
            OK, RANGE, INVALID
        }

        private static readonly Dictionary<CursorStatus, string> CursorErrorToasts = new() {
            { CursorStatus.OK, "Something has gone terribly wrong." },
            { CursorStatus.RANGE, "Target is not in range." },
            { CursorStatus.INVALID, "Invalid target." },
        };

        private const string UIMOSig = "E8 ?? ?? ?? ?? 48 8B 6C 24 58 48 8B 5C 24 50 4C 8B 7C";
        private const string ActionResourceSig = "E8 ?? ?? ?? ?? 4C 8B E8 48 85 C0 0F 84 ?? ?? ?? ?? 41 83 FE 04";
        private const string GroundActionCheckSig = "E8 ?? ?? ?? ?? 44 8B 83 ?? ?? ?? ?? 4C 8D 4C 24 60";
        private const string GetGroundPlacementSig = "E8 ?? ?? ?? ?? 84 C0 0F 84 ?? ?? ?? ?? 4C 8B C3 48 8D 54";

        private Configuration Configuration { get; } = null!;
        private Actions Actions { get; } = null!;
        private static TargetManager TargetManager => Services.TargetManager;
        private static SigScanner SigScanner => Services.SigScanner;
        private static ToastGui ToastGui => Services.ToastGui;

        // param is the same in both functions,
        // 65535 can be observed for older food,
        // for teleports it is aertheryte ID,
        // generally 0

        private unsafe delegate bool TryActionDelegate(IntPtr tp, ActionType t, uint id, ulong target, uint param, uint origin, uint unk, void* l);
        private unsafe delegate bool UseActionDelegate(IntPtr tp, ActionType t, uint id, ulong target, Vector3* l, uint param = 0);
        private unsafe delegate void GroundActionValidDelegate(IntPtr tp, uint id, ActionType t, bool* results);
        private unsafe delegate bool GetGroundPlacementDelegate(IntPtr tp, uint id, ActionType t, float* where, ulong* results);
        private delegate IntPtr GetActionRowPtrDelegate(int id);
        private delegate void MouseoverEntityDelegate(IntPtr t, IntPtr entity);
        private readonly Hook<TryActionDelegate> TryActionHook = null!;
        private readonly Hook<MouseoverEntityDelegate> MouseoverHook = null!;
        private readonly UseActionDelegate UseAction = null!;
        private readonly GetActionRowPtrDelegate GetActionRowPtr = null!;
        private readonly GroundActionValidDelegate GroundActionValid = null!;
        private readonly GetGroundPlacementDelegate GetGroundPlacement = null!;
        private volatile GameObject? CurrentUIMouseover = null!;

        public GameHooks(Configuration config, Actions actions) {
            Configuration = config;
            Actions = actions;

            var uimo_ptr = SigScanner.ScanModule(UIMOSig);
            var actionres_ptr = SigScanner.ScanModule(ActionResourceSig);
            var groundaction_ptr = SigScanner.ScanModule(GroundActionCheckSig);
            var gp_ptr = SigScanner.ScanModule(GetGroundPlacementSig);

            if (uimo_ptr == IntPtr.Zero || actionres_ptr == IntPtr.Zero || groundaction_ptr == IntPtr.Zero || gp_ptr == IntPtr.Zero) {
                PluginLog.Error("Error during game hook initialization, plugin functionality is disabled.");
                return;
            }

            var actionres_offset = Dalamud.Memory.MemoryHelper.Read<int>(actionres_ptr + 1);
            var actionres_fn_ptr = actionres_ptr + 5 + actionres_offset;
            GetActionRowPtr = Marshal.GetDelegateForFunctionPointer<GetActionRowPtrDelegate>(actionres_fn_ptr);

            var groundaction_offset = Dalamud.Memory.MemoryHelper.Read<int>(groundaction_ptr + 1);
            var groundaction_fn_ptr = groundaction_ptr + 5 + groundaction_offset;
            GroundActionValid = Marshal.GetDelegateForFunctionPointer<GroundActionValidDelegate>(groundaction_fn_ptr);


            var gp_offset = Dalamud.Memory.MemoryHelper.Read<int>(gp_ptr + 1);
            var gp_fn_ptr = gp_ptr + 5 + gp_offset;
            GetGroundPlacement = Marshal.GetDelegateForFunctionPointer<GetGroundPlacementDelegate>(gp_fn_ptr);

            var uimo_offset = Dalamud.Memory.MemoryHelper.Read<int>(uimo_ptr + 1);
            var uimo_hook_ptr = uimo_ptr + 5 + uimo_offset;
            MouseoverHook = Hook<MouseoverEntityDelegate>.FromAddress(uimo_hook_ptr, OnMouseoverEntityCallback);

            unsafe {
                TryActionHook = Hook<TryActionDelegate>.FromAddress((IntPtr)ActionManager.fpUseAction, TryActionCallback);
                UseAction = Marshal.GetDelegateForFunctionPointer<UseActionDelegate>((IntPtr)ActionManager.fpUseActionLocation);
            }

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
            var row = GetActionRowPtr(3);
            var type = enable ? ActionType.Ability : ActionType.MainCommand;
            Dalamud.SafeMemory.Write(row + 0x20, (byte)type);
        }

        public void UpdatePotionQueueing(bool enable) {
            var row = GetActionRowPtr(846);
            var type = enable ? ActionType.Ability : ActionType.General;
            Dalamud.SafeMemory.Write(row + 0x20, (byte)type);
        }

        private static bool TryQueueAction(IntPtr action_manager, uint id, uint param, ActionType action_type, ulong target_id) {
            Dalamud.SafeMemory.Read(action_manager + 0x68, out int queue_full);

            if (queue_full > 0) {
                return false;
            }

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

        private CursorStatus ValidateCursorPlacement(IntPtr action_manager, Lumina.Excel.GeneratedSheets.Action action) {
            bool[] results = new bool[4];
            unsafe {
                fixed (bool* p = results) {
                    GroundActionValid(action_manager, action.RowId, ActionType.Spell, p);
                }

                if (results[0] && results[1]) {
                    return CursorStatus.OK;
                }
                else if (results[2]) {
                    return CursorStatus.RANGE;
                }

                return CursorStatus.INVALID;
            }
        }

        private unsafe GameObject? ResolvePlaceholder(string ph) {
            try {
                var fw = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance();
                var ui = fw->GetUiModule();
                var pm = ui->GetPronounModule();
                var p = (IntPtr)pm->ResolvePlaceholder(ph, 0, 0);
                return Services.ObjectTable.CreateObjectReference(p);
            } catch (Exception ex){
                PluginLog.Error($"Unable to resolve pronoun ({ph}): {ex.Message}");
                return null;
            }
        }

        public GameObject? ResolveTarget(string target) {
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

        private unsafe bool TryActionCallback(IntPtr action_manager, ActionType type, uint id, ulong target, uint param, uint origin, uint unk, void* location) {

            // Potion dequeueing
            // This picks the item from any available slot, which is the default hotbar method
            if (Configuration.QueuePotions && type == ActionType.Item && origin == 1) {
                param = 65535;
            }

            // Special sprint handling
            if (Configuration.QueueSprint && type == ActionType.General && id == 4) {
                return TryActionHook.Original(action_manager, ActionType.Spell, 3, target, param, origin, unk, location);
            }

            // This is NOT the same classification as the item's resource, but a more generic version
            // Every spell, ability, and weaponskill (even sprint, which is a "main command"), gets called with
            // the designation of "Spell" (1). Thus, we can avoid most tomfoolery by returning early
            if (type != ActionType.Spell) {
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
            var adjusted_id = ActionManager.fpGetAdjustedActionId((ActionManager*)action_manager, id);

            // The action id to match against what's stored in the user config
            var conf_id = original_row!.RowId;

            // The actual action that will be used
            var adjusted_row = Actions.GetRow(adjusted_id);

            if (adjusted_row is null || !adjusted_row.HasOptionalTargeting()) {
                return TryActionHook.Original(action_manager, type, id, target, param, origin, unk, location);
            }

            // Only actions where "IsPlayerAction" is true are allowed into the config
            if (adjusted_row.IsPlayerAction) {
                conf_id = adjusted_row!.RowId;
            }

            if (Configuration.Redirections.ContainsKey(conf_id)) {

                CursorStatus cursor_status = CursorStatus.INVALID;
                bool suppress_target_ring = false;

                foreach (var t in Configuration.Redirections[conf_id].Priority) {

                    if (t == "Cursor" && adjusted_row.TargetArea) {
                        suppress_target_ring = true;
                        cursor_status = ValidateCursorPlacement(action_manager, adjusted_row);
                        if (cursor_status == CursorStatus.OK) {
                            return GroundActionAtCursor(action_manager, type, id, target, param, origin, unk, location);
                        }
                    }
                    else {
                        GameObject? nt = ResolveTarget(t);
                        if (nt is not null) {
                            bool ok = adjusted_row.TargetInRangeAndLOS(nt, out var err);
                            bool tt_ok = adjusted_row.TargetTypeValid(nt);
                            if (ok && tt_ok) {
                                if (adjusted_row.TargetArea) {
                                    return GroundActionAtTarget(action_manager, type, id, nt, param, origin, unk, location);
                                }
                                return TryActionHook.Original(action_manager, type, id, nt.ObjectId, param, origin, unk, location);
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
                    ToastGui.ShowError(CursorErrorToasts[cursor_status]);
                    return false;
                }

                return TryActionHook.Original(action_manager, type, id, target, param, origin, unk, location);

            }
            else {
                GameObject? nt = null;
                var friendly = adjusted_row.CanTargetFriendly();
                var hostile = adjusted_row.CanTargetHostile && !friendly;
                var ground = adjusted_row.TargetArea && !adjusted_row.IsActionBlocked();
                var mo = ground ? Configuration.DefaultMouseoverGround : friendly ? Configuration.DefaultMouseoverFriendly : hostile && Configuration.DefaultMouseoverHostile;
                var model_mo = friendly && mo ? Configuration.DefaultModelMouseoverFriendly : hostile && mo && Configuration.DefaultModelMouseoverHostile;

                if (CurrentUIMouseover is not null && mo) {
                    bool ok = adjusted_row.TargetInRangeAndLOS(CurrentUIMouseover, out var err);
                    bool tt_ok = adjusted_row.TargetTypeValid(CurrentUIMouseover);
                    if (ok && tt_ok) {
                        nt = CurrentUIMouseover;
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

                    return TryActionHook.Original(action_manager, type, id, nt.ObjectId, param, origin, unk, location);
                }

                if (Configuration.DefaultCursorMouseover && ground) {
                    CursorStatus cs = ValidateCursorPlacement(action_manager, adjusted_row);
                    if (cs == CursorStatus.OK) {
                        return GroundActionAtCursor(action_manager, type, id, target, param, origin, unk, location);
                    }
                    else {
                        ToastGui.ShowError(CursorErrorToasts[cs]);
                        return false;
                    }
                }
            }

            return TryActionHook.Original(action_manager, type, id, target, param, origin, unk, location);
        }

        private unsafe bool GroundActionAtCursor(IntPtr action_manager, ActionType type, uint id, ulong target, uint param, uint origin, uint unk, void* location) {
            var status = ActionManager.fpGetActionStatus((ActionManager*)action_manager, type, id, (uint)target, 1, 1);

            if (status != 0 && status != 0x244) {
                return TryActionHook.Original(action_manager, type, id, target, param, origin, unk, location);
            }

            Dalamud.SafeMemory.Read(action_manager + 0x08, out float animation_timer);

            if (status == 0x244 || animation_timer > 0) {
                if (!Configuration.QueueGroundActions) {
                    ToastGui.ShowError("Cannot use while casting.");
                    return false;
                }

                return TryQueueAction(action_manager, id, param, type, target);
            }

            float[] loc = new float[6];
            ulong results = 1;
            fixed (float* p = loc) {
                bool success = GetGroundPlacement(action_manager, id, type, p, &results);
            }

            Vector3 v = new(loc[0], loc[1], loc[2]);

            return UseAction(action_manager, type, id, target, &v, param);
        }

        private unsafe bool GroundActionAtTarget(IntPtr action_manager, ActionType type, uint action, GameObject target, uint param, uint origin, uint unk, void* location) {

            var status = ActionManager.fpGetActionStatus((ActionManager*)action_manager, type, action, target.ObjectId, 1, 1);

            if (status != 0 && status != 0x244) {
                return TryActionHook.Original(action_manager, type, action, target.ObjectId, param, origin, unk, location);
            }

            Dalamud.SafeMemory.Read(action_manager + 0x08, out float animation_timer);

            if (status == 0x244 || animation_timer > 0) {
                if (!Configuration.QueueGroundActions) {
                    ToastGui.ShowError("Cannot use while casting.");
                    return false;
                }

                return TryQueueAction(action_manager, action, param, type, target.ObjectId);
            }

            var new_location = target.Position;

            return UseAction(action_manager, type, action, target.ObjectId, &new_location);
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
