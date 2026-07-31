using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using MiniMappingway.Manager;
using MiniMappingway.Model;
using MiniMappingway.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace MiniMappingway.Service;

public sealed class FinderService : IDisposable
{
    public const string FcMembersKey = "FC Members";
    public const string FriendKey = "Friends";
    public const string EveryoneKey = "Everyone";
    private readonly IEnumerable<int> _enumerable;
    private IEnumerator<int> _enumerator;
    private int _index;

    public FinderService()
    {
        ServiceManager.NaviMapManager.AddOrUpdateSource(FriendKey, new Vector4(0.957f, 0.533f, 0.051f, 1));
        ServiceManager.NaviMapManager.AddOrUpdateSource(FcMembersKey, new Vector4(1, 0, 0, 1));
        ServiceManager.NaviMapManager.AddOrUpdateSource(EveryoneKey, new Vector4(0, 0.7f, 0.7f, 1));
        ServiceManager.Framework.Update += Iterate;

        _enumerable = Enumerable.Range(2, 200).Where(x => x % 2 == 0);
        _enumerator = _enumerable.GetEnumerator();
        _enumerator.MoveNext();
        _index = _enumerator.Current;

    }

    private void Iterate(IFramework framework)
    {

        CheckNewPeople(_index);
        CheckSamePerson(_index);

        var iteratorValid = _enumerator.MoveNext();
        if (!iteratorValid)
        {
            _enumerator = _enumerable.GetEnumerator();
            _enumerator.MoveNext();

        }
        _index = _enumerator.Current;
    }

    private static void CheckSamePerson(in int i)
    {
        foreach (var dict in ServiceManager.NaviMapManager.PersonDict)
        {
            if (!ServiceManager.NaviMapManager.SourceDataDict[dict.Key].Enabled)
            {
                continue;
            }
            dict.Value.TryGetValue(i, out var person);
            if (person == null)
            {
                continue;
            }

            // ⚠️ 每次都從物件表重查,不要解參考 PersonDetails.Ptr。
            // Ptr 是這個人被記錄「當下」的原始位址;之後 slot 可能被釋放或換人,
            // 而原本的守衛(ObjectTable[i] != null)只說明該索引有東西、
            // 不代表還是同一個人,名稱比對又排在解參考之後。
            // 下面的 try/catch 也不是防護:AccessViolationException 在 .NET Core
            // 是 corrupted-state exception,catch 不到。
            var current = ServiceManager.ObjectTable[i];
            if (current == null)
            {
                continue;
            }

            // 位址不符代表這個 slot 已經換人,原本記錄的位址不可再解參考。
            if (current.Address != person.Ptr)
            {
                ServiceManager.NaviMapManager.RemoveFromBag(person.Id, dict.Key);
                continue;
            }

            unsafe
            {
                // 走這次重查到的位址(本幀剛從物件表解析出來,確定是活的)。
                var charPointer = (Character*)current.Address;
                if ((byte)charPointer->GameObject.ObjectKind != (byte)ObjectKind.Player)
                {
                    ServiceManager.NaviMapManager.RemoveFromBag(person.Id, dict.Key);
                    continue;
                }
            }

            if (current.Name.ToString() != person.Name)
            {
                ServiceManager.NaviMapManager.RemoveFromBag(person.Id, dict.Key);
            }
        }
    }
    private static void CheckNewPeople(in int i)
    {
        if (MarkerUtility.ChecksPassed)
        {
            LookFor(i);
        }
    }

    private static unsafe void LookFor(int i)
    {
        ServiceManager.Configuration.SourceConfigs.TryGetValue(FriendKey, out var friendConfig);
        ServiceManager.Configuration.SourceConfigs.TryGetValue(FcMembersKey, out var fcConfig);
        ServiceManager.Configuration.SourceConfigs.TryGetValue(EveryoneKey, out var everyoneConfig);
        if (fcConfig == null || friendConfig == null || everyoneConfig == null)
        {
            return;
        }
        if (!fcConfig.Enabled && !friendConfig.Enabled && !everyoneConfig.Enabled)
        {
            return;
        }

        Span<byte> fc = null;
        try
        {
            if (ServiceManager.ObjectTable[0] is null)
            {
                return;
            }
            var player = (Character*)ServiceManager.ObjectTable[0]?.Address;
            if (player == null)
            {
                return;
            }

            ServiceManager.NaviMapManager.InCombat = player->InCombat;
            if (ServiceManager.NaviMapManager.InCombat)
            {
                return;
            }
            if (!player->FreeCompanyTagString.IsNullOrEmpty())
            {
                fc = player->FreeCompanyTag;
            }
        }
        catch
        {
            // ignored
        }

        var alreadyInFriendBag = false;
        var alreadyInFcBag = false;
        var obj = ServiceManager.ObjectTable[i];

        if (obj == null) { return; }

        ServiceManager.NaviMapManager.PersonDict.TryGetValue(FriendKey, out var friendDict);
        ServiceManager.NaviMapManager.PersonDict.TryGetValue(FcMembersKey, out var fcDict);

        if (friendDict == null || fcDict == null)
        {
            return;
        }

        if (friendDict.Any(x => x.Value.Id == obj.GameObjectId) && friendConfig.Enabled)
        {
            alreadyInFriendBag = true;
        }

        if (fcDict.Any(x => x.Value.Id == obj.GameObjectId) && fcConfig.Enabled)
        {
            alreadyInFcBag = true;
        }

        if (alreadyInFcBag && alreadyInFriendBag)
        {
            return;
        }

        var ptr = obj.Address;
        var charPointer = (Character*)ptr;
        if ((byte)charPointer->GameObject.ObjectKind != (byte)ObjectKind.Player)
        {
            return;
        }
        if ((byte)charPointer->GameObject.ObjectKind == (byte)ObjectKind.BattleNpc)
        {
            return;
        }

        if (charPointer->IsAllianceMember && charPointer->IsPartyMember)
        {
            return;
        }

        if (friendConfig.Enabled && !alreadyInFriendBag)
        {
            if (charPointer->IsFriend)
            {
                var personDetails = new PersonDetails(obj.Name.ToString(), obj.GameObjectId, FriendKey, obj.Address);
                alreadyInFriendBag = true;
                ServiceManager.NaviMapManager.AddToBag(personDetails);

            }
        }

        if (fcConfig.Enabled && !alreadyInFcBag)
        {
            if (fc != null)
            {
                var tempFc = charPointer->FreeCompanyTag;
                var playerFc = fc;
                if (playerFc.SequenceEqual(tempFc))
                {

                    var personDetails = new PersonDetails(obj.Name.ToString(), obj.GameObjectId, FcMembersKey, obj.Address);
                    alreadyInFcBag = true;
                    ServiceManager.NaviMapManager.AddToBag(personDetails);
                }
            }
        }

        if (!alreadyInFcBag && !alreadyInFriendBag && everyoneConfig.Enabled)
        {
            var personDetails = new PersonDetails(obj.Name.ToString(), obj.GameObjectId, EveryoneKey, obj.Address);

            ServiceManager.NaviMapManager.AddToBag(personDetails);

        }
    }

    public void Dispose()
    {
        ServiceManager.Framework.Update -= Iterate;
    }
}
