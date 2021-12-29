using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;


namespace Redirect
{

    using Category = Lumina.Excel.GeneratedSheets.ClassJobCategory;
    using Job = Lumina.Excel.GeneratedSheets.ClassJob;
    using Action = Lumina.Excel.GeneratedSheets.Action;

    static class Util
    {
        private static readonly HashSet<string> ActionBlacklist = new HashSet<string>() { 
            // These don't work anyway, but they're technically "ground target" placement so they get thrown in
            "Ley Lines",
            "Between the Lines",
            "Regress"
        };
        
        public static readonly string[] TargetOptions = {"Cursor","UI Mouseover", "Model Mouseover", "Target", "Focus", "Target of Target", "Self", "<2>", "<3>", "<4>", "<5>", "<6>", "<7>", "<8>"};

        public static bool UsableByJob(this Action a, Job j)
        {
            if(ActionBlacklist.Contains(a.Name))
            {
                return false;
            }
            
            if(a.ClassJob.Value == j || a.ClassJob.Value == j.ClassJobParent.Value)
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

        public static bool HasOptionalTargeting(this Action a)
        {
            return a.CanTargetFriendly || a.CanTargetHostile || a.CanTargetParty || a.TargetArea;
        }
    }
}
