using Lumina;
using Lumina.Data;
using Lumina.Excel;
using Lumina.Excel.GeneratedSheets;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Redirect {

    /// <summary>
    /// Extends ClassJobCategory to be indexable by a Job
    /// Author: Caraxi
    /// Source: https://github.com/Caraxi/SimpleTweaksPlugin/blob/main/Sheets/ExtendedClassJobCategory.cs
    /// </summary>
    public class ExtendedClassJobCategory : ClassJobCategory {

        private bool[]? classJob;

        public bool this[ClassJob cj] => cj.RowId < classJob?.Length && classJob[cj.RowId];
        public bool this[uint cj] => cj < classJob?.Length && classJob[cj];

        public override void PopulateData(RowParser parser, GameData gameData, Language language) {
            base.PopulateData(parser, gameData, language);

            classJob = new bool[parser.Sheet.ColumnCount - 1];
            for (var i = 0; i < parser.Sheet.ColumnCount - 1; i++) {
                classJob[i] = parser.ReadColumn<bool>(i + 1);
            }
        }
    }

    public class Actions {

        private ExcelSheet<Action> Sheet = null!;
        private ExcelSheet<ExtendedClassJobCategory> CJC = null!;
        private IEnumerable<Action> Role = null!;
        private List<ClassJob> JobInfo = null!;
        private readonly Dictionary<ClassJob, IEnumerable<Action>> Jobs = new();
        private bool initialized = false;

        public List<ClassJob> GetJobInfo() => initialized ? JobInfo : new List<ClassJob>();
        public IEnumerable<Action> GetJobActions(ClassJob job) => initialized ? Jobs[job] : new List<Action>();
        public IEnumerable<Action> GetRoleActions() => initialized ? Role : new List<Action>();
        public Action? GetRow(uint id) => Sheet?.GetRow(id);

        public Actions() {
            Task.Factory.StartNew(this.Initialize);
        }

        private void Initialize() {
            Sheet = Services.DataManager.GetExcelSheet<Action>()!;
            Role = Sheet.Where(a => a.IsRoleAction && a.ClassJobLevel != 0 && a.HasOptionalTargeting()).ToList();
            JobInfo = Services.DataManager.GetExcelSheet<ClassJob>()!.Where(j => j.Role > 0 && j.ItemSoulCrystal.Value?.RowId > 0).ToList();
            CJC = Services.DataManager.GetExcelSheet<ExtendedClassJobCategory>()!;

            foreach (var job in JobInfo) {
                Jobs[job] = Sheet.Where(a => {

                    if (a.IsActionBlocked() || a.ClassJob.Row + 1 == 0 || !a.IsPlayerAction || a.IsRoleAction) {
                        return false;
                    }

                    var id = a.ClassJobCategory.Row;
                    var ecjc = CJC.GetRow(id)!;

                    return ecjc[job] && (a.HasOptionalTargeting() || a.IsActionAllowed());

                }).ToList();
            }
            initialized = true;
        }
    }
}
