using System.Resources;
using Lumina.Excel.GeneratedSheets;
using System.Collections.Generic;
using System.ComponentModel;

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
