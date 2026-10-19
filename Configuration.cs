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
    // (敵對色同時是 Battalion 陣營無法判定時的退回色)
    public uint PvpRadarEnemyColor { get; set; } = 0xFF3030DC;

    public uint PvpRadarFriendlyColor { get; set; } = 0xFFDC641E;

    // 紛爭前線三方陣營色(依 CharacterData.Battalion 歸屬上色;自己那隊一律用友方色)。
    // 預設代表色:黑渦團=紅、雙蛇黨=黃、恆輝隊=橙。
    public uint PvpRadarTeamMaelstromColor { get; set; } = 0xFF3030DC;

    public uint PvpRadarTeamAdderColor { get; set; } = 0xFF1EC8E6;

    public uint PvpRadarTeamFlamesColor { get; set; } = 0xFF1E8CF0;

    // --- 「把我選定為目標」的連線層(疊在上面的 PvP 玩家雷達之上) ---
    // 預設關,開啟後從每個把我選定為目標的玩家畫一條線到自己。
    public bool PvpRadarShowTargetingMeLines { get; set; } = false;

    // 同時顯示「未確認」的連線:對方的注視(LookAt)目標是我,但硬目標欄位不是。
    // 預設開,因為硬目標欄位若在台服拿不到資料,這是唯一還看得到東西的來源。
    public bool PvpRadarTargetingMeIncludeGaze { get; set; } = true;

    // 在自己身上顯示「有幾個人以我為目標」。數字帶 "+",因為那是下限不是總數。
    public bool PvpRadarTargetingMeShowCount { get; set; } = true;

    // 連線粗細(未確認的連線會自動畫細一點)。
    public float PvpRadarTargetingMeLineThickness { get; set; } = 2f;

    // ImGui U32 色彩(ABGR)。橙黃,和三方陣營色都拉開。
    // 未確認的連線用同一個 RGB、alpha 自動降低,不另外開一個顏色設定。
    public uint PvpRadarTargetingMeLineColor { get; set; } = 0xFF20D0FF;

    public void Initialize()
    {

    }

    public void Save()
    {
        ServiceManager.DalamudPluginInterface.SavePluginConfig(this);
    }
}
