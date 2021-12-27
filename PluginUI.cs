using ImGuiNET;
using System;
using System.Numerics;
using ImGuiScene;
using Dalamud.Game.Text.SeStringHandling;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Interface;
using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace Redirect
{
    // It is good to have this be disposable in general, in case you ever need it
    // to do any cleanup
    class PluginUI : IDisposable
    {
        const uint ICON_SIZE = 32;
        private Plugin Plugin { get; } = null!;
        private Configuration Configuration { get; } = null!;
        private List<Lumina.Excel.GeneratedSheets.ClassJob> Jobs { get; } = null!;
        private Dictionary<ushort, TextureWrap> Icons { get; } = new();

        private Lumina.Excel.GeneratedSheets.ClassJob SelectedJob = null!;
        private bool SelectedRoleActions = false;
        private string search = string.Empty;

        internal bool MainWindowVisible;


        public PluginUI(Plugin plugin, Configuration config)
        {
            this.Plugin = plugin;
            this.Configuration = config;

           Plugin.Interface.UiBuilder.Draw += this.Draw;
           Plugin.Interface.UiBuilder.OpenConfigUi += this.OnOpenConfig;

            Jobs = Plugin.DataManager.GetExcelSheet<Lumina.Excel.GeneratedSheets.ClassJob>()!.
                Where(j => j.Role > 0 && j.ItemSoulCrystal.Value?.RowId > 0).ToList();

        }

        private void OnOpenConfig()
        {
            this.MainWindowVisible = true;
        }

        public void Dispose()
        {
            Plugin.Interface.UiBuilder.OpenConfigUi -= this.OnOpenConfig;
            Plugin.Interface.UiBuilder.Draw -= this.Draw;
            foreach(var icon in Icons.Values)
            {
                icon.Dispose();
            }
        }

        public void Draw()
        {
            if (!MainWindowVisible)
            {
                return;
            }

            ImGui.SetNextWindowSize(new Vector2(450, 400), ImGuiCond.FirstUseEver);

            if (!ImGui.Begin(this.Plugin.Name, ref this.MainWindowVisible, ImGuiWindowFlags.MenuBar))
            {
                ImGui.End();
                return;
            }

            if (ImGui.BeginMenuBar())
            {
                if (ImGui.BeginMenu("Options"))
                {
                    ImGui.PushID("options-menu");
                    if (ImGui.MenuItem("test"))
                    {
                        Dalamud.Logging.PluginLog.Information("test");
                    }
                    ImGui.EndMenu();
                }
                ImGui.EndMenuBar();
            }

            if(ImGui.BeginChild("abilities", new Vector2(ImGui.GetContentRegionAvail().X * 0.25f, -1))) {

      
                if (ImGui.TreeNode("Jobs"))
                {
                    if (ImGui.Selectable("Role Actions", SelectedRoleActions))
                    {
                        this.SelectedRoleActions = true;
                        this.SelectedJob = null!;
                    }

                    foreach (var job in Jobs)
                    {
                        if (ImGui.Selectable(job.Abbreviation, this.SelectedJob == job))
                        {
                            this.SelectedJob = job;
                            this.SelectedRoleActions = false;
                        }
                    }

                    ImGui.TreePop();
                }

                ImGui.EndChild();
            }

            ImGui.SameLine();

            if(ImGui.BeginChild("ability-view", new Vector2(-1, -1)))
            {
                this.DrawActions();
                ImGui.EndChild();
            }

            ImGui.End();
        }

        private TextureWrap? FetchTexture(ushort id)
        {
            this.Icons.TryGetValue(id, out TextureWrap? texture);
            
            if(texture is null && id > 0)
            {
                texture = Plugin.DataManager.GetImGuiTextureIcon(id);
                if(texture is not null) { 
                    this.Icons[id] = texture;
                }
            }

            return texture;
        }

        private void DrawIcon(ushort id, Vector2 size = default)
        {
            var texture = FetchTexture(id);

            if(texture is null)
            {
                return;
            }
            var drawsize = size == default ? new Vector2(texture.Width, texture.Height) : size;
            ImGui.Image(texture.ImGuiHandle, drawsize);
        }

        private void DrawActions()
        {
            if (!this.SelectedRoleActions && this.SelectedJob is null)
            {
                return;
            }

            ImGui.InputTextWithHint("##search", "Search", ref search, 100);

            if (ImGui.BeginTable("actions", 4, ImGuiTableFlags.BordersInnerH))
            {
                ImGui.TableSetupColumn("##icon", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("##plus-icons", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Priority");

                ImGui.TableHeadersRow();

                var actions = this.SelectedRoleActions ? CActions.GetRoleActions() : CActions.GetJobActions(this.SelectedJob);

                var filtered = actions.Where(x =>
                {

                    if (search.Length > 0 && !x.Name.ToString().ToLower().Contains(search.ToLower()))
                    {
                        return false;
                    }

                    if (!Configuration.DisplayPVP && x.IsPvP)
                    {
                        return false;
                    }

                    return true;
                });
 

                foreach(var action in filtered)
                {
                    
                    var dims = new Vector2(ICON_SIZE);

                    // ICON

                    ImGui.TableNextColumn();
                    this.DrawIcon(action.Icon, dims);

                    // ACTION NAME

                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted($"{action.Name}{(action.IsPvP ? " (PVP)" : "")}");

                    // ADD REDIRECTION

                    ImGui.TableNextColumn();

                    ImGui.Dummy(new Vector2(0, 2));
                    ImGui.PushFont(UiBuilder.IconFont);

                    Configuration.Redirections.TryGetValue(action.RowId, out var redirection);

                    redirection = redirection ?? new() { ID = action.RowId };

                    if (ImGui.Button($"{FontAwesomeIcon.PlusCircle.ToIconString()}##-{action.RowId}"))
                    {
                        redirection.Priority.Add(Configuration.DefaultRedirection);
                    }
                    ImGui.PopFont();

                    ImGui.TableNextColumn();

                    var remove = -1;

                    for (var i = 0; i < redirection.Count; i++)
                    {
                        ImGui.Dummy(new Vector2(0, 2));

                        ImGui.PushItemWidth(125f);


                        if (ImGui.BeginCombo($"##redirection-{action.RowId}-{i}", redirection[i]))
                        {

                            for (int j = 0; j < Util.TargetOptions.Length; j++)
                            {
                                bool is_selected = (Util.TargetOptions[j] == redirection[i]);
                                if (ImGui.Selectable(Util.TargetOptions[j], is_selected))
                                {
                                    redirection[i] = Util.TargetOptions[j];
                                }

                                if (is_selected)
                                {
                                    ImGui.SetItemDefaultFocus();
                                }

                            }
                            ImGui.EndCombo();
                        }

                        ImGui.PopItemWidth();

                        ImGui.SameLine();

                        ImGui.PushFont(UiBuilder.IconFont);
                        if (ImGui.Button($"{FontAwesomeIcon.Trash.ToIconString()}##-{action.RowId}-{i}"))
                        {
                            remove = i;

                        }
                        ImGui.PopFont();
                    }

                    if (remove >= 0)
                    {
                        
                        redirection.RemoveAt(remove);
                    }

                    if (redirection.Count > 0)
                    {
                        Configuration.Redirections[action.RowId] = redirection;
                    } else
                    {
                        Configuration.Redirections.Remove(action.RowId);
                    }

                    ImGui.Dummy(new Vector2(0, 2));
                }


                ImGui.EndTable();
            }
        }
    }

}
