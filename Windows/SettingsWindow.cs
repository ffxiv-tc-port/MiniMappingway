using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using ImGuiNET;
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

        ImGui.Text(Resources.MarkerSettingsHeader);

        foreach (var source in ServiceManager.NaviMapManager.SourceDataDict.OrderBy(x => x.Value.Priority))
        {
            ImGui.PushID(source.Key);
            var sourceDataLocal = new SourceData(source.Value);
            if (ImGui.BeginListBox($"##list{source.Key}", new Vector2(-1, 210 * ImGuiHelpers.GlobalScale)))
            {
                ImGui.Text(source.Key);

                var enabledLocal = sourceDataLocal.Enabled;
                if (ImGui.Checkbox(Resources.Enabled, ref enabledLocal))
                {
                    sourceDataLocal.Enabled = enabledLocal;
                }

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
                var circleSizeLocal = source.Value.CircleSize;
                if (ImGui.SliderInt(Resources.CircleSize, ref circleSizeLocal, 1, 20))
                {
                    sourceDataLocal.CircleSize = circleSizeLocal;
                }

                var border = sourceDataLocal.ShowBorder;
                if (ImGui.Checkbox(Resources.ShowBorder, ref border))
                {
                    sourceDataLocal.ShowBorder = border;
                }

                var darkeningAmount = sourceDataLocal.BorderDarkeningAmount;
                if (ImGui.SliderFloat(Resources.BorderBrightness, ref darkeningAmount, 0.0f, 2f))
                {
                    sourceDataLocal.BorderDarkeningAmount = darkeningAmount;
                    sourceDataLocal.BorderValid = false;
                }

                var borderRadius = sourceDataLocal.BorderRadius;
                if (ImGui.SliderInt(Resources.BorderRadius, ref borderRadius, 1, 10))
                {
                    sourceDataLocal.BorderRadius = borderRadius;
                }

                ServiceManager.NaviMapManager.SourceDataDict.AddOrUpdate(source.Key, sourceDataLocal, (_, _) => sourceDataLocal);
                if (sourceDataLocal != ServiceManager.Configuration.SourceConfigs[source.Key])
                {
                    if (!isPriorityError)
                    {
                        ServiceManager.Configuration.SourceConfigs[source.Key] = sourceDataLocal;

                    }
                    ServiceManager.Configuration.Save();
                }
                ImGui.EndListBox();

            }
            ImGui.PopID();
        }

    }
}
