using Lumina.Excel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Redirect
{
    using ActionInfo = Lumina.Excel.GeneratedSheets.Action;
    using JobInfo = Lumina.Excel.GeneratedSheets.ClassJob;

    public class Actions
    {
        public static void Initialize()
        {
            // This is silly but it creates the singleton in the background and populates filtered job actions

            Dalamud.Logging.PluginLog.Information($"[{Instance.GetHashCode()}] Job action information created");
        }
        public static IEnumerable<ActionInfo> GetJobActions(JobInfo job) => Instance.jobs[job];

        public static IEnumerable<ActionInfo> GetRoleActions() => Instance.role;

        public static ActionInfo? GetRow(uint id) => Instance.sheet.GetRow(id);

        private static readonly Lazy<Actions> lazy = new Lazy<Actions>(() => new Actions());

        private static Actions Instance { get { return lazy.Value; } }

        private ExcelSheet<ActionInfo> sheet = null!;
        private IEnumerable<ActionInfo> role = null!;
        private Dictionary<JobInfo, IEnumerable<ActionInfo>> jobs = new();

        private Actions()
        {
            sheet = Services.DataManager.GetExcelSheet<ActionInfo>()!;
            role = sheet.Where(a => a.IsRoleAction && a.ClassJobLevel != 0 && a.HasOptionalTargeting()).ToList();
            var jobs = Services.DataManager.GetExcelSheet<JobInfo>()!.
                Where(j => j.Role > 0 && j.ItemSoulCrystal.Value?.RowId > 0).ToList();

            foreach (var job in jobs)
            {
                this.jobs[job] = sheet.Where(a => a.HasOptionalTargeting() && a.UsableByJob(job)).ToList();
            }
        }

    }
}
