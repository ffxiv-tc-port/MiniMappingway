using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using MiniMappingway.Manager;
using MiniMappingway.Model;
using MiniMappingway.Properties;
using MiniMappingway.Service;
using System.Linq;
using System.Numerics;

namespace MiniMappingway.Windows;

public class SettingsWindow : Window
{
    public SettingsWindow() : base(Resources.SettingsWindowTitle)
    {
        Size = new Vector2(450, 405);
        SizeCondition = ImGuiCond.Once;
        Flags = ImGuiWindowFlags.NoCollapse;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(450, 405),
            MaximumSize = new Vector2(1000, 1000)
        };
        ForceMainWindow = true;

    }

    public override void Draw()
    {
        // can't ref a property, so use a local copy
        var enabled = ServiceManager.Configuration.Enabled;
        if (ImGui.Checkbox(Resources.Enabled, ref enabled))
        {
            ServiceManager.Configuration.Enabled = enabled;
            ServiceManager.Configuration.Save();
        }

        ImGui.TextColored(new Vector4(255, 0, 0, 255), Resources.FcTagComparisonNotice);
        ImGui.TextColored(new Vector4(255, 0, 0, 255), Resources.FcTagCommonNotice);

        DrawPvpRadarSettings();

        ImGui.Text(Resources.MarkerSettingsHeader);

        foreach (var source in ServiceManager.NaviMapManager.SourceDataDict.OrderBy(x => x.Value.Priority))
        {
            ImGui.PushID(source.Key);
            var sourceDataLocal = new SourceData(source.Value);
            // Values are always applied to sourceDataLocal immediately below (live preview),
            // but the config file is only written once editing on a widget actually finishes
            // (mouse released / edit deactivated) — otherwise dragging a slider or color
            // picker writes the config to disk every single frame.
            var shouldSave = false;
            if (ImGui.BeginListBox($"##list{source.Key}", new Vector2(-1, 210 * ImGuiHelpers.GlobalScale)))
            {
                ImGui.Text(source.Key);

                var enabledLocal = sourceDataLocal.Enabled;
                if (ImGui.Checkbox(Resources.Enabled, ref enabledLocal))
                {
                    sourceDataLocal.Enabled = enabledLocal;
                }
                if (ImGui.IsItemDeactivatedAfterEdit()) shouldSave = true;

                ImGui.SameLine(90);

                var tempPriority = sourceDataLocal.Priority;
                var isPriorityError = false;

                if (source.Key == FinderService.EveryoneKey)
                {
                    ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "  " + Resources.EveryoneLowestPriority);

                }
                else
                {

                    ImGui.PushItemWidth(100);

                    if (ImGui.InputInt("##priority", ref tempPriority, 1))
                    {
                        if (tempPriority < 1)
                        {
                            tempPriority = 1;
                        }

                        if (tempPriority > 99)
                        {
                            tempPriority = 99;
                        }

                        sourceDataLocal.Priority = tempPriority;
                    }
                    if (ImGui.IsItemDeactivatedAfterEdit()) shouldSave = true;
                    isPriorityError = ServiceManager.NaviMapManager.SourceDataDict.Any(x => x.Value.Priority == tempPriority && x.Key != source.Key);

                    ImGui.PopItemWidth();
                    ImGui.SameLine();
                    if (isPriorityError)
                    {
                        ImGui.TextColored(new Vector4(1, 0, 0, 1), string.Format(Resources.PriorityTaken, tempPriority));
                    }
                    else
                    {
                        ImGui.Text(Resources.PriorityDescription);
                    }

                }

                var color = ImGui.ColorConvertU32ToFloat4(source.Value.Color);
                ImGui.Text(Resources.MarkerColourDescription);
                if (ImGui.ColorEdit4("##color", ref color, ImGuiColorEditFlags.NoAlpha))
                {

                    var uintColour = ImGui.ColorConvertFloat4ToU32(color);
                    sourceDataLocal.Color = uintColour;
                    sourceDataLocal.BorderValid = false;

                }
                if (ImGui.IsItemDeactivatedAfterEdit()) shouldSave = true;
                var circleSizeLocal = source.Value.CircleSize;
                if (ImGui.SliderInt(Resources.CircleSize, ref circleSizeLocal, 1, 20))
                {
                    sourceDataLocal.CircleSize = circleSizeLocal;
                }
                if (ImGui.IsItemDeactivatedAfterEdit()) shouldSave = true;

                var border = sourceDataLocal.ShowBorder;
                if (ImGui.Checkbox(Resources.ShowBorder, ref border))
                {
                    sourceDataLocal.ShowBorder = border;
                }
                if (ImGui.IsItemDeactivatedAfterEdit()) shouldSave = true;

                var darkeningAmount = sourceDataLocal.BorderDarkeningAmount;
                if (ImGui.SliderFloat(Resources.BorderBrightness, ref darkeningAmount, 0.0f, 2f))
                {
                    sourceDataLocal.BorderDarkeningAmount = darkeningAmount;
                    sourceDataLocal.BorderValid = false;
                }
                if (ImGui.IsItemDeactivatedAfterEdit()) shouldSave = true;

                var borderRadius = sourceDataLocal.BorderRadius;
                if (ImGui.SliderInt(Resources.BorderRadius, ref borderRadius, 1, 10))
                {
                    sourceDataLocal.BorderRadius = borderRadius;
                }
                if (ImGui.IsItemDeactivatedAfterEdit()) shouldSave = true;

                ServiceManager.NaviMapManager.SourceDataDict.AddOrUpdate(source.Key, sourceDataLocal, (_, _) => sourceDataLocal);
                if (sourceDataLocal != ServiceManager.Configuration.SourceConfigs[source.Key])
                {
                    if (!isPriorityError)
                    {
                        ServiceManager.Configuration.SourceConfigs[source.Key] = sourceDataLocal;

                    }
                    if (shouldSave)
                    {
                        ServiceManager.Configuration.Save();
                    }
                }
                ImGui.EndListBox();

            }
            ImGui.PopID();
        }

    }

    private static void DrawPvpRadarSettings()
    {
        if (!ImGui.CollapsingHeader(Resources.PvpRadarHeader))
        {
            return;
        }

        var config = ServiceManager.Configuration;

        var radarEnabled = config.PvpRadarEnabled;
        if (ImGui.Checkbox(Resources.PvpRadarEnabled, ref radarEnabled))
        {
            config.PvpRadarEnabled = radarEnabled;
            config.Save();
        }

        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), Resources.PvpRadarNotice);

        var outsidePvp = config.PvpRadarOutsidePvp;
        if (ImGui.Checkbox(Resources.PvpRadarOutsidePvp, ref outsidePvp))
        {
            config.PvpRadarOutsidePvp = outsidePvp;
            config.Save();
        }

        var onlyDot = config.PvpRadarOnlyDot;
        if (ImGui.Checkbox(Resources.PvpRadarOnlyDot, ref onlyDot))
        {
            config.PvpRadarOnlyDot = onlyDot;
            config.Save();
        }

        var hideFriendly = config.PvpRadarHideFriendly;
        if (ImGui.Checkbox(Resources.PvpRadarHideFriendly, ref hideFriendly))
        {
            config.PvpRadarHideFriendly = hideFriendly;
            config.Save();
        }

        var dotRadius = config.PvpRadarDotRadius;
        if (ImGui.SliderFloat(Resources.PvpRadarDotRadius, ref dotRadius, 1f, 20f))
        {
            config.PvpRadarDotRadius = dotRadius;
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            config.Save();
        }

        var enemyColor = ImGui.ColorConvertU32ToFloat4(config.PvpRadarEnemyColor);
        if (ImGui.ColorEdit4(Resources.PvpRadarEnemyColor, ref enemyColor, ImGuiColorEditFlags.NoAlpha))
        {
            config.PvpRadarEnemyColor = ImGui.ColorConvertFloat4ToU32(enemyColor);
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            config.Save();
        }

        var friendlyColor = ImGui.ColorConvertU32ToFloat4(config.PvpRadarFriendlyColor);
        if (ImGui.ColorEdit4(Resources.PvpRadarFriendlyColor, ref friendlyColor, ImGuiColorEditFlags.NoAlpha))
        {
            config.PvpRadarFriendlyColor = ImGui.ColorConvertFloat4ToU32(friendlyColor);
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            config.Save();
        }

        // 紛爭前線三方陣營色(標籤走 Lumina GrandCompany sheet,台服自動顯示繁中)
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), Resources.PvpRadarTeamColorsNote);

        var teamMael = ImGui.ColorConvertU32ToFloat4(config.PvpRadarTeamMaelstromColor);
        if (ImGui.ColorEdit4($"{GetGrandCompanyName(1)}##teamMael", ref teamMael, ImGuiColorEditFlags.NoAlpha))
        {
            config.PvpRadarTeamMaelstromColor = ImGui.ColorConvertFloat4ToU32(teamMael);
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            config.Save();
        }

        var teamAdder = ImGui.ColorConvertU32ToFloat4(config.PvpRadarTeamAdderColor);
        if (ImGui.ColorEdit4($"{GetGrandCompanyName(2)}##teamAdder", ref teamAdder, ImGuiColorEditFlags.NoAlpha))
        {
            config.PvpRadarTeamAdderColor = ImGui.ColorConvertFloat4ToU32(teamAdder);
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            config.Save();
        }

        var teamFlames = ImGui.ColorConvertU32ToFloat4(config.PvpRadarTeamFlamesColor);
        if (ImGui.ColorEdit4($"{GetGrandCompanyName(3)}##teamFlames", ref teamFlames, ImGuiColorEditFlags.NoAlpha))
        {
            config.PvpRadarTeamFlamesColor = ImGui.ColorConvertFloat4ToU32(teamFlames);
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            config.Save();
        }

        ImGui.Separator();

        DrawTargetingMeSettings(config);

        ImGui.Separator();
    }

    /// <summary>
    /// 「把我選定為目標」連線層的設定。整組預設關,開啟前的行為與原本完全一樣。
    /// </summary>
    private static void DrawTargetingMeSettings(Configuration config)
    {
        var showTargetingLines = config.PvpRadarShowTargetingMeLines;
        if (ImGui.Checkbox(Resources.PvpRadarShowTargetingMeLines, ref showTargetingLines))
        {
            config.PvpRadarShowTargetingMeLines = showTargetingLines;
            config.Save();
        }

        // 這段講的是「這個顯示看不到什麼」,屬於隨時要看得見的限制,不藏進 tooltip。
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), Resources.PvpRadarTargetingMeNotice);

        var includeGaze = config.PvpRadarTargetingMeIncludeGaze;
        if (ImGui.Checkbox(Resources.PvpRadarTargetingMeIncludeGaze, ref includeGaze))
        {
            config.PvpRadarTargetingMeIncludeGaze = includeGaze;
            config.Save();
        }

        var showCount = config.PvpRadarTargetingMeShowCount;
        if (ImGui.Checkbox(Resources.PvpRadarTargetingMeShowCount, ref showCount))
        {
            config.PvpRadarTargetingMeShowCount = showCount;
            config.Save();
        }

        var lineThickness = config.PvpRadarTargetingMeLineThickness;
        if (ImGui.SliderFloat(Resources.PvpRadarTargetingMeLineThickness, ref lineThickness, 1f, 8f))
        {
            config.PvpRadarTargetingMeLineThickness = lineThickness;
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            config.Save();
        }

        var targetingColor = ImGui.ColorConvertU32ToFloat4(config.PvpRadarTargetingMeLineColor);
        if (ImGui.ColorEdit4(Resources.PvpRadarTargetingMeLineColor, ref targetingColor, ImGuiColorEditFlags.NoAlpha))
        {
            config.PvpRadarTargetingMeLineColor = ImGui.ColorConvertFloat4ToU32(targetingColor);
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            config.Save();
        }
    }

    private static string GetGrandCompanyName(uint rowId)
    {
        var name = ServiceManager.DataManager.GetExcelSheet<Lumina.Excel.Sheets.GrandCompany>().GetRowOrDefault(rowId)?.Name.ExtractText();
        return string.IsNullOrEmpty(name) ? $"Team {rowId}" : name;
    }
}
