using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Lumina.Excel.Sheets;

namespace Redirect {
    class PluginUI : IDisposable {

        const uint ICON_SIZE = 32;
        const uint MAX_REDIRECTS = 12;

        private Plugin Plugin { get; } = null!;
        private Configuration Configuration { get; } = null!;
        private Actions Actions { get; } = null!;

        private List<uint> Jobs => Actions.GetJobInfo();

        internal bool MainWindowVisible = false;
        private bool SelectedRoleActions = false;
        private uint SelectedJob;
        private string search = string.Empty;
        private readonly string[] TargetOptions = ["Cursor", "UI Mouseover", "Model Mouseover", "Target", "Focus", "Target of Target", "Self", "Soft Target", "Chocobo", "<2>", "<3>", "<4>", "<5>", "<6>", "<7>", "<8>"];

        public PluginUI(Plugin plugin, Configuration config, Actions actions) {
            Plugin = plugin;
            Configuration = config;
            Actions = actions;
            Plugin.Interface.UiBuilder.Draw += Draw;
            Plugin.Interface.UiBuilder.OpenConfigUi += OnOpenConfig;
        }

        private void OnOpenConfig() {
            MainWindowVisible = true;
        }

        public void Dispose() {
            Plugin.Interface.UiBuilder.OpenConfigUi -= OnOpenConfig;
            Plugin.Interface.UiBuilder.Draw -= Draw;
        }

        public void Draw() {
            if (!MainWindowVisible) {
                return;
            }

            ImGui.SetNextWindowSize(new Vector2(450, 400), ImGuiCond.FirstUseEver);

            if (!ImGui.Begin(Plugin.Name, ref MainWindowVisible, ImGuiWindowFlags.MenuBar)) {
                ImGui.End();
                return;
            }

            if (ImGui.BeginMenuBar()) {
                if (ImGui.BeginMenu("Options")) {

                    ImGui.Text("Target redirection options:");

                    ImGui.Dummy(new Vector2(-1, 1));

                    bool ignoreErrors = Configuration.IgnoreErrors;
                    if (ImGui.Checkbox("Ignore range and target type errors", ref ignoreErrors)) {
                        Configuration.IgnoreErrors = ignoreErrors;
                    }

                    bool friendlyMO = Configuration.DefaultMouseoverFriendly;
                    if (ImGui.Checkbox("Treat all friendly actions as mouseovers", ref friendlyMO)) {
                        Configuration.DefaultMouseoverFriendly = friendlyMO;
                    }

                    if (friendlyMO) {
                        ImGui.Dummy(new Vector2(1, -1));
                        ImGui.SameLine();
                        bool friendlyModelMO = Configuration.DefaultModelMouseoverFriendly;
                        if (ImGui.Checkbox("Include friendly target models", ref friendlyModelMO)) {
                            Configuration.DefaultModelMouseoverFriendly = friendlyModelMO;
                        }
                    }

                    bool hostileMO = Configuration.DefaultMouseoverHostile;
                    if (ImGui.Checkbox("Treat all hostile actions as mouseovers", ref hostileMO)) {
                        Configuration.DefaultMouseoverHostile = hostileMO;
                    }

                    if (hostileMO) {
                        ImGui.Dummy(new Vector2(1, -1));
                        ImGui.SameLine();
                        bool hostileModelMO = Configuration.DefaultModelMouseoverHostile;
                        if (ImGui.Checkbox("Include hostile target models", ref hostileModelMO)) {
                            Configuration.DefaultModelMouseoverHostile = hostileModelMO;
                        }
                    }

                    bool groundMO = Configuration.DefaultMouseoverGround;
                    if (ImGui.Checkbox("Treat all ground-targeted actions as mouseovers", ref groundMO)) {
                        Configuration.DefaultMouseoverGround = groundMO;
                    }

                    bool cursorMo = Configuration.DefaultCursorMouseover;
                    if (ImGui.Checkbox("Place all ground targets at the cursor", ref cursorMo)) {
                        Configuration.DefaultCursorMouseover = cursorMo;
                    }

                    ImGui.Dummy(new Vector2(-1, 1));

                    ImGui.Text("Allow these actions to queue:");

                    ImGui.Dummy(new Vector2(-1, 1));

                    bool queueGround = Configuration.QueueGroundActions;
                    if (ImGui.Checkbox("Ground targeted actions", ref queueGround)) {
                        Configuration.QueueGroundActions = queueGround;
                    }

                    bool queueMacros = Configuration.EnableMacroQueueing;
                    if (ImGui.Checkbox("Actions from macros", ref queueMacros)) {
                        Configuration.EnableMacroQueueing = queueMacros;
                    }

                    ImGui.EndMenu();
                }

                ImGui.EndMenuBar();
            }

            ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X * 0.05f, -1));

            if (ImGui.BeginChild("abilities", new Vector2(ImGui.GetContentRegionAvail().X * 0.20f, -1))) {

                if (ImGui.Selectable(" Role Actions", SelectedRoleActions)) {
                    SelectedRoleActions = true;
                    SelectedJob = 0;
                }

                var cjSheet = Services.DataManager.GetExcelSheet<ClassJob>()!;

                foreach (var job in Jobs) {
                    var jobRow = cjSheet.GetRow(job)!;
                    if (ImGui.Selectable($" {jobRow.Abbreviation}", SelectedJob == job)) {
                        SelectedJob = job;
                        SelectedRoleActions = false;
                    }
                }

                ImGui.EndChild();
            }

            ImGui.SameLine();

            if (ImGui.BeginChild("ability-view", new Vector2(-1, -1))) {
                DrawActions();
                ImGui.EndChild();
            }

            ImGui.End();
        }

        private static void DrawIcon(ushort id, Vector2 size = default) {
            var icon = new GameIconLookup(id);
            var texture = Services.TextureProvider.GetFromGameIcon(icon);
            var wrap = texture.GetWrapOrDefault();

            if (wrap is null) {
                return;
            }

            var drawsize = size == default ? new Vector2(wrap.Width, wrap.Height) : size;
            ImGui.Image(wrap.Handle, drawsize);
        }

        private void DrawActions() {

            if (!SelectedRoleActions && SelectedJob <= 0) {
                var region = ImGui.GetContentRegionAvail();
                ImGui.Dummy(new Vector2(1, region.Y * .45f));
                ImGui.Dummy(new Vector2(region.X * .30f, -1));
                ImGui.SameLine();
                ImGui.Text("Select a job to get started!");
                return;
            }

            bool save = false;
            ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
            ImGui.InputTextWithHint("##search", "Search", ref search, 250);
            ImGui.PopItemWidth();

            if (ImGui.BeginTable("actions", 4, ImGuiTableFlags.BordersInnerH)) {
                ImGui.TableSetupColumn("##icon", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("##plus-icons", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Priority");
                ImGui.TableHeadersRow();

                var actions = SelectedRoleActions ? Actions.GetRoleActions() : Actions.GetJobActions(SelectedJob);

                var filtered = actions.Where(x => {

                    if (search.Length > 0 && !x.Name.ToString().Contains(search, StringComparison.CurrentCultureIgnoreCase)) {
                        return false;
                    }

                    if (x.IsPvP) {
                        return false;
                    }

                    return true;
                });


                foreach (var action in filtered) {

                    var dims = new Vector2(ICON_SIZE);

                    // ICON

                    ImGui.TableNextColumn();
                    DrawIcon(action.Icon, dims);

                    // ACTION NAME

                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted(action.Name.ToString());

                    // ADD REDIRECTION

                    ImGui.TableNextColumn();
                    ImGui.Dummy(new Vector2(0, 2));
                    ImGui.PushFont(UiBuilder.IconFont);

                    Configuration.Redirections.TryGetValue(action.RowId, out var redirection);

                    redirection ??= new() { ID = action.RowId };

                    // TODO: Disable the button? Why isn't this possible

                    if (ImGui.Button($"{FontAwesomeIcon.PlusCircle.ToIconString()}##-{action.RowId}")) {
                        if (redirection.Count < MAX_REDIRECTS) {
                            redirection.Priority.Add(Configuration.DefaultRedirection);
                            save = true;
                        }
                    }

                    ImGui.PopFont();
                    ImGui.TableNextColumn();
                    var remove = -1;

                    for (var i = 0; i < redirection.Count; i++) {

                        ImGui.Dummy(new Vector2(0, 2));
                        ImGui.PushItemWidth(125f);

                        if (ImGui.BeginCombo($"##redirection-{action.RowId}-{i}", redirection[i])) {

                            for (int j = 0; j < TargetOptions.Length; j++) {

                                if (TargetOptions[j] == "Cursor" && !action.TargetArea) {
                                    continue;
                                }

                                bool selected = (TargetOptions[j] == redirection[i]);

                                if (ImGui.Selectable(TargetOptions[j], selected)) {
                                    redirection[i] = TargetOptions[j];
                                    save = true;
                                }

                                if (selected) {
                                    ImGui.SetItemDefaultFocus();
                                }
                            }

                            ImGui.EndCombo();
                        }

                        ImGui.PopItemWidth();
                        ImGui.SameLine();
                        ImGui.PushFont(UiBuilder.IconFont);

                        if (ImGui.Button($"{FontAwesomeIcon.Trash.ToIconString()}##-{action.RowId}-{i}")) {
                            remove = i;
                            save = true;

                        }

                        ImGui.PopFont();
                    }

                    if (remove >= 0) {
                        redirection.RemoveAt(remove);
                    }

                    if (redirection.Count > 0) {
                        Configuration.Redirections[action.RowId] = redirection;

                    }
                    else {
                        Configuration.Redirections.Remove(action.RowId);
                    }

                    if (save) {
                        Configuration.Save();
                    }

                    ImGui.Dummy(new Vector2(0, 2));
                }

                ImGui.EndTable();
            }
        }
    }
}
