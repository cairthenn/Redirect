using Lumina.Excel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Redirect {
    using ActionInfo = Lumina.Excel.GeneratedSheets.Action;
    using JobInfo = Lumina.Excel.GeneratedSheets.ClassJob;

    public class Actions {

        private ExcelSheet<ActionInfo> Sheet = null!;
        private IEnumerable<ActionInfo> Role = null!;
        private List<JobInfo> JobInfo = null!;
        private Dictionary<JobInfo, IEnumerable<ActionInfo>> Jobs = new();
        private bool initialized = false;

        public List<JobInfo> GetJobInfo() => initialized ? JobInfo : new List<JobInfo>();
        public IEnumerable<ActionInfo> GetJobActions(JobInfo job) => initialized ? Jobs[job] : new List<ActionInfo>();
        public IEnumerable<ActionInfo> GetRoleActions() => initialized ? Role : new List<ActionInfo>();
        public ActionInfo? GetRow(uint id) => Sheet?.GetRow(id);

        public Actions() {
            Task.Factory.StartNew(this.Initialize);
        }

        private void Initialize() {
            Sheet = Services.DataManager.GetExcelSheet<ActionInfo>()!;
            Role = Sheet.Where(a => a.IsRoleAction && a.ClassJobLevel != 0 && a.HasOptionalTargeting()).ToList();
            JobInfo = Services.DataManager.GetExcelSheet<JobInfo>(Dalamud.ClientLanguage.English)!.Where(j => j.Role > 0 && j.ItemSoulCrystal.Value?.RowId > 0).ToList();

            foreach (var job in JobInfo) {
                Jobs[job] = Sheet.Where(a => a.UsableByJob(job) && (a.HasOptionalTargeting() || a.IsActionAllowed())).ToList();
            }
            initialized = true;
        }
    }
}
