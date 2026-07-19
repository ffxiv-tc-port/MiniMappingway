using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using MiniMappingway.Manager;
using MiniMappingway.Model;
using MiniMappingway.Service;
using System.Linq;
using System.Numerics;

namespace MiniMappingway.Windows;

public class SettingsWindow : Window
{
    public SettingsWindow() : base("Mini-Mappingway 設定")
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
        if (ImGui.Checkbox("啟用", ref enabled))
        {
            ServiceManager.Configuration.Enabled = enabled;
            ServiceManager.Configuration.Save();
        }

        ImGui.TextColored(new Vector4(255, 0, 0, 255), "目前是透過比對公會標籤來尋找公會成員。");
        ImGui.TextColored(new Vector4(255, 0, 0, 255), "若您的公會標籤較常見，您可能會想停用此功能。");

        ImGui.Text("標記設定（依優先度排序）：");

        foreach (var source in ServiceManager.NaviMapManager.SourceDataDict.OrderBy(x => x.Value.Priority))
        {
            ImGui.PushID(source.Key);
            var sourceDataLocal = new SourceData(source.Value);
            if (ImGui.BeginListBox($"##list{source.Key}", new Vector2(-1, 210 * ImGuiHelpers.GlobalScale)))
            {
                ImGui.Text(source.Key);

                var enabledLocal = sourceDataLocal.Enabled;
                if (ImGui.Checkbox("啟用", ref enabledLocal))
                {
                    sourceDataLocal.Enabled = enabledLocal;
                }

                ImGui.SameLine(90);

                var tempPriority = sourceDataLocal.Priority;
                var isPriorityError = false;

                if (source.Key == FinderService.EveryoneKey)
                {
                    ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "  「所有人」永遠是最低優先度");

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
                        ImGui.TextColored(new Vector4(1, 0, 0, 1), $"優先度 {tempPriority} 已被使用");
                    }
                    else
                    {
                        ImGui.Text("優先度，數值越高顯示在越上層");
                    }

                }

                var color = ImGui.ColorConvertU32ToFloat4(source.Value.Color);
                ImGui.Text("標記顏色。點擊色塊可開啟選色器。");
                if (ImGui.ColorEdit4("##color", ref color, ImGuiColorEditFlags.NoAlpha))
                {

                    var uintColour = ImGui.ColorConvertFloat4ToU32(color);
                    sourceDataLocal.Color = uintColour;
                    sourceDataLocal.BorderValid = false;

                }
                var circleSizeLocal = source.Value.CircleSize;
                if (ImGui.SliderInt("圓圈大小", ref circleSizeLocal, 1, 20))
                {
                    sourceDataLocal.CircleSize = circleSizeLocal;
                }

                var border = sourceDataLocal.ShowBorder;
                if (ImGui.Checkbox("顯示邊框", ref border))
                {
                    sourceDataLocal.ShowBorder = border;
                }

                var darkeningAmount = sourceDataLocal.BorderDarkeningAmount;
                if (ImGui.SliderFloat("邊框亮度", ref darkeningAmount, 0.0f, 2f))
                {
                    sourceDataLocal.BorderDarkeningAmount = darkeningAmount;
                    sourceDataLocal.BorderValid = false;
                }

                var borderRadius = sourceDataLocal.BorderRadius;
                if (ImGui.SliderInt("邊框半徑", ref borderRadius, 1, 10))
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
