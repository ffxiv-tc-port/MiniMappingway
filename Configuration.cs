using Dalamud.Configuration;
using MiniMappingway.Manager;
using MiniMappingway.Model;
using System;
using System.Collections.Generic;

namespace MiniMappingway;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public Dictionary<string, SourceData> SourceConfigs { get; set; } = [];

    public bool Enabled { get; set; } = true;

    // --- PvP 玩家雷達(參考 DailyRoutines FrontlinePlayerRadar 的功能形狀) ---
    // 預設全關;啟用後在 PvP 區域(紛爭前線等)以螢幕標點顯示可接收到的周圍玩家。
    public bool PvpRadarEnabled { get; set; } = false;

    // 在非 PvP 區域也顯示(對應 DR「紛爭前線外仍顯示」)。
    public bool PvpRadarOutsidePvp { get; set; } = false;

    // 僅畫判定點,不顯示玩家名稱(對應 DR「僅顯示人物判定點」)。
    public bool PvpRadarOnlyDot { get; set; } = true;

    // 不顯示友方角色(對應 DR「不顯示友方角色資訊」)。
    public bool PvpRadarHideFriendly { get; set; } = false;

    // 點半徑(對應 DR「點半徑」,DR 預設 5)。
    public float PvpRadarDotRadius { get; set; } = 5f;

    // ImGui U32 色彩(ABGR)。敵對:紅;友方:藍。
    public uint PvpRadarEnemyColor { get; set; } = 0xFF3030DC;

    public uint PvpRadarFriendlyColor { get; set; } = 0xFFDC641E;

    public void Initialize()
    {

    }

    public void Save()
    {
        ServiceManager.DalamudPluginInterface.SavePluginConfig(this);
    }
}
