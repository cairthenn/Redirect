using Dalamud.Game;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace Redirect {
    internal class GameHooks : IDisposable {

        private const uint DefaultTarget = 0xE0000000;
        private Configuration Configuration { get; } = null!;
        private Actions Actions { get; } = null!;
        private static ITargetManager TargetManager => Services.TargetManager;
        private static IToastGui ToastGui => Services.ToastGui;

        private unsafe delegate bool TryActionDelegate(IntPtr tp, ActionType t, uint id, ulong target, uint param, uint origin, uint unk, Vector3* l);
        private unsafe delegate bool UseActionDelegate(IntPtr tp, ActionType t, uint id, ulong target, Vector3* l, uint param = 0);
        private delegate void MouseoverEntityDelegate(IntPtr t, IntPtr entity);
        private readonly Hook<TryActionDelegate> UseActionHook = null!;
        private readonly UseActionDelegate UseAction = null!;

        private const byte AbilityActionCategory = 4;

        public GameHooks(Configuration config, Actions actions) {
            Configuration = config;
            Actions = actions;

            unsafe {
                UseActionHook = Services.InteropProvider.HookFromAddress<TryActionDelegate>((IntPtr)ActionManager.MemberFunctionPointers.UseAction, UseActionCallback);
                UseAction = Marshal.GetDelegateForFunctionPointer<UseActionDelegate>((IntPtr)ActionManager.MemberFunctionPointers.UseActionLocation);
            }

            UseActionHook.Enable();
        }


        private static bool TryQueueAction(IntPtr actManager, uint id, uint param, ActionType actType, ulong targetId) {
            Dalamud.SafeMemory.Read(actManager + 0x68, out int queueFull);

            if (queueFull > 0) {
                return false;
            }

            // This is how the game queues actions within the "UseAction" function
            // There is no separate function for it, it simply updates variables
            // within the ActionManager during the call

            Dalamud.SafeMemory.Write(actManager + 0x68, 1);
            Dalamud.SafeMemory.Write(actManager + 0x6C, (byte) actType);
            Dalamud.SafeMemory.Write(actManager + 0x70, id);
            Dalamud.SafeMemory.Write(actManager + 0x78, targetId);
            Dalamud.SafeMemory.Write(actManager + 0x80, 0); // "Origin", for whatever reason
            Dalamud.SafeMemory.Write(actManager + 0x84, param);
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

        private unsafe IGameObject? GetCurrentUIMouseover() {
            var pm = PronounModule.Instance();
            if (pm is null) {
                return null;
            }
            var obj = (IntPtr)pm->UiMouseOverTarget;
            return Services.ObjectTable.CreateObjectReference(obj);
        }

        public unsafe IGameObject? ResolveTarget(string target) {
            return target switch {
                "UI Mouseover" => GetCurrentUIMouseover(),
                "Model Mouseover" => TargetManager.MouseOverTarget,
                "Self" => Services.ObjectTable.LocalPlayer,
                "Target" => TargetManager.Target,
                "Focus" => TargetManager.FocusTarget,
                "Target of Target" => TargetManager.Target is { } ? TargetManager.Target.TargetObject : null,
                "Soft Target" => TargetManager.SoftTarget,
                "Chocobo" => ResolvePlaceholder("<b>"),
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

        public static float DistanceFromPlayer(Vector3 v) {

            var player = Services.ObjectTable.LocalPlayer;
            if (player is null) {
                return float.PositiveInfinity;
            }

            return Vector3.Distance(player.Position, v);
        }

        private unsafe bool UseActionCallback(IntPtr actManager, ActionType type, uint id, ulong target, uint param, uint origin, uint unk, Vector3* location) {     

            // This is NOT the same classification as the action's ActionCategory
            if (type != ActionType.Action) {
                return UseActionHook.Original(actManager, type, id, target, param, origin, unk, location);
            }

            // The action row for the originating ID
            var ogRow = Actions.GetRow(id);

            if (ogRow.IsPvP) {
                return UseActionHook.Original(actManager, type, id, target, param, origin, unk, location);
            }

            // Macro queueing
            // Known origins : 0 - bar, 1 - queue, 2 - macro
            origin = origin == 2 && Configuration.EnableMacroQueueing ? 0 : origin;

            // Actions placed on bars try to use their base action, so we need to get the upgraded version
            var adjustedId = ActionManager.MemberFunctionPointers.GetAdjustedActionId((ActionManager*)actManager, id);

            // The action id to match against what's stored in the user config
            var configurationId = ogRow.RowId;

            // The actual action that will be used
            var adjustedRow = Actions.GetRow(adjustedId);

            if (!adjustedRow.HasOptionalTargeting()) {
                return UseActionHook.Original(actManager, type, id, target, param, origin, unk, location);
            }

            // Retain queued actions calculated target
            if (origin == 1) {
                if (adjustedRow.TargetArea && !adjustedRow.IsGroundActionBlocked()) {

                    // Ground targeted actions should not normally reach the queue
                    // Assume cursor placement is intended if no target is specified

                    if(target == DefaultTarget) {
                        Vector3 loc;
                        var success = ActionManager.MemberFunctionPointers.GetGroundPositionForCursor((ActionManager*)actManager, &loc);
                        if(success) {
                            return GroundActionAtCursor(actManager, type, id, target, param, origin, unk, &loc);
                        }
                    }
                    else {
                        IGameObject targetObj = Services.ObjectTable.SearchById(target)!;
                        return GroundActionAtTarget(actManager, type, id, targetObj, param, origin, unk, location);
                    }
                }

                return UseActionHook.Original(actManager, type, id, target, param, origin, unk, location);
            }

            // Only actions where "IsPlayerAction" is true are allowed into the config
            if (adjustedRow.IsPlayerAction) {
                configurationId = adjustedRow!.RowId;
            }

            if (Configuration.Redirections.TryGetValue(configurationId, out Redirection? value)) {

                bool suppressRing = false;

                foreach (var t in value.Priority) {

                    if (t == "Cursor" && adjustedRow.TargetArea) {
                        suppressRing = true;
                        Vector3 loc;
                        var success = ActionManager.MemberFunctionPointers.GetGroundPositionForCursor((ActionManager*)actManager, &loc);
                        if (success) {
                            return GroundActionAtCursor(actManager, type, id, target, param, origin, unk, &loc);
                        }
                    }
                    else {
                        IGameObject? nt = ResolveTarget(t);
                        if (nt is not null) {
                            bool rangeOk = adjustedRow.TargetInRangeAndLOS(nt, out var err);
                            bool typeOk = adjustedRow.TargetTypeValid(nt);
                            if (rangeOk && typeOk) {
                                if (adjustedRow.TargetArea) {
                                    return GroundActionAtTarget(actManager, type, id, nt, param, origin, unk, location);
                                }
                                return UseActionHook.Original(actManager, type, id, nt.GameObjectId, param, origin, unk, location);
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

                if (adjustedRow.TargetArea && suppressRing) {
                    ToastGui.ShowError("Invalid target.");
                    return false;
                }

                return UseActionHook.Original(actManager, type, id, target, param, origin, unk, location);

            }
            else {
                IGameObject? nt = null;
                var friendly = adjustedRow.CanTargetFriendly();
                var hostile = adjustedRow.CanTargetHostile && !friendly;
                var ground = adjustedRow.TargetArea && !adjustedRow.IsGroundActionBlocked();
                var mo = ground ? Configuration.DefaultMouseoverGround : friendly ? Configuration.DefaultMouseoverFriendly : hostile && Configuration.DefaultMouseoverHostile;
                var modelMO = friendly && mo ? Configuration.DefaultModelMouseoverFriendly : hostile && mo && Configuration.DefaultModelMouseoverHostile;
                var currentMO = GetCurrentUIMouseover();
                if (currentMO is not null && mo) {
                    bool rangeOk = adjustedRow.TargetInRangeAndLOS(currentMO, out _);
                    bool typeOk = adjustedRow.TargetTypeValid(currentMO);
                    if (rangeOk && typeOk) {
                        nt = currentMO;
                    }
                    else if (!Configuration.IgnoreErrors) {
                        ToastGui.ShowError(rangeOk ? "Invalid target." : "Target is not in range.");
                        return false;
                    }
                }
                else if (TargetManager.MouseOverTarget is not null && modelMO) {
                    bool rangeOk = adjustedRow.TargetInRangeAndLOS(TargetManager.MouseOverTarget, out _);
                    bool typeOk = adjustedRow.TargetTypeValid(TargetManager.MouseOverTarget);
                    if (rangeOk && typeOk) {
                        nt = TargetManager.MouseOverTarget;
                    }
                    else if (!Configuration.IgnoreErrors) {
                        ToastGui.ShowError(rangeOk ? "Invalid target." : "Target is not in range.");
                        return false;
                    }
                }

                if (nt is not null) {
                    if (adjustedRow.TargetArea) {
                        return GroundActionAtTarget(actManager, type, id, nt, param, origin, unk, location);
                    }

                    return UseActionHook.Original(actManager, type, id, nt.GameObjectId, param, origin, unk, location);
                }

                if (Configuration.DefaultCursorMouseover && ground) {
                    Vector3 loc;
                    var success = ActionManager.MemberFunctionPointers.GetGroundPositionForCursor((ActionManager*)actManager, &loc);
                    if (success) {
                        return GroundActionAtCursor(actManager, type, id, target, param, origin, unk, &loc);
                    }

                    ToastGui.ShowError("Invalid target.");
                    return false;
                }
            }

            return UseActionHook.Original(actManager, type, id, target, param, origin, unk, location);
        }

        private unsafe bool GroundActionAtCursor(IntPtr actManager, ActionType type, uint action, ulong target, uint param, uint origin, uint unk, Vector3* location) {
            var status = ActionManager.MemberFunctionPointers.GetActionStatus((ActionManager*)actManager, type, action, (uint)target, true, true, null);

            if (status != 0 && status != 0x244) {
                return UseActionHook.Original(actManager, type, action, target, param, origin, unk, location);
            }

            Dalamud.SafeMemory.Read(actManager + 0x08, out float animationTime);
            var actRow = Actions.GetRow(action)!;

            if (status == 0x244 || animationTime > 0) {
                if (!Configuration.QueueGroundActions || actRow.ActionCategory.Value.RowId != AbilityActionCategory) {
                    ToastGui.ShowError("Cannot use while casting.");
                    return false;
                }

                return TryQueueAction(actManager, action, param, type, DefaultTarget);
            }

            var distance = DistanceFromPlayer(*location);
       
            if (distance > actRow.Range) {
                ToastGui.ShowError("Target is not in range.");
                return false;
            }

            return UseAction(actManager, type, action, target, location, param);
        }

        private unsafe bool GroundActionAtTarget(IntPtr actManager, ActionType type, uint action, IGameObject target, uint param, uint origin, uint unk, Vector3* location) {


            var status = ActionManager.MemberFunctionPointers.GetActionStatus((ActionManager*)actManager, type, action, target.GameObjectId, true, true, null);

            if (status != 0 && status != 0x244) {
                return UseActionHook.Original(actManager, type, action, target.GameObjectId, param, origin, unk, location);
            }

            Dalamud.SafeMemory.Read(actManager + 0x08, out float animationTime);
            var actRow = Actions.GetRow(action)!;

            if (status == 0x244 || animationTime > 0) {

                if (!Configuration.QueueGroundActions || actRow.ActionCategory.Value.RowId != AbilityActionCategory) {
                    ToastGui.ShowError("Cannot use while casting.");
                    return false;
                }

                return TryQueueAction(actManager, action, param, type, target.GameObjectId);
            }

            var newLoc = target.Position;
            var distance = DistanceFromPlayer(newLoc);

            if (distance > actRow.Range) {
                ToastGui.ShowError("Target is not in range.");
                return false;
            }

            return UseAction(actManager, type, action, target.GameObjectId, &newLoc);
        }

        public void Dispose() {
            UseActionHook?.Dispose();
        }
    }
}
