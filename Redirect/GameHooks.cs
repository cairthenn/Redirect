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

namespace Redirect
{
    internal class GameHooks : IDisposable
    {
        private const string TryActionSignature = "E8 ?? ?? ?? ?? EB 64 B1 01";
        private const string UseActionSignature = "E8 ?? ?? ?? ?? 3C 01 0F 85 ?? ?? ?? ?? EB 46";
        private const string UIMOSignature = "E8 ?? ?? ?? ?? 48 8B 6C 24 58 48 8B 5C 24 50 4C 8B 7C";

        private PartyList PartyMembers => Services.PartyMembers;
        private ClientState ClientState => Services.ClientState;
        private TargetManager TargetManager => Services.TargetManager;
        private SigScanner SigScanner => Services.SigScanner;

        private delegate bool TryAction(IntPtr tp, ActionType t, uint id, uint target, uint p4, uint p5, uint p6, ref Vector3 l);
        private delegate bool UseAction(IntPtr tp, ActionType t, uint id, uint target, ref Vector3 l, uint p5 = 0);

        private delegate void MouseoverEntity(IntPtr t, IntPtr entity);

        private volatile GameObject CurrentUIMouseover = null!;

        private Hook<TryAction> TryActionHook = null!;
        private Hook<UseAction> UseActionHook = null!;
        private Hook<MouseoverEntity> MouseoverHook = null!;

        private Configuration Configuration;


        public GameHooks(Configuration config)
        {
            Configuration = config;
            var tryaction_ptr = SigScanner.ScanModule(TryActionSignature);
            var useaction_ptr = SigScanner.ScanModule(UseActionSignature);
            var uimo_ptr = SigScanner.ScanModule(UIMOSignature);

            if (tryaction_ptr == IntPtr.Zero || uimo_ptr == IntPtr.Zero || useaction_ptr == IntPtr.Zero)
            {
                PluginLog.Error("Unable to initialize game hooks");
                return;
            }

            var tryaction_offset = Dalamud.Memory.MemoryHelper.Read<int>(tryaction_ptr + 1);
            var tryaction_hook_ptr = tryaction_ptr + 5 + tryaction_offset;

            var useaction_offset = Dalamud.Memory.MemoryHelper.Read<int>(useaction_ptr + 1);
            var useaction_hook_ptr = useaction_ptr + 5 + useaction_offset;

            var uimo_offset = Dalamud.Memory.MemoryHelper.Read<int>(uimo_ptr + 1);
            var uimo_hook_ptr = uimo_ptr + 5 + uimo_offset;

            UseActionHook = new Hook<UseAction>(useaction_hook_ptr, UseActionCallback);
            TryActionHook = new Hook<TryAction>(tryaction_hook_ptr, TryActionCallback);
            MouseoverHook = new Hook<MouseoverEntity>(uimo_hook_ptr, OnMouseoverEntityCallback);

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

        private unsafe bool TryActionCallback(IntPtr this_ptr, ActionType action_type, uint id, uint target, uint unk_1, uint origin, uint unk_2, ref Vector3 location)
        {
            if (action_type != ActionType.Spell)
            {
                return TryActionHook.Original(this_ptr, action_type, id, target, unk_1, origin, unk_2, ref location);
            }

            var adj_id = id;
            unsafe
            {
                var temp_id = ActionManager.fpGetAdjustedActionId(ActionManager.Instance(), id);
                var temp_res = Actions.GetRow(temp_id);
                if(temp_res != null && temp_res.IsPlayerAction)
                {
                    adj_id = temp_id;
                }
            }

            bool place_at_cursor = false;
            var new_target = RedirectTarget(adj_id, ref place_at_cursor);

            if(place_at_cursor)
            {
                var success = Services.GameGui.ScreenToWorld(ImGui.GetMousePos(), out var game_coords);
                if (ActionManager.fpIsRecastTimerActive(ActionManager.Instance(), action_type, adj_id) > 0)
                {
                    return TryActionHook.Original(this_ptr, action_type, id, target, unk_1, origin, unk_2, ref location);
                }

                return UseActionHook.Original(this_ptr, action_type, id, target, ref game_coords);
            }

            if (new_target != null)
            {
                var res = Actions.GetRow(adj_id)!;
                if (res.TargetArea)
                {
                    var new_location = new_target.Position;
                    if (ActionManager.fpIsRecastTimerActive(ActionManager.Instance(), action_type, adj_id) > 0)
                    {
                        return TryActionHook.Original(this_ptr, action_type, id, target, unk_1, origin, unk_2, ref location);
                    }
                    return UseActionHook.Original(this_ptr, action_type, id, new_target.ObjectId, ref new_location);
                } 

                return TryActionHook.Original(this_ptr, action_type, id, new_target.ObjectId, unk_1, origin, unk_2, ref location);
            }

            return TryActionHook.Original(this_ptr, action_type, id, target, unk_1, origin, unk_2, ref location);
        }
        private bool UseActionCallback(IntPtr this_ptr, ActionType action_type, uint id, uint target, ref Vector3 location, uint unk)
        {
            return UseActionHook.Original(this_ptr, action_type, id, target, ref location, unk);
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
        }
    }
}
