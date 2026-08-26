using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using MiniMappingway.Manager;
using MiniMappingway.Properties;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace MiniMappingway.Windows;

/// <summary>
/// 全螢幕玩家雷達:把可接收到的周圍玩家以世界座標投影成螢幕標點(敵紅友藍),
/// 主要用於紛爭前線等 PvP 區域(小地圖版本在 PvP 中被遊戲隱藏且本外掛原本直接停畫)。
/// 功能形狀對齊 DailyRoutines 的 FrontlinePlayerRadar:點半徑/僅顯示點/隱藏友方/PvP 外仍顯示。
/// 另可疊一層「把我選定為目標」的連線(預設關)。
/// 純顯示:全程只讀 ObjectTable 已解析好的資料,不施放、不走位、不做任何自動化。
/// 預設關閉,於設定視窗開啟。
/// </summary>
internal class PvpRadarWindow : Window
{
    /// <summary>
    /// 「這個玩家有沒有把我選定為目標」的判定結果。
    /// ⚠️ 必須有零值:欄位/陣列的 default 會落在這裡。
    /// </summary>
    private enum TargetingMe : byte
    {
        /// <summary>沒有,或是判定不出來(兩者在客戶端無法區分)。</summary>
        No = 0,

        /// <summary>對方的硬目標欄位就是我 —— 已確認。</summary>
        Confirmed = 1,

        /// <summary>對方的注視目標是我,但硬目標不是 —— 只能算「可能」。</summary>
        Gaze = 2,
    }

    /// <summary>未確認連線的 alpha 係數,用來和已確認的連線拉開。</summary>
    private const float GazeAlphaFactor = 0.45f;

    private static readonly TimeSpan TargetingDiagInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 每幀重建的繪製資料。
    /// 🔴 只允許放實值型別與 string:這個 List 是欄位、會跨幀存活,
    /// 放進 IGameObject 或 IntPtr 就等於跨幀保存原生指標(位址在建構時凍結、永不重解析)。
    /// </summary>
    private readonly struct RadarEntry
    {
        public RadarEntry(Vector2 screenPos, uint color, string? name, TargetingMe targeting)
        {
            ScreenPos = screenPos;
            Color = color;
            Name = name;
            Targeting = targeting;
        }

        public Vector2 ScreenPos { get; }

        public uint Color { get; }

        public string? Name { get; }

        public TargetingMe Targeting { get; }
    }

    private readonly List<RadarEntry> _entries = new(64);

    // 🔴 效能：「誰在鎖定我」不需要每幀重算。前線最多 72 人，每幀對每個人做多層結構
    // 解參考（Character->LookAt.Controller.Params[0]）在高幀率下是每秒近萬次，實機回報會影響
    // 遊戲流暢度。目標關係以人類反應時間變化，節流到 TargetingScanIntervalMs 完全夠用；
    // 位置與畫點仍然每幀更新，所以點不會延遲、只有「線」的歸屬最多慢一個間隔。
    private const int TargetingScanIntervalMs = 200;

    private long _nextTargetingScanTick;

    private readonly Dictionary<ulong, TargetingMe> _targetingCache = new(64);

    private int _cachedScanned;

    private int _cachedConfirmed;

    private int _cachedGaze;

    private int _cachedZeroHard;

    private DateTime _lastTargetingDiag = DateTime.MinValue;

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
        var localId = local.GameObjectId;

        // 連線的終點是自己,所以先確認自己投影得出來;投影不到就整層不畫。
        var localScreenPos = Vector2.Zero;
        var drawTargetingLines = false;
        if (config.PvpRadarShowTargetingMeLines
            && ServiceManager.GameGui.WorldToScreen(local.Position, out var localProjected))
        {
            localScreenPos = localProjected;
            drawTargetingLines = true;
        }

        // 目標關係節流：時間到才真的重掃，否則沿用上一次的快取（見欄位處的說明）。
        var now = Environment.TickCount64;
        var rescanTargeting = config.PvpRadarShowTargetingMeLines && now >= _nextTargetingScanTick;
        if (rescanTargeting)
        {
            _nextTargetingScanTick = now + TargetingScanIntervalMs;
            _targetingCache.Clear();
            _cachedScanned = 0;
            _cachedConfirmed = 0;
            _cachedGaze = 0;
            _cachedZeroHard = 0;
        }


        _entries.Clear();

        foreach (var obj in ServiceManager.ObjectTable)
        {
            if (obj is not IPlayerCharacter pc)
            {
                continue;
            }
            if (pc.GameObjectId == localId)
            {
                continue;
            }
            if (pc.CurrentHp == 0)
            {
                continue;
            }

            // 目標判定刻意放在可見性過濾「之前」:被「不顯示友方」濾掉、或投影不到螢幕上
            // (例如在身後)的人一樣要算進人數,否則畫面會把「看不到」講成「沒有人」。
            var targeting = TargetingMe.No;
            if (config.PvpRadarShowTargetingMeLines)
            {
                if (rescanTargeting)
                {
                    _cachedScanned++;
                    ReadTargetIds(pc, out var hardTargetId, out var gazeTargetId);

                    if (hardTargetId == 0)
                    {
                        _cachedZeroHard++;
                    }

                    if (hardTargetId == localId)
                    {
                        targeting = TargetingMe.Confirmed;
                        _cachedConfirmed++;
                    }
                    else if (gazeTargetId == localId)
                    {
                        targeting = TargetingMe.Gaze;
                        _cachedGaze++;
                    }

                    _targetingCache[pc.GameObjectId] = targeting;
                }
                else if (!_targetingCache.TryGetValue(pc.GameObjectId, out targeting))
                {
                    // 這一幀新出現、還沒被掃到的人：當「不知道」處理，不畫線。
                    targeting = TargetingMe.No;
                }
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

            string? name = null;
            if (!config.PvpRadarOnlyDot)
            {
                var nameText = pc.Name.TextValue;
                if (nameText.Length != 0)
                {
                    name = nameText;
                }
            }

            _entries.Add(new RadarEntry(screenPos, color, name, targeting));
        }

        // 先畫連線再畫標點:連線是背景資訊,不應該蓋住判定點。
        if (drawTargetingLines)
        {
            DrawTargetingLines(drawList, config, localScreenPos);
        }

        foreach (var entry in _entries)
        {
            drawList.AddCircleFilled(entry.ScreenPos, config.PvpRadarDotRadius, entry.Color);
            drawList.AddCircle(entry.ScreenPos, config.PvpRadarDotRadius, 0xFF000000, 0, 1.5f);

            if (entry.Name != null)
            {
                var textSize = ImGui.CalcTextSize(entry.Name);
                var textPos = entry.ScreenPos + new Vector2(-textSize.X / 2f, config.PvpRadarDotRadius + 2f);
                drawList.AddText(textPos + new Vector2(1f, 1f), 0xFF000000, entry.Name);
                drawList.AddText(textPos, entry.Color, entry.Name);
            }
        }

        if (drawTargetingLines && config.PvpRadarTargetingMeShowCount)
        {
            var known = _cachedConfirmed + (config.PvpRadarTargetingMeIncludeGaze ? _cachedGaze : 0);
            if (known > 0)
            {
                DrawTargetingCount(drawList, config, localScreenPos, known);
            }
        }

        LogTargetingDiagnostics(config, _cachedScanned, _cachedConfirmed, _cachedGaze, _cachedZeroHard);
    }

    /// <summary>
    /// 從每個把我選定為目標的玩家畫一條線到自己。
    /// 顯示風格對齊 NecroLens:有方向(已確認的畫箭頭)、有外框(黑線墊底)、不對顏色做疊加。
    /// 「已確認」與「可能」的兩態區分參考 PalacePal:同一個色相,未確認的畫細、alpha 降低、不給箭頭。
    /// </summary>
    private void DrawTargetingLines(ImDrawListPtr drawList, Configuration config, Vector2 localScreenPos)
    {
        var baseColor = config.PvpRadarTargetingMeLineColor;
        var gazeColor = WithAlphaFactor(baseColor, GazeAlphaFactor);
        var baseThickness = config.PvpRadarTargetingMeLineThickness;
        var includeGaze = config.PvpRadarTargetingMeIncludeGaze;

        foreach (var entry in _entries)
        {
            var confirmed = entry.Targeting == TargetingMe.Confirmed;
            if (!confirmed && (entry.Targeting != TargetingMe.Gaze || !includeGaze))
            {
                continue;
            }

            var color = confirmed ? baseColor : gazeColor;
            var thickness = confirmed ? baseThickness : baseThickness * 0.6f;
            var outlineColor = WithAlphaFactor(0xFF000000, confirmed ? 1f : GazeAlphaFactor);

            drawList.AddLine(entry.ScreenPos, localScreenPos, outlineColor, thickness + 2f);
            drawList.AddLine(entry.ScreenPos, localScreenPos, color, thickness);

            if (confirmed)
            {
                DrawArrowHead(drawList, entry.ScreenPos, localScreenPos, color, thickness, config.PvpRadarDotRadius);
            }
        }
    }

    /// <summary>
    /// 在自己頭上顯示人數。
    /// 🔴 尾綴的 "+" 是刻意的:ObjectTable 只有客戶端實際收得到的對象,
    /// 這個數字是下限,不是「總共有幾個人選我」。畫成純數字會直接誤導使用者。
    /// </summary>
    private static void DrawTargetingCount(ImDrawListPtr drawList, Configuration config, Vector2 localScreenPos, int known)
    {
        var label = string.Format(Resources.PvpRadarTargetingMeCount, known);
        var textSize = ImGui.CalcTextSize(label);
        var textPos = localScreenPos + new Vector2(-textSize.X / 2f, -(config.PvpRadarDotRadius + textSize.Y + 6f));

        drawList.AddText(textPos + new Vector2(1f, 1f), 0xFF000000, label);
        drawList.AddText(textPos, config.PvpRadarTargetingMeLineColor, label);
    }

    /// <summary>
    /// 在靠近自己的那一端畫一個指向自己的箭頭,讓「誰指向誰」不需要靠顏色就看得出來。
    /// </summary>
    private static void DrawArrowHead(ImDrawListPtr drawList, Vector2 from, Vector2 to, uint color, float thickness, float dotRadius)
    {
        var delta = to - from;
        var length = delta.Length();
        var headLength = MathF.Max(8f, thickness * 3.5f);

        // 線比箭頭還短就不畫,否則箭頭會反過來蓋掉整條線。
        if (length < headLength + dotRadius + 2f)
        {
            return;
        }

        var direction = delta / length;
        var perpendicular = new Vector2(-direction.Y, direction.X) * (headLength * 0.4f);
        var tip = to - (direction * (dotRadius + 2f));
        var back = tip - (direction * headLength);

        drawList.AddTriangleFilled(back + perpendicular, back - perpendicular, tip, color);
    }

    /// <summary>
    /// 把 ImGui U32(ABGR)的 alpha 乘上係數,RGB 不動。
    /// 用來由單一顏色設定推出「未確認」的暗色,而不是再開一個顏色設定。
    /// </summary>
    private static uint WithAlphaFactor(uint color, float factor)
    {
        var alpha = (uint)MathF.Round(((color >> 24) & 0xFF) * factor);
        if (alpha > 0xFF)
        {
            alpha = 0xFF;
        }

        return (color & 0x00FFFFFF) | (alpha << 24);
    }

    /// <summary>
    /// 讀取「這個角色把誰當目標」。
    /// 兩個來源都是 Character 結構內的**行內欄位**:不解參考次級指標、不呼叫任何特徵碼函式,
    /// 所以不會因為台服簽章對不上而變成呼叫位址 0(那種失敗是 AVE,try/catch 攔不到)。
    ///  * hardTargetId = Character.TargetId(0x22F8,結構大小 0x2360,在界內)。
    ///    CS 註記這個欄位「不會為本機玩家設定」,而呼叫端一定已經排除自己,剛好合用。
    ///  * gazeTargetId = LookAt 注視目標。⚠️ 那個位置是 union:只有 Type == GameObjectId 時
    ///    才是物件 ID,Type 是 2/3 時同一塊記憶體是 Vector3 座標。
    ///    Dalamud 的 IPlayerCharacter.TargetObjectId 沒有檢查 Type 就直接當 ID 讀,
    ///    所以這裡自己判別,刻意不走那個屬性。
    /// 讀不到時兩者都回 0 —— 0 代表「不知道」,不代表「沒有目標」。
    /// </summary>
    private static unsafe void ReadTargetIds(Dalamud.Game.ClientState.Objects.Types.IGameObject obj, out ulong hardTargetId, out ulong gazeTargetId)
    {
        hardTargetId = 0;
        gazeTargetId = 0;

        var ch = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)obj.Address;
        if (ch == null)
        {
            return;
        }

        hardTargetId = ch->TargetId;

        var gaze = ch->LookAt.Controller.Params[0].TargetParam;
        if (gaze.Type == CharacterLookAtTargetParam.TargetInfoType.GameObjectId)
        {
            gazeTargetId = gaze.TargetId;
        }
    }

    /// <summary>
    /// 節流的 Information 級診斷(使用者跑 LogLevel 2,Debug/Verbose 收不到)。
    /// 最關鍵的欄位是 zeroHardTarget:如果它長期等於 scannedPlayers,代表本機端根本沒有
    /// 其他玩家的目標資料(或欄位對不上台服),而不是「真的沒人選我為目標」。
    /// </summary>
    private void LogTargetingDiagnostics(Configuration config, int scannedPlayers, int confirmedCount, int gazeCount, int zeroHardTarget)
    {
        if (!config.PvpRadarShowTargetingMeLines || scannedPlayers == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now - _lastTargetingDiag < TargetingDiagInterval)
        {
            return;
        }

        _lastTargetingDiag = now;

        ServiceManager.Log.Information(
            "PvP 雷達目標連線:掃描 {Scanned} 名玩家,已確認以我為目標 {Confirmed},注視我 {Gaze},硬目標欄位為 0 的 {Zero}",
            scannedPlayers,
            confirmedCount,
            gazeCount,
            zeroHardTarget);
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
