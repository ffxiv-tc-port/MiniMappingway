using System;

namespace MiniMappingway.Model;

public class PersonDetails
{
    public string Name { get; }

    public ulong Id { get; }

    public string SourceName { get; }

    public IntPtr Ptr { get; }

    /// <summary>
    /// 這一筆允許不是玩家嗎（寶箱、風脈泉、採集點……）。
    /// </summary>
    /// <remarks>
    /// 🔴 <b>false 的那條路徑會把物件當成 <c>Character*</c> 解參考</b>
    /// （讀 <c>IsPartyMember</c>／<c>IsAllianceMember</c>，那兩個欄位位在 <c>Character</c> 的深處）。
    /// 非玩家物件的配置比 <c>Character</c> 小很多，那樣讀會落在配置範圍外 ——
    /// AccessViolationException 在 .NET Core 是 corrupted-state exception，<c>try</c>/<c>catch</c> 攔不到。
    /// ⇒ 這個旗標是<b>安全開關</b>不是顯示選項：true 的路徑一個 <c>Character*</c> 都不轉，
    /// 身分比對改用 <c>GameObjectId</c>。
    /// <para>
    /// 📌 內建的三個來源（好友／部隊／所有人）一律 false；
    /// 只有透過 IPC 加進來的（<c>MiniMappingway.AddPerson</c>）才是 true。
    /// </para>
    /// </remarks>
    public bool AnyObjectKind { get; }

    /// <summary>
    /// 內建來源加入當下，角色名字欄位的原始位元組（IPC 加進來的沒有）。
    /// 位元組沒變 ⇒ 轉出來的名字必然沒變，檢查「還是不是同一個人」時就不必每次重轉字串。
    /// </summary>
    internal byte[]? NameBytes { get; init; }

    public PersonDetails(string name, ulong id, string sourceName, IntPtr ptr)
        : this(name, id, sourceName, ptr, false)
    {
    }

    public PersonDetails(string name, ulong id, string sourceName, IntPtr ptr, bool anyObjectKind)
    {
        Name = name;
        Id = id;
        SourceName = sourceName;
        Ptr = ptr;
        AnyObjectKind = anyObjectKind;
    }
}
