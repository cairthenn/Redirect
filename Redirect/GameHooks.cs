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
using System.Runtime.InteropServices;

namespace Redirect
{
    internal class GameHooks : IDisposable
    {

        private const string UIMOSignature = "E8 ?? ?? ?? ?? 48 8B 6C 24 58 48 8B 5C 24 50 4C 8B 7C";
        private const string ActionResourceSig = "E8 ?? ?? ?? ?? 4C 8B E8 48 85 C0 0F 84 ?? ?? ?? ?? 41 83 FE 04";

        private Configuration Configuration;

        private PartyList PartyMembers => Services.PartyMembers;
        private ClientState ClientState => Services.ClientState;
        private TargetManager TargetManager => Services.TargetManager;
        private SigScanner SigScanner => Services.SigScanner;

        private HashSet<int> Seen = new HashSet<int>();

        // param is the same in both functions,
        // 65535 can be observed for older food,
        // for teleports it is aertheryte ID,
        // generally 0

        private unsafe delegate bool TryActionDelegate(IntPtr tp, ActionType t, uint id, ulong target, uint param, uint origin, uint unk, void* l);
        private unsafe delegate bool UseActionDelegate(IntPtr tp, ActionType t, uint id, ulong target, Vector3* l, uint param = 0);
        private delegate IntPtr GetActionResourceDelegate(int id);
        private delegate void MouseoverEntityDelegate(IntPtr t, IntPtr entity);


        private Hook<TryActionDelegate> TryActionHook = null!;
        private Hook<MouseoverEntityDelegate> MouseoverHook = null!;
        private UseActionDelegate UseAction = null!;
        private GetActionResourceDelegate GetActionResource = null!;

        private volatile GameObject CurrentUIMouseover = null!;

        public GameHooks(Configuration config)
        {
            Configuration = config;

            var uimo_ptr = SigScanner.ScanModule(UIMOSignature);
            var actionres_ptr = Services.SigScanner.ScanModule(ActionResourceSig);

            if (uimo_ptr == IntPtr.Zero || actionres_ptr == IntPtr.Zero)
            {
                PluginLog.Error("Error during game hook initialization, plugin functionality is disabled.");
                return;
            }

            var actionres_offset = Dalamud.Memory.MemoryHelper.Read<int>(actionres_ptr + 1);
            var actionres_fn_ptr = actionres_ptr + 5 + actionres_offset;
            GetActionResource = Marshal.GetDelegateForFunctionPointer<GetActionResourceDelegate>(actionres_fn_ptr);

            var uimo_offset = Dalamud.Memory.MemoryHelper.Read<int>(uimo_ptr + 1);
            var uimo_hook_ptr = uimo_ptr + 5 + uimo_offset;
            MouseoverHook = new Hook<MouseoverEntityDelegate>(uimo_hook_ptr, OnMouseoverEntityCallback);

            unsafe
            {
                TryActionHook = new Hook<TryActionDelegate>((IntPtr) ActionManager.fpUseAction, TryActionCallback);
                UseAction = Marshal.GetDelegateForFunctionPointer<UseActionDelegate>((IntPtr) ActionManager.fpUseActionLocation);
            }

            UpdateSprintQueueing(Configuration.QueueSprint);

            TryActionHook.Enable();
            MouseoverHook.Enable();
        }

        public void UpdateSprintQueueing(bool enable)
        {
            if(enable)
            {
                var res = GetActionResource(3);
                Dalamud.SafeMemory.Write(res + 0x20, (byte) ActionType.Ability);
            }
            else
            {
                var res = GetActionResource(3);
                Dalamud.SafeMemory.Write(res + 0x20, (byte) ActionType.MainCommand);
            }
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

        public GameObject? ResolveTarget(string target, ref bool place_at_cursor)
        {
            switch (target)
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

        private unsafe bool TryActionCallback(IntPtr this_ptr, ActionType action_type, uint id, ulong target, uint param, uint origin, uint unk, void* location)
        {
            
            // Special sprint handling
            
            if(Configuration.QueueSprint && (action_type == ActionType.General && id == 4))
            {
                return TryActionHook.Original(this_ptr, ActionType.Spell, 3, target, param, origin, unk, location);
            }


            // Ignore all handling if it's not a spell

            if (action_type != ActionType.Spell)
            {
                return TryActionHook.Original(this_ptr, action_type, id, target, param, origin, unk, location);
            }

            // Macro queueing
            
            origin = origin == 2 && Configuration.EnableMacroQueueing ? 0 : origin;

            // Actions placed on bars try to use their base action, so we need to get the upgraded version

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

                var status = ActionManager.fpGetActionStatus((ActionManager*) this_ptr, action_type, id, (uint) target, 1, 1);

                if (status != 0 && status != 0x244)
                {
                    return TryActionHook.Original(this_ptr, action_type, id, target, param, origin, unk, location);
                }

                UseAction(this_ptr, action_type, id, target, &new_location, param);
                return true;
            }

            if (new_target != null)
            {
                var res = Actions.GetRow(adj_id)!;
                if (res.TargetArea)
                {
                    var new_location = new_target.Position;

                    var status = ActionManager.fpGetActionStatus((ActionManager*) this_ptr, action_type, id, (uint) target, 1, 1);
                    
                    if (status != 0 && status != 0x244)
                    {
                        return TryActionHook.Original(this_ptr, action_type, id, target, param, origin, unk, location);
                    }

                    UseAction(this_ptr, action_type, id, new_target.ObjectId, &new_location);
                    return true;
                } 

                return TryActionHook.Original(this_ptr, action_type, id, new_target.ObjectId, param, origin, unk, location);
            }

            return TryActionHook.Original(this_ptr, action_type, id, target, param, origin, unk, location);
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

        public void Dispose()
        {
            TryActionHook?.Dispose();
            MouseoverHook?.Dispose();
            UpdateSprintQueueing(false);
        }
    }
}
