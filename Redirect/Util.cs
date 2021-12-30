using System.Collections.Generic;

namespace Redirect
{

    using Category = Lumina.Excel.GeneratedSheets.ClassJobCategory;
    using Job = Lumina.Excel.GeneratedSheets.ClassJob;
    using Action = Lumina.Excel.GeneratedSheets.Action;

    static class Util
    {
        private static readonly HashSet<uint> ActionBlacklist = new HashSet<uint>() { 
            // These don't work anyway, but they're technically "ground target" placement so they get thrown in
            3573,   // "Ley Lines",
            7419,   // "Between the Lines",
            24403,  // "Regress",
        };

        private static readonly Dictionary<uint, HashSet<uint>> ActionWhitelist = new Dictionary<uint, HashSet<uint>>()
        {
            // AST: Play
            [0x21] = new HashSet<uint>() { 17055 },
        };
        
        public static readonly string[] TargetOptions = {"Cursor","UI Mouseover", "Model Mouseover", "Target", "Focus", "Target of Target", "Self", "<2>", "<3>", "<4>", "<5>", "<6>", "<7>", "<8>"};

        public static bool UsableByJob(this Action a, Job j)
        {
            if(ActionBlacklist.Contains(a.RowId))
            {
                return false;
            }
            
            if (a.ClassJob.Value == j || a.ClassJob.Value == j.ClassJobParent.Value)
            {
                return true;
            }
            
            if(!a.IsPlayerAction || a.IsRoleAction)
            {
                return false;
            }

            var prop = typeof(Category).GetProperty(j.Abbreviation.ToString());
            return (bool) prop?.GetValue(a.ClassJobCategory.Value)!;
        }

        public static bool IsActionWhiteListed(this Job j, Action a)
        {
            return ActionWhitelist.ContainsKey(j.RowId) && ActionWhitelist[j.RowId].Contains(a.RowId);
        }

        public static bool HasOptionalTargeting(this Action a)
        {
            return a.CanTargetFriendly || a.CanTargetHostile || a.CanTargetParty || a.TargetArea;
        }
    }
}
