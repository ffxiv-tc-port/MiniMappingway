using Dalamud.Plugin.Ipc;
using MiniMappingway.Manager;
using MiniMappingway.Model;
using System;
using System.Numerics;

namespace MiniMappingway.Api;

/// <summary>
/// For integration over IPC.
/// Each method has comments explaining what the arguments are below.
/// General flowchart for usage is:
/// GetVersion (to see if plugin active/check compatibility),
/// RegisterOrUpdateSourceVec/RegisterOrUpdateSourceUint to register as a source of markers,
/// AddPerson/OverwriteList to add the people you wish to show, whether in bulk or one by one,
/// RemovePersonByName/RemovePersonByUint as needed,
/// RemoveSourceAndPeople to remove your plugin as a source, and remove the list containing the people.
/// 
/// NB: people will be removed from the list automatically if they leave the ObjectTable.
/// Trying to remove a person that isn't in the list is safe, and will return false.
/// Trying to add a person that is already in the list is safe, and will return false.
/// </summary>
/// <remarks>
/// I might add more methods for adding to list of player names instead of specific references to player objects. These would then be scanned by mini-mappingway on your behalf. If you would like this let me know.
/// </remarks>
public class ApiController : IDisposable
{
    private const int ApiVersionMajor = 1;

    /// <summary>
    /// 次版號。1 → 2：<see cref="AddPerson"/> 開始支援<b>非玩家</b>的物件
    /// （寶箱、風脈泉、採集點……）。
    /// </summary>
    /// <remarks>
    /// 🔴 <b>次版號 1 的時候 IPC 端點對非玩家物件是完全靜默地不生效的</b>，成因有兩個，
    /// 兩個都不會擲例外也不會寫記錄：
    /// <list type="number">
    /// <item><c>MarkerUtility.GetObjIndexById</c> 只掃物件表的偶數索引 2..200
    /// （＝CharacterManager 的玩家欄位）⇒ 寶箱／風脈泉那一段（449-488）永遠找不到
    /// ⇒ <see cref="AddPerson"/> 回 false。</item>
    /// <item>就算加得進去，繪製與巡檢兩條路徑都硬性要求 <c>ObjectKind.Player</c>，
    /// 不是玩家的當幀就被移除。</item>
    /// </list>
    /// ⇒ 消費端要顯示非玩家物件時，判準請用<b>次版號 &gt;= 2</b>；
    /// 只用來顯示玩家的消費端維持 &gt;= 1 即可。
    /// </remarks>
    private const int ApiVersionMinor = 2;

    private readonly ICallGateProvider<Tuple<int, int>> _getVersionIpc = ServiceManager.DalamudPluginInterface.GetIpcProvider<Tuple<int, int>>("MiniMappingway.CheckVersion");

    private readonly ICallGateProvider<string, Vector4, bool> _registerOrUpdateSourceVecIpc = ServiceManager.DalamudPluginInterface.GetIpcProvider<string, Vector4, bool>("MiniMappingway.RegisterOrUpdateSourceVec");

    private readonly ICallGateProvider<string, uint, bool> _registerOrUpdateSourceUintIpc = ServiceManager.DalamudPluginInterface.GetIpcProvider<string, uint, bool>("MiniMappingway.RegisterOrUpdateSourceUint");

    //private readonly ICallGateProvider<string, List<PersonDetails>, bool> _overwriteListIpc = ServiceManager.DalamudPluginInterface.GetIpcProvider<string, List<PersonDetails>, bool>("MiniMappingway.OverwriteList");

    private readonly ICallGateProvider<string, string, uint, bool> _addPersonIpc = ServiceManager.DalamudPluginInterface.GetIpcProvider<string, string, uint, bool>("MiniMappingway.AddPerson");

    private readonly ICallGateProvider<string, string, bool> _removePersonByNameIpc = ServiceManager.DalamudPluginInterface.GetIpcProvider<string, string, bool>("MiniMappingway.RemovePersonByName");

    private readonly ICallGateProvider<uint, string, bool> _removePersonByIdIpc = ServiceManager.DalamudPluginInterface.GetIpcProvider<uint, string, bool>("MiniMappingway.RemovePersonByUint");

    private readonly ICallGateProvider<string, bool> _removeSourceAndPeopleIpc = ServiceManager.DalamudPluginInterface.GetIpcProvider<string, bool>("MiniMappingway.RemoveSourceAndPeople");

    public ApiController()
    {
        _getVersionIpc.RegisterFunc(CheckVersion);
        _registerOrUpdateSourceVecIpc.RegisterFunc(RegisterOrUpdateSource);
        _registerOrUpdateSourceUintIpc.RegisterFunc(RegisterOrUpdateSource);
        //_overwriteListIpc.RegisterFunc(OverwriteList);
        _addPersonIpc.RegisterFunc(AddPerson);
        _removePersonByNameIpc.RegisterFunc(RemovePerson);
        _removePersonByIdIpc.RegisterFunc(RemovePerson);
        _removeSourceAndPeopleIpc.RegisterFunc(RemoveSourceAndPeople);
    }

    /// <summary>
    /// Get Version
    /// </summary>
    /// <returns>A tuple of Major and Minor version numbers</returns>
    /// <remarks>
    /// 📌 這一支<b>刻意沒有</b>套 <see cref="IpcFrameworkGate"/>：它只回兩個常數，
    /// 不碰原生記憶體也不碰任何共用集合，在哪條執行緒上跑都一樣。
    /// 消費端通常是在自己的背景工作裡先問版本再決定要不要用，
    /// 不該為此多等一幀。
    /// </remarks>
    private Tuple<int, int> CheckVersion()
    {
        return new Tuple<int, int>(ApiVersionMajor, ApiVersionMinor);
    }

    /// <summary>
    /// Register as a source, or update source data (currently just marker color)
    /// </summary>
    /// <param name="sourceName">Source name string, should be unique to your plugin and human readable, will show in settings pane</param>
    /// <param name="color">Color for markers in vector4 format - this can be overwritten by the user in settings</param>
    /// <returns>Success boolean</returns>
    private bool RegisterOrUpdateSource(string sourceName, Vector4 color)
    {
        return IpcFrameworkGate.Get("MiniMappingway.RegisterOrUpdateSourceVec", () =>
        {
            return ServiceManager.NaviMapManager.AddOrUpdateSource(sourceName, color);
        }, false);
    }

    /// <summary>
    /// Register as a source, or update source data (currently just marker color)
    /// </summary>
    /// <param name="sourceName">Source name string, should be unique to your plugin</param>
    /// <param name="color">Color for markers in uint format - this can be overwritten by the user in settings</param>
    /// <returns>Success boolean</returns>
    private bool RegisterOrUpdateSource(string sourceName, uint color)
    {
        return IpcFrameworkGate.Get("MiniMappingway.RegisterOrUpdateSourceUint", () =>
        {
            return ServiceManager.NaviMapManager.AddOrUpdateSource(sourceName, color);
        }, false);
    }

    //I don't like the below method, I may rewrite it, please don't use it

    /// <summary>
    /// Overwrite all people in list for source
    /// </summary>
    /// <param name="sourceName">Source name</param>
    /// <param name="list">List of people you wish to replace with</param>
    /// <returns>Success boolean</returns>
    //private bool OverwriteList(string sourceName, List<PersonDetails> list)
    //{
    //    return ServiceManager.NaviMapManager.OverwriteWholeBag(sourceName, list);
    //}

    /// <summary>
    /// Add person to list for source
    /// </summary>
    /// <param name="sourceName">Source name</param>
    /// <param name="name">Name of person as seen in ObjectTable</param>
    /// <param name="id">
    /// <c>GameObjectId</c> of the object in the ObjectTable.
    /// ⚠️ This parameter is <c>uint</c> while <c>IGameObject.GameObjectId</c> is <c>ulong</c>:
    /// objects whose id does not fit in 32 bits cannot be addressed through this endpoint
    /// (the call simply returns false).
    /// </param>
    /// <returns>Success boolean</returns>
    /// <remarks>
    /// 📌 Since API 1.2 the object does <b>not</b> have to be a player: treasure coffers,
    /// aether currents and gathering points work too. Entries added through this endpoint are
    /// tracked by <c>GameObjectId</c> and are never reinterpreted as <c>Character*</c>.
    /// <para>
    /// 🔴🔴 <b>整段交回遊戲主執行緒執行</b>（<see cref="IpcFrameworkGate"/>）：這一支要走訪
    /// 物件表<b>兩次</b> —— 這裡的 <c>ObjectTable.SearchById</c>，以及
    /// <c>NaviMapManager.AddToBag</c> 裡的 <c>MarkerUtility.GetObjIndexById</c>
    /// （後者逐格讀 <c>ObjectTable[i]</c>）。而 CallGate 是直接方法呼叫，這一支原本跑在
    /// <b>呼叫端的執行緒</b>上。本 pin 的物件表是每格預先配好一個包裝、存取時就地改寫它的
    /// <c>Address</c> 再交出來（<c>ObjectTable.cs:198-231</c>）⇒ 在別條執行緒上取元素
    /// 可能拿到<b>別人的位址或懸空位址</b>，那是攔不到的 AccessViolationException。
    /// </para>
    /// <para>
    /// 📌 <c>PersonDetails.Ptr</c> 存的是記錄當下的位址，但它<b>從來不被解參考</b> ——
    /// 只在內建來源那條路徑上當「這個 slot 有沒有換人」的身分比對用；
    /// 透過本端點加進來的（<c>AnyObjectKind == true</c>）連比對都改用 <c>GameObjectId</c>，
    /// 所以那一半不需要動。
    /// </para>
    /// </remarks>
    private bool AddPerson(string sourceName, string name, uint id)
    {
        return IpcFrameworkGate.Get("MiniMappingway.AddPerson", () =>
        {
            var person = ServiceManager.ObjectTable.SearchById(id);
            if (person == null)
            {
                return false;
            }
            return ServiceManager.NaviMapManager.AddToBag(
                new PersonDetails(name, id, sourceName, person.Address, anyObjectKind: true));
        }, false);
    }

    /// <summary>
    /// Remove person from list for source by name
    /// </summary>
    /// <param name="name">Name of person as seen in ObjectTable</param>
    /// <param name="sourceName">Name of source</param>
    /// <returns>Success boolean</returns>
    private bool RemovePerson(string name, string sourceName)
    {
        return IpcFrameworkGate.Get("MiniMappingway.RemovePersonByName", () =>
        {
            return ServiceManager.NaviMapManager.RemoveFromBag(name, sourceName);
        }, false);
    }

    /// <summary>
    /// Remove person from list for source by uint
    /// </summary>
    /// <param name="id">Id of person in ObjectTable</param>
    /// <param name="sourceName">Name of source</param>
    /// <returns>Success boolean</returns>
    private bool RemovePerson(uint id, string sourceName)
    {
        return IpcFrameworkGate.Get("MiniMappingway.RemovePersonByUint", () =>
        {
            return ServiceManager.NaviMapManager.RemoveFromBag(id, sourceName);
        }, false);
    }

    /// <summary>
    /// Completely remove source and all associated people
    /// </summary>
    /// <param name="sourceName">Source name</param>
    /// <returns>Success boolean</returns>
    private bool RemoveSourceAndPeople(string sourceName)
    {
        return IpcFrameworkGate.Get("MiniMappingway.RemoveSourceAndPeople", () =>
        {
            return ServiceManager.NaviMapManager.RemoveSourceAndPeople(sourceName);
        }, false);
    }

    public void Dispose()
    {
        _getVersionIpc.UnregisterFunc();
        _registerOrUpdateSourceVecIpc.UnregisterFunc();
        _registerOrUpdateSourceUintIpc.UnregisterFunc();
        //_overwriteListIpc.UnregisterFunc();
        _addPersonIpc.UnregisterFunc();
        _removePersonByNameIpc.UnregisterFunc();
        _removePersonByIdIpc.UnregisterFunc();
        _removeSourceAndPeopleIpc.UnregisterFunc();
    }
}
