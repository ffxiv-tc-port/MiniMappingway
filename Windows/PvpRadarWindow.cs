using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Interface.Windowing;
using MiniMappingway.Manager;
using System.Numerics;

namespace MiniMappingway.Windows;

/// <summary>
/// 全螢幕玩家雷達:把可接收到的周圍玩家以世界座標投影成螢幕標點(敵紅友藍),
/// 主要用於紛爭前線等 PvP 區域(小地圖版本在 PvP 中被遊戲隱藏且本外掛原本直接停畫)。
/// 功能形狀對齊 DailyRoutines 的 FrontlinePlayerRadar:點半徑/僅顯示點/隱藏友方/PvP 外仍顯示。
/// 預設關閉,於設定視窗開啟。
/// </summary>
internal class PvpRadarWindow : Window
{
    public PvpRadarWindow() : base("MMWPvpRadar")
    {
        Flags |= ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground
            | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNavFocus
            | ImGuiWindowFlags.NoSavedSettings;

        ForceMainWindow = true;
        RespectCloseHotkey = false;
        IsOpen = true;
    }

    public override bool DrawConditions()
    {
        var config = ServiceManager.Configuration;
        if (!config.Enabled || !config.PvpRadarEnabled)
        {
            return false;
        }
        if (!ServiceManager.ClientState.IsLoggedIn)
        {
            return false;
        }
        if (!ServiceManager.ClientState.IsPvPExcludingDen && !config.PvpRadarOutsidePvp)
        {
            return false;
        }
        if (ServiceManager.NaviMapManager.CheckIfLoading())
        {
            return false;
        }

        return true;
    }

    public override void PreDraw()
    {
        var viewport = ImGui.GetMainViewport();
        Position = viewport.Pos;
        Size = viewport.Size;
    }

    public override void Draw()
    {
        var config = ServiceManager.Configuration;
        var drawList = ImGui.GetWindowDrawList();

        var local = ServiceManager.ObjectTable[0];
        if (local == null)
        {
            return;
        }

        // 紛爭前線是三方陣營戰:以遊戲自身的敵我判定欄位 CharacterData.Battalion
        // (CS 註解:used for determining friend/enemy state)歸隊上色。
        var inPvp = ServiceManager.ClientState.IsPvPExcludingDen;
        var localBattalion = GetBattalion(local);

        foreach (var obj in ServiceManager.ObjectTable)
        {
            if (obj is not IPlayerCharacter pc)
            {
                continue;
            }
            if (pc.GameObjectId == local.GameObjectId)
            {
                continue;
            }
            if (pc.CurrentHp == 0)
            {
                continue;
            }

            var battalion = GetBattalion(pc);
            bool friendly;
            uint color;

            if (inPvp && localBattalion is >= 0 and <= 2 && battalion is >= 0 and <= 2)
            {
                // 三方陣營模式:自己那隊=友方色,其餘兩隊各自陣營色
                friendly = battalion == localBattalion;
                color = friendly
                    ? config.PvpRadarFriendlyColor
                    : battalion switch
                    {
                        0 => config.PvpRadarTeamMaelstromColor,
                        1 => config.PvpRadarTeamAdderColor,
                        _ => config.PvpRadarTeamFlamesColor,
                    };
            }
            else
            {
                // 拿不到陣營資料(或非 PvP 區):退回敵對/友方二分
                var hostile = pc.StatusFlags.HasFlag(StatusFlags.Hostile);
                friendly = !hostile;
                color = hostile ? config.PvpRadarEnemyColor : config.PvpRadarFriendlyColor;
            }

            if (friendly && config.PvpRadarHideFriendly)
            {
                continue;
            }

            // WorldToScreen 已含主 viewport 位移,回傳即為全域 ImGui 座標
            if (!ServiceManager.GameGui.WorldToScreen(pc.Position, out var screenPos))
            {
                continue;
            }

            drawList.AddCircleFilled(screenPos, config.PvpRadarDotRadius, color);
            drawList.AddCircle(screenPos, config.PvpRadarDotRadius, 0xFF000000, 0, 1.5f);

            if (!config.PvpRadarOnlyDot)
            {
                var name = pc.Name.TextValue;
                if (name.Length == 0)
                {
                    continue;
                }
                var textSize = ImGui.CalcTextSize(name);
                var textPos = screenPos + new Vector2(-textSize.X / 2f, config.PvpRadarDotRadius + 2f);
                drawList.AddText(textPos + new Vector2(1f, 1f), 0xFF000000, name);
                drawList.AddText(textPos, color, name);
            }
        }
    }

    /// <summary>
    /// 讀取 CS CharacterData.Battalion(遊戲用來判定敵我/隊伍歸屬的欄位)。
    /// 讀不到時回傳 -1,呼叫端退回敵對/友方二分。
    /// </summary>
    private static unsafe int GetBattalion(Dalamud.Game.ClientState.Objects.Types.IGameObject obj)
    {
        var ch = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)obj.Address;
        if (ch == null)
        {
            return -1;
        }

        return ch->CharacterData.Battalion;
    }
}
