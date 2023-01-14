using Lumina.Excel.GeneratedSheets;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Objects.Enums;
using FFXIVClientStructs.FFXIV.Client.Game;
using Dalamud.Logging;

namespace Redirect {

    static class ExtensionMethods {

        private static readonly HashSet<uint> ActionBlocklist = new() {
            // These don't work anyway, but they're technically "ground target" placement so they get thrown in
            3573,   // "Ley Lines",
            7419,   // "Between the Lines",
            24403,  // "Regress",
        };

        private static readonly HashSet<uint> ActionAllowlist = new() {
            17055, // "Play",
            25822, // "Astral Flow",
        };

        /// <summary>
        /// For certain actions, only the upgraded version has optional targeting. This returns true for such actions.
        /// * Relies on a manually updated list (ActionAllowlist)
        /// </summary>
        /// <param name="a"></param>
        /// <returns></returns>
        public static bool IsActionAllowed(this Action a) => ActionAllowlist.Contains(a.RowId);

        /// <summary>
        /// Some actions are labeled with optional targetability, but break if tried to use in such a way. This returns true for such actions.
        /// * Relies on a manually updated list (ActionBlocklist)
        /// </summary>
        /// <param name="a"></param>
        /// <returns></returns>
        public static bool IsActionBlocked(this Action a) => ActionBlocklist.Contains(a.RowId);

        public static bool HasOptionalTargeting(this Action a) => a.CanTargetFriendly || a.CanTargetHostile || a.CanTargetParty || a.TargetArea;

        public static bool CanTargetFriendly(this Action a) => a.CanTargetFriendly || a.CanTargetParty;

        public static bool TargetTypeValid(this Action a, GameObject target) {

            if(a.TargetArea) {
                return true;
            }

            switch (target.ObjectKind) {
                case ObjectKind.BattleNpc:
                    BattleNpc npc = (BattleNpc)target;
                    return npc.BattleNpcKind == BattleNpcSubKind.Enemy ? a.CanTargetHostile : a.CanTargetFriendly();
                case ObjectKind.EventNpc:
                case ObjectKind.Player:
                case ObjectKind.Companion:
                    return a.CanTargetFriendly();
                default:
                    PluginLog.Information($"{a.Name} cannot be used on {target.Name} with type {target.ObjectKind}");
                    return false;
            }
        }

        public static bool TargetInRangeAndLOS(this Action a, GameObject target, out uint err) {
            if (Services.ClientState.LocalPlayer is not { } player) {
                err = 0;
                return false;
            }

            unsafe {
                var player_ptr = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)player.Address;
                var target_ptr = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)target.Address;
                err = ActionManager.MemberFunctionPointers.GetActionInRangeOrLoS(a.RowId, player_ptr, target_ptr);
            }

            // 0 success, 562 no LOS, 566 range, 565 not facing
            // TODO: Check "auto face" option instead of assuming it is on
            return err == 0 || err == 565;
        }
    }
}
