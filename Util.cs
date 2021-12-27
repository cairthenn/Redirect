using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Redirect
{

    using Category = Lumina.Excel.GeneratedSheets.ClassJobCategory;
    using Job = Lumina.Excel.GeneratedSheets.ClassJob;
    using Action = Lumina.Excel.GeneratedSheets.Action;

    static class Util
    {
        public static readonly string[] TargetOptions = {"UI Mouseover", "Field Mouseover", "Target", "Focus", "Target of Target", "Self", "<2>", "<3>", "<4>", "<5>", "<6>", "<7>", "<8>"};

        public static bool UsableByJob(this Action a, Job j)
        {
            return true;
        }
    }
}
