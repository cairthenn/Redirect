using System;
using System.Numerics;
using ImGuiNET;
using ImGuiScene;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Interface;

namespace Redirect {
    class PluginUI : IDisposable {
        
        const uint ICON_SIZE = 32;
        const uint MAX_REDIRECTS = 12;

        private Plugin Plugin { get; } = null!;
        private Configuration Configuration { get; } = null!;
        private GameHooks GameHooks { get; } = null!;
        private Actions Actions { get; } = null!;

        private List<Lumina.Excel.GeneratedSheets.ClassJob> Jobs => Actions.GetJobInfo();
        private Dictionary<ushort, TextureWrap> Icons { get; } = new();

        internal bool MainWindowVisible = false;
        private bool SelectedRoleActions = false;
        private Lumina.Excel.GeneratedSheets.ClassJob SelectedJob = null!;
        private string search = string.Empty;
        private readonly string[] TargetOptions = { "Cursor", "UI Mouseover", "Model Mouseover", "Target", "Focus", "Target of Target", "Self", "Soft Target", "<2>", "<3>", "<4>", "<5>", "<6>", "<7>", "<8>" };

        public PluginUI(Plugin plugin, Configuration config, GameHooks hooks, Actions actions) {
            Plugin = plugin;
            Configuration = config;
            GameHooks = hooks;
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
            foreach(var icon in Icons.Values) {
                icon.Dispose();
            }
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

                    bool range_fail = Configuration.SilentRangeFailure;
                    if (ImGui.Checkbox("Ignore targets out of range", ref range_fail)) {
                        Configuration.SilentRangeFailure = range_fail;
                    }

                    bool tt_fail = Configuration.SilentTargetTypeFailure;
                    if (ImGui.Checkbox("Ignore incorrect target types", ref tt_fail)) {
                        Configuration.SilentTargetTypeFailure = tt_fail;
                    }

                    bool friendly_mo = Configuration.DefaultMouseoverFriendly;
                    if (ImGui.Checkbox("Treat all friendly actions as mouseovers", ref friendly_mo)) {
                        Configuration.DefaultMouseoverFriendly = friendly_mo;
                    }

                    if (friendly_mo) {
                        ImGui.Dummy(new Vector2(1, -1));
                        ImGui.SameLine();
                        bool friendly_mo_model = Configuration.DefaultModelMouseoverFriendly;
                        if (ImGui.Checkbox("Include friendly target models", ref friendly_mo_model)) {
                            Configuration.DefaultModelMouseoverFriendly = friendly_mo_model;
                        }
                        ImGui.Dummy(new Vector2(1, -1));
                        ImGui.SameLine();
                        bool cursor_mo = Configuration.DefaultCursorMouseover;
                        if (ImGui.Checkbox("Include ground targets at cursor", ref cursor_mo)) {
                            Configuration.DefaultCursorMouseover = cursor_mo;
                        }
                    }

                    bool hostile_mo = Configuration.DefaultMouseoverHostile;
                    if (ImGui.Checkbox("Treat all hostile actions as mouseovers", ref hostile_mo)) {
                        Configuration.DefaultMouseoverHostile = hostile_mo;
                    }

                    if (hostile_mo) {
                        ImGui.Dummy(new Vector2(1, -1));
                        ImGui.SameLine();
                        bool hostile_mo_model = Configuration.DefaultModelMouseoverHostile;
                        if (ImGui.Checkbox("Include hostile target models", ref hostile_mo_model)) {
                            Configuration.DefaultModelMouseoverHostile = hostile_mo_model;
                        }
                    }

                    ImGui.Dummy(new Vector2(-1, 1));

                    ImGui.Text("Allow these actions to queue:");

                    ImGui.Dummy(new Vector2(-1, 1));

                    bool queue_ground = Configuration.QueueGroundActions;
                    if (ImGui.Checkbox("Ground targeted actions", ref queue_ground)) {
                        Configuration.QueueGroundActions = queue_ground;
                    }

                    bool macro_queue = Configuration.EnableMacroQueueing;
                    if (ImGui.Checkbox("Actions from macros", ref macro_queue)) {
                        Configuration.EnableMacroQueueing = macro_queue;
                    }

                    bool sprint_queue = Configuration.QueueSprint;
                    if (ImGui.Checkbox("Sprint", ref sprint_queue)) {
                        if(sprint_queue != Configuration.QueueSprint) {
                            GameHooks.UpdateSprintQueueing(sprint_queue);
                            Configuration.QueueSprint = sprint_queue;
                        }
                    }

                    bool item_queue = Configuration.QueuePotions;
                    if (ImGui.Checkbox("Potions", ref item_queue)) {
                        if (item_queue != Configuration.QueuePotions) {
                            GameHooks.UpdatePotionQueueing(item_queue);
                            Configuration.QueuePotions = item_queue;
                        }
                    }

                    ImGui.EndMenu();
                }

                ImGui.EndMenuBar();
            }

            ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X * 0.05f, -1));

            if (ImGui.BeginChild("abilities", new Vector2(ImGui.GetContentRegionAvail().X * 0.20f, -1))) {        
                    
                    if (ImGui.Selectable(" Role Actions", SelectedRoleActions)) {
                        SelectedRoleActions = true;
                        SelectedJob = null!;
                    }

                    foreach (var job in Jobs) { 
                        if (ImGui.Selectable($" {job.Abbreviation}", SelectedJob == job)) {
                            SelectedJob = job;
                            SelectedRoleActions = false;
                        }
                    }

                ImGui.EndChild();
            }

            ImGui.SameLine();

            if(ImGui.BeginChild("ability-view", new Vector2(-1, -1))) {
                DrawActions();
                ImGui.EndChild();
            }

            ImGui.End();
        }

        private TextureWrap? FetchTexture(ushort id) {
            Icons.TryGetValue(id, out TextureWrap? texture);
            
            if(texture is null && id > 0) {
                texture = Plugin.DataManager.GetImGuiTextureIcon(id);
                if(texture is not null) { 
                    Icons[id] = texture;
                }
            }

            return texture;
        }

        private void DrawIcon(ushort id, Vector2 size = default) {
            var texture = FetchTexture(id);

            if(texture is null) {
                return;
            }

            var drawsize = size == default ? new Vector2(texture.Width, texture.Height) : size;
            ImGui.Image(texture.ImGuiHandle, drawsize);
        }

        private void DrawActions() {

            if (!SelectedRoleActions && SelectedJob is null) {
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

                    if (search.Length > 0 && !x.Name.ToString().ToLower().Contains(search.ToLower())) {
                        return false;
                    }

                    if (x.IsPvP) {
                        return false;
                    }

                    return true;
                });
 

                foreach(var action in filtered) {
                    
                    var dims = new Vector2(ICON_SIZE);

                    // ICON

                    ImGui.TableNextColumn();
                    DrawIcon(action.Icon, dims);

                    // ACTION NAME

                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted(action.Name);

                    // ADD REDIRECTION

                    ImGui.TableNextColumn();
                    ImGui.Dummy(new Vector2(0, 2));
                    ImGui.PushFont(UiBuilder.IconFont);

                    Configuration.Redirections.TryGetValue(action.RowId, out var redirection);

                    redirection = redirection ?? new() { ID = action.RowId };

                    // TODO: Disable the button? Why isn't this possible

                    if (ImGui.Button($"{FontAwesomeIcon.PlusCircle.ToIconString()}##-{action.RowId}")) {
                        if(redirection.Count < MAX_REDIRECTS) {
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
                               
                                if(TargetOptions[j] == "Cursor" && !action.TargetArea) {
                                    continue;
                                }

                                bool is_selected = (TargetOptions[j] == redirection[i]);

                                if (ImGui.Selectable(TargetOptions[j], is_selected)) {
                                    redirection[i] = TargetOptions[j];
                                    save = true;
                                }

                                if (is_selected) {
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

                    if(save) {
                        Configuration.Save();
                    }

                    ImGui.Dummy(new Vector2(0, 2));
                }

                ImGui.EndTable();
            }
        }
    }
}
