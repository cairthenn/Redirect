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

        public static bool CanTargetFriendly(this Action a) => a.CanTargetFriendly || a.CanTargetParty || (a.TargetArea && !a.IsActionBlocked());
    }
}
