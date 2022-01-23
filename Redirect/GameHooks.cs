using System;
using System.Numerics;
using ImGuiNET;
using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Hooking;
using Dalamud.Logging;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.Collections.Generic;

namespace Redirect
{
    internal class GameHooks : IDisposable
    {
        private const string UIMOSignature = "E8 ?? ?? ?? ?? 48 8B 6C 24 58 48 8B 5C 24 50 4C 8B 7C";

        private PartyList PartyMembers => Services.PartyMembers;
        private ClientState ClientState => Services.ClientState;
        private TargetManager TargetManager => Services.TargetManager;
        private SigScanner SigScanner => Services.SigScanner;

        private HashSet<int> Seen = new HashSet<int>();


        // param is the same in both functions,
        // 65535 can be observed for older food,
        // for teleports it is aertheryte ID,
        // generally 0

        private unsafe delegate bool TryAction(IntPtr tp, ActionType t, uint id, ulong target, uint param, uint origin, uint unk, void* l);
        private unsafe delegate bool UseAction(IntPtr tp, ActionType t, uint id, ulong target, Vector3* l, uint param = 0);

        private delegate void MouseoverEntity(IntPtr t, IntPtr entity);

        private volatile GameObject CurrentUIMouseover = null!;

        private Hook<TryAction> TryActionHook = null!;
        private Hook<UseAction> UseActionHook = null!;
        private Hook<MouseoverEntity> MouseoverHook = null!;

        private Configuration Configuration;

        public static void UpdateQueueOptions(bool enable)
        {
            if (enable)
            {
                EnableQueueOptions();
            }
            else
            {
                DisableQueueOptions();
            }
        }

        private static void EnableQueueOptions()
        {
            unsafe
            {
                IntPtr try_action = (IntPtr) ActionManager.fpUseAction;
                Dalamud.SafeMemory.Write<byte>(try_action + 0x17D, 8);
            }
        }

        private static void DisableQueueOptions()
        {
            unsafe
            {
                IntPtr try_action = (IntPtr)ActionManager.fpUseAction;
                Dalamud.SafeMemory.Write<byte>(try_action + 0x17D, 1);
            }
        }


        public GameHooks(Configuration config)
        {
            Configuration = config;

            var uimo_ptr = SigScanner.ScanModule(UIMOSignature);

            if (uimo_ptr == IntPtr.Zero)
            {
                PluginLog.Error("Unable to initialize game hooks");
                return;
            }

            var uimo_offset = Dalamud.Memory.MemoryHelper.Read<int>(uimo_ptr + 1);
            var uimo_hook_ptr = uimo_ptr + 5 + uimo_offset;

            unsafe
            {
                UseActionHook = new Hook<UseAction>((IntPtr) ActionManager.fpUseActionLocation, UseActionCallback);
                TryActionHook = new Hook<TryAction>((IntPtr) ActionManager.fpUseAction, TryActionCallback);
            }

            MouseoverHook = new Hook<MouseoverEntity>(uimo_hook_ptr, OnMouseoverEntityCallback);

            if(Configuration.QueueMoreActions)
            {
                EnableQueueOptions();
            }

            TryActionHook.Enable();
            UseActionHook.Enable();
            MouseoverHook.Enable();
        }

        private GameObject? RedirectTarget(uint action_id, ref bool place_at_cursor)
        {
            if (!Configuration.Redirections.ContainsKey(action_id))
            {
                return null;
            }

            foreach (var t in Configuration.Redirections[action_id].Priority)
            {
                var nt = ResolveTarget(t, ref place_at_cursor);
                if (nt != null)
                {
                    return nt;
                }
            };

            return null;
        }

        private Vector3 ClampCoordinates(Vector3 origin, Vector3 dest, int range)
        {
            if (Vector3.Distance(origin, dest) <= range)
            {
                return dest;
            }
            
            var o = new Vector2(origin.X, origin.Z);
            var d = new Vector2(dest.X, dest.Z);
            var n = Vector2.Normalize(d - o);
            var t = o + (n * range);
            return new Vector3(t.X, dest.Y, t.Y);
        }

        private unsafe bool TryActionCallback(IntPtr this_ptr, ActionType action_type, uint id, ulong target, uint param, uint origin, uint unk, void* location)
        {   
            // This allows sprint to queue
            
            if(Configuration.QueueMoreActions && action_type == ActionType.General && id == 4)
            {
                id = 3;
                action_type = ActionType.Spell;
            }

            // Ignore all handling if it's not a spell

            if (action_type != ActionType.Spell)
            {
                return TryActionHook.Original(this_ptr, action_type, id, target, param, origin, unk, location);
            }

            // Macro queueing
            
            origin = origin == 2 && Configuration.EnableMacroQueueing ? 0 : origin;

            var adj_id = id;
            var temp_id = ActionManager.fpGetAdjustedActionId(ActionManager.Instance(), id);
            var temp_res = Actions.GetRow(temp_id);
            if(temp_res != null && temp_res.IsPlayerAction)
            {
                adj_id = temp_id;
            }

            bool place_at_cursor = false;
            var new_target = RedirectTarget(adj_id, ref place_at_cursor);

            if (place_at_cursor)
            {
                var res = Actions.GetRow(adj_id)!;
                var success = Services.GameGui.ScreenToWorld(ImGui.GetMousePos(), out var game_coords);

                var new_location = ClampCoordinates(ClientState.LocalPlayer!.Position, game_coords, res.Range);

                if (ActionManager.fpIsRecastTimerActive(ActionManager.Instance(), action_type, adj_id) > 0)
                {
                    return TryActionHook.Original(this_ptr, action_type, id, target, param, origin, unk, location);
                }

                uint status = ActionManager.fpGetActionStatus((ActionManager*)this_ptr, action_type, id, (uint) target, 1, 1);

                if(status == 0)
                {
                    return UseActionHook.Original(this_ptr, action_type, id, target, &new_location, param);
                }

                return TryActionHook.Original(this_ptr, action_type, id, target, param, origin, unk, location);
            }

            if (new_target != null)
            {
                var res = Actions.GetRow(adj_id)!;
                if (res.TargetArea)
                {
                    var new_location = new_target.Position;
                    if (ActionManager.fpIsRecastTimerActive(ActionManager.Instance(), action_type, adj_id) > 0)
                    {
                        return TryActionHook.Original(this_ptr, action_type, id, target, param, origin, unk, location);
                    }

                    uint status = ActionManager.fpGetActionStatus((ActionManager*)this_ptr, action_type, id, (uint)target, 1, 1);

                    if (status == 0)
                    {
                        return UseActionHook.Original(this_ptr, action_type, id, new_target.ObjectId, &new_location);
                    }
                } 

                return TryActionHook.Original(this_ptr, action_type, id, new_target.ObjectId, param, origin, unk, location);
            }

            return TryActionHook.Original(this_ptr, action_type, id, target, param, origin, unk, location);
        }
        private unsafe bool UseActionCallback(IntPtr this_ptr, ActionType action_type, uint id, ulong target, Vector3* location, uint unk)
        {
            return UseActionHook.Original(this_ptr, action_type, id, target, location, unk);
        }

        private void OnMouseoverEntityCallback(IntPtr this_ptr, IntPtr entity)
        {
            MouseoverHook.Original(this_ptr, entity);

            if (entity == IntPtr.Zero)
            {
                CurrentUIMouseover = null!;
            } 
            else
            {
                CurrentUIMouseover = Services.ObjectTable.CreateObjectReference(entity)!;
            }
        }

        public GameObject? ResolveTarget(string target, ref bool place_at_cursor)
        {
            switch(target)
            {
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

        public void Dispose()
        {
            TryActionHook?.Dispose();
            UseActionHook?.Dispose();
            MouseoverHook?.Dispose();

            DisableQueueOptions();
        }
    }
}
