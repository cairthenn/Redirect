using Lumina;
using Lumina.Data;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Redirect {


    public class Actions {

        private ExcelSheet<Action> Sheet = null!;
        private ExcelSheet<RawRow> CJC = null!;
        private IEnumerable<Action> Role = null!;
        private List<uint> JobInfo = null!;
        private readonly Dictionary<uint, IEnumerable<Action>> Jobs = new();
        private bool initialized = false;

        public List<uint> GetJobInfo() => initialized ? JobInfo : new List<uint>();
        public IEnumerable<Action> GetJobActions(uint job) => initialized ? Jobs[job] : new List<Action>();
        public IEnumerable<Action> GetRoleActions() => initialized ? Role : new List<Action>();
        public Action GetRow(uint id) => Sheet.GetRow(id);

        public Actions() {
            Task.Factory.StartNew(this.Initialize);
        }

        private void Initialize() {
            Sheet = Services.DataManager.GetExcelSheet<Action>()!;
            Role = Sheet.Where(a => a.IsRoleAction && a.ClassJobLevel != 0 && a.HasOptionalTargeting()).ToList();
            JobInfo = Services.DataManager.GetExcelSheet<ClassJob>()!.Where(j => j.Role > 0 && j.ItemSoulCrystal.Value.RowId > 0).Select(j => j.RowId).ToList();
            CJC = Services.DataManager.GetExcelSheet<RawRow>(name:"ClassJobCategory");

            foreach (var job in JobInfo) {
                Jobs[job] = Sheet.Where(a => {

                    if (a.IsGroundActionBlocked() || a.ClassJob.RowId + 1 == 0 || !a.IsPlayerAction || a.IsRoleAction) {
                        return false;
                    }

                    var id = a.ClassJobCategory.RowId;
                    var cjc = CJC.GetRow(id);

                    return cjc.ReadBoolColumn((int) job + 1) && (a.HasOptionalTargeting() || a.IsActionAllowed());

                }).ToList();
            }
            initialized = true;
        }
    }
}
