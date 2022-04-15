using Lumina.Data;
using Lumina.Excel.GeneratedSheets;
using System.Collections.Generic;

namespace Redirect {

    static class Util {

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


        public static readonly string[] TargetOptions = {"Cursor", "UI Mouseover", "Model Mouseover", "Target", "Focus", "Target of Target", "Self", "Soft Target", "<2>", "<3>", "<4>", "<5>", "<6>", "<7>", "<8>"};

        public static bool IsActionAllowed(this Action a) {
            return ActionAllowlist.Contains(a.RowId);
        }

        public static bool IsActionBlocked(this Action a) {
            return ActionBlocklist.Contains(a.RowId);
        }

        public static bool HasOptionalTargeting(this Action a) {
            return a.CanTargetFriendly || a.CanTargetHostile || a.CanTargetParty || a.TargetArea;
        }

        public static bool CanTargetFriendly(this Action a) => a.CanTargetFriendly || a.CanTargetParty || (a.TargetArea && !a.IsActionBlocked());
    }
}
