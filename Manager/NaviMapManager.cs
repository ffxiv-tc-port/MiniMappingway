using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Dalamud.Bindings.ImGui;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using MiniMappingway.Model;
using MiniMappingway.Service;
using MiniMappingway.Utility;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace MiniMappingway.Manager;

public unsafe class NaviMapManager : IDisposable
{

    public readonly ConcurrentDictionary<string, ConcurrentDictionary<int, PersonDetails>> PersonDict = new();

    public readonly ConcurrentDictionary<string, SourceData> SourceDataDict = new();

    public int X;

    public int Y;

    public float NaviScale;

    public float ZoneScale;

    public float Rotation;

    public bool Visible;

    public float Zoom;

    public short OffsetX;
    public short OffsetY;

    public bool Loading;

    public bool DebugMode = false;

    public bool IsLocked;

    public bool InCombat { get; set; }

    private AtkUnitBase* NaviMapPointer => (AtkUnitBase*)ServiceManager.GameGui.GetAddonByName("_NaviMap").Address;

    private readonly ExcelSheet<Map>? _maps;

    public readonly ConcurrentDictionary<int, Queue<CircleData>> CircleData = new();

    public NaviMapManager()
    {
        ServiceManager.GameInteropProvider.InitializeFromAttributes(this);

        _maps = ServiceManager.DataManager.GetExcelSheet<Map>();
        UpdateNaviMap();
        UpdateMap();
    }

    public bool AddOrUpdateSource(string sourceName, uint colour)
    {
        if (ServiceManager.Configuration.SourceConfigs.TryGetValue(sourceName, out var source))
        {
            SourceDataDict.AddOrUpdate(sourceName, source, (_, _) => source);
            PersonDict.AddOrUpdate(sourceName, new ConcurrentDictionary<int, PersonDetails>(),
                (_, _) => new ConcurrentDictionary<int, PersonDetails>());
        }
        else
        {
            var sourceData = new SourceData(colour);

            SourceDataDict.AddOrUpdate(sourceName, sourceData, (_, _) => sourceData);
            PersonDict.AddOrUpdate(sourceName, new ConcurrentDictionary<int, PersonDetails>(),
                (_, _) => new ConcurrentDictionary<int, PersonDetails>());

        }

        return true;
    }

    public bool AddOrUpdateSource(string sourceName, Vector4 colour)
    {
        if (ServiceManager.Configuration.SourceConfigs.TryGetValue(sourceName, out var source))
        {
            SourceDataDict.AddOrUpdate(sourceName, source, (_, _) => source);
            PersonDict.AddOrUpdate(sourceName, new ConcurrentDictionary<int, PersonDetails>(),
                (_, _) => new ConcurrentDictionary<int, PersonDetails>());

        }
        else
        {
            var uintColor = ImGui.ColorConvertFloat4ToU32(colour);
            var sourceData = new SourceData(uintColor);
            switch (sourceName)
            {
                case FinderService.EveryoneKey:
                    sourceData.Priority = 0;
                    sourceData.Enabled = false;
                    break;
                case FinderService.FcMembersKey:
                    sourceData.Priority = 1;
                    break;
                case FinderService.FriendKey:
                    sourceData.Priority = 2;
                    break;
                default:
                    sourceData.Priority = GetNextFreePriority();
                    break;
            }

            ServiceManager.Configuration.SourceConfigs.TryAdd(sourceName, sourceData);
            SourceDataDict.AddOrUpdate(sourceName, sourceData, (_, _) => sourceData);
            PersonDict.AddOrUpdate(sourceName, new ConcurrentDictionary<int, PersonDetails>(),
                (_, _) => new ConcurrentDictionary<int, PersonDetails>());
        }
        return true;
    }

    public bool UpdateNaviMap()
    {
        // 原生指標只在這次呼叫內使用,不存進欄位跨幀保留;每次呼叫都重新從 GameGui 解析。
        var naviMap = NaviMapPointer;

        if (naviMap == null)
        {
            return false;
        }

        // UldManager 尚未載入完成時 NodeList / NodeListCount 可能未初始化或已失效,
        // 此時呼叫 GetNodeById 會走到無效節點。所有節點存取都必須排在這個檢查之後。
        if (naviMap->UldManager.LoadedState != AtkLoadState.Loaded)
        {
            return false;
        }

        // 以下每一跳都做 null 與型別檢查,任何一跳失敗就保留前一次的值(fail-closed)。
        // 刻意不使用 try/catch:懸空指標造成的 AccessViolationException 在 .NET Core 屬於
        // corrupted-state exception,try/catch 完全攔不到,加了只會製造假的安全感。

        // 地圖鎖定核取方塊。
        var lockComponent = GetComponentOfNode(naviMap->GetNodeById(4));
        if (IsButtonDerivedComponent(lockComponent))
        {
            IsLocked = ((AtkComponentCheckBox*)lockComponent)->IsChecked;
        }

        var rotationNode = naviMap->GetNodeById(8);
        if (rotationNode != null)
        {
            Rotation = rotationNode->Rotation;
        }

        var zoomComponent = GetComponentOfNode(naviMap->GetNodeById(18));
        if (zoomComponent != null && zoomComponent->UldManager.LoadedState == AtkLoadState.Loaded)
        {
            var zoomImageNode = zoomComponent->GetImageNodeById(6);
            if (zoomImageNode != null)
            {
                Zoom = zoomImageNode->ScaleX;
            }
        }

        X = naviMap->X;
        Y = naviMap->Y;
        NaviScale = naviMap->Scale;
        Visible = naviMap->IsVisible && naviMap->VisibilityFlags == 0;

        return true;
    }

    /// <summary>
    /// 取出 component 節點所掛的元件;節點為 null 或不是 component 節點時回傳 null。
    /// AtkResNode 的結構大小是 0xB0,而 AtkComponentNode.Component 位在 0xB0,
    /// 少了 Type 檢查就會讀到配置範圍外的記憶體。CS 對 component 節點的 Type 一律 >= 1000。
    /// </summary>
    private static AtkComponentBase* GetComponentOfNode(AtkResNode* node)
    {
        if (node == null || (int)node->Type < 1000)
        {
            return null;
        }

        return ((AtkComponentNode*)node)->Component;
    }

    /// <summary>
    /// 元件是不是 AtkComponentButton 衍生型別(結構大小 0xF0)。
    /// IsChecked 讀的是 AtkComponentButton.Flags(位於 0xE8),已經超出 AtkComponentBase
    /// (0xC0)的範圍,所以必須先確認元件型別才可以讀。元件型別取自 uld 的
    /// AtkUldComponentInfo,全程只走已定義的結構欄位,不對任意位址做探測。
    /// </summary>
    private static bool IsButtonDerivedComponent(AtkComponentBase* component)
    {
        if (component == null || component->UldManager.BaseType != AtkUldManagerBaseType.Component)
        {
            return false;
        }

        var info = (AtkUldComponentInfo*)component->UldManager.Objects;
        if (info == null)
        {
            return false;
        }

        return info->ComponentType is ComponentType.Button
                                   or ComponentType.CheckBox
                                   or ComponentType.RadioButton
                                   or ComponentType.ListItemRenderer
                                   or ComponentType.HoldButton;
    }

    public bool CheckIfLoading()
    {
        // addon 沒開時 GetAddonByName 回傳 0,原本的寫法會直接對空指標解參考。
        // 這兩個 addon 都只在切換區域時才存在,不存在即代表沒有在讀取。
        var locationTitle = (AtkUnitBase*)ServiceManager.GameGui.GetAddonByName("_LocationTitle").Address;
        var fadeMiddle = (AtkUnitBase*)ServiceManager.GameGui.GetAddonByName("FadeMiddle").Address;

        return Loading = (locationTitle != null && locationTitle->IsVisible)
                         || (fadeMiddle != null && fadeMiddle->IsVisible);
    }

    public void UpdateMap()
    {
        if (_maps != null)
        {
            try
            {
                var mapId = GetMapId();
                // GetMapId() 取不到 AgentMap 時回 0。Map 第 0 列是佔位列,拿它算出來的
                // ZoneScale／Offset 會是錯的且完全靜默 —— 寧可保留上一次的值,下一次
                // TerritoryChanged 會再更新一次。
                if (mapId == 0)
                {
                    ServiceManager.Log.Information(
                        "[MiniMappingway] AgentMap 尚未就緒,這次不更新地圖比例與位移。");
                    return;
                }

                var map = _maps.GetRow(mapId);

                if (map.SizeFactor != 0)
                {
                    ZoneScale = (float)map.SizeFactor / 100;
                }
                else
                {
                    ZoneScale = 1;
                }
                OffsetX = map.OffsetX;
                OffsetY = map.OffsetY;
            }
            catch (ArgumentOutOfRangeException e)
            {
                return;
            }
        }
    }

    /// <summary>
    /// 取得目前地圖編號;取不到 <c>AgentMap</c> 時回 0(呼叫端必須把 0 當成「不知道」處理)。
    /// </summary>
    /// <remarks>
    /// 🔴 <c>AgentMap.Instance()</c> 由 <c>[Agent(AgentId.Map)]</c> 產生:內部鏈
    /// AgentModule → UIModule → Framework,任一層回 null 整條就回 null(登入前、切場景、
    /// 登出後都是常態),而底層 <c>[StaticAddress]</c>／<c>[MemberFunction]</c> 特徵碼失配時
    /// 改為擲 <c>InvalidOperationException</c>——兩種失效模式並存,只擋一種等於假防護。
    /// 裸解參考 null 原生指標是 AccessViolationException,在 .NET Core 屬 corrupted-state
    /// exception,<c>try/catch</c> 完全攔不到 ⇒ 只能事前判空。
    /// ⚠️ 這條路徑其中一個呼叫端是 <c>NaviMapManager</c> 的建構子,也就是外掛載入的當下 ——
    /// 在標題畫面載入外掛時 AgentMap 必定還不存在,那是最容易踩到的情境。
    /// </remarks>
    private uint GetMapId()
    {
        AgentMap* agent;
        try
        {
            agent = AgentMap.Instance();
        }
        catch
        {
            return 0;
        }

        return agent == null ? 0u : agent->CurrentMapId;
    }

    public bool ClearPersonBag(string sourceName)
    {
        PersonDict.TryGetValue(sourceName, out var dict);
        if (dict == null)
        {
            return false;
        }
        dict.Clear();
        return true;
    }
    public bool OverwriteWholeBag(string sourceName, List<PersonDetails> list)
    {
        ClearPersonBag(sourceName);

        var success = true;

        PersonDict.TryGetValue(sourceName, out var dict);

        if (dict == null)
        {
            return false;
        }

        foreach (var person in list)
        {
            var personIndex = MarkerUtility.GetObjIndexById(person.Id);

            if (personIndex == null)
            {
                continue;
            }
            if (!dict.TryAdd((int)personIndex, person))
            {
                success = false;
            }
        }
        return success;
    }

    public bool AddToBag(PersonDetails details)
    {
        PersonDict.TryGetValue(details.SourceName, out var dict);

        if (dict == null)
        {
            return false;
        }

        var personIndex = MarkerUtility.GetObjIndexById(details.Id);
        if (personIndex == null)
        {
            return false;
        }
        return dict.TryAdd((int)personIndex, details);
    }

    public bool RemoveFromBag(ulong id, string sourceName)
    {
        PersonDict.TryGetValue(sourceName, out var dict);
        if (dict == null)
        {
            return false;
        }
        var entry = dict.First(x => x.Value.Id == id);
        return dict.TryRemove(entry);

    }

    public bool RemoveFromBag(string name, string sourceName)
    {
        PersonDict.TryGetValue(sourceName, out var dict);
        if (dict == null)
        {
            return false;
        }
        var entry = dict.First(x => x.Value.Name == name);
        return dict.TryRemove(entry);

    }

    public bool RemoveSourceAndPeople(string sourceName)
    {
        var successPerson = ClearPersonBag(sourceName);
        var successSource = SourceDataDict.TryRemove(sourceName, out _);

        return successPerson && successSource;
    }

    public void Dispose()
    {
        PersonDict.Clear();
        SourceDataDict.Clear();
    }

    public int GetNextFreePriority()
    {
        for (var i = 0; i < 99; i++)
        {
            if (SourceDataDict.Values.All(x => x.Priority != i))
            {
                return i;
            }
        }

        return 1;
    }
}
