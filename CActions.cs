using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Redirect
{
    using ActionInfo = Lumina.Excel.GeneratedSheets.Action;
    using JobInfo = Lumina.Excel.GeneratedSheets.ClassJob;

    public class CActions
    {
        public static void Initialize()
        {
            Dalamud.Logging.PluginLog.Information($"Job action information created: {Instance.GetHashCode()}");
        }
        public static IEnumerable<ActionInfo> GetJobActions(JobInfo job) => Instance.jobs[job];

        public static IEnumerable<ActionInfo> GetRoleActions() => Instance.role;

        private static readonly Lazy<CActions> lazy = new Lazy<CActions>(() => new CActions());

        private static CActions Instance { get { return lazy.Value; } }


        private IEnumerable<ActionInfo> role = null!;
        private Dictionary<JobInfo, IEnumerable<ActionInfo>> jobs = new();

        private CActions()
        {
            var all = Services.DataManager.GetExcelSheet<ActionInfo>()!;
            role = all.Where(a => a.IsRoleAction && a.ClassJobLevel != 0 && a.HasOptionalTargeting());
            var jobs = Services.DataManager.GetExcelSheet<JobInfo>()!.
                Where(j => j.Role > 0 && j.ItemSoulCrystal.Value?.RowId > 0).ToList();

            foreach (var job in jobs)
            {
                this.jobs[job] = all.Where(a => a.HasOptionalTargeting() && a.UsableByJob(job)).ToList();
            }
        }

    }
}
