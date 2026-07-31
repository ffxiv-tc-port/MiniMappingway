using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Bindings.ImGui;
using MiniMappingway.Manager;
using MiniMappingway.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Character = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace MiniMappingway.Utility;

internal static class MarkerUtility
{
    private static readonly int NaviMapSize = 218;

    public static Vector2 MapSize;
    public static Vector2 MapPos;
    public static Vector2 WindowPos;
    public static Vector2 PlayerPos = new(0, 0);
    public static Vector2 PlayerCirclePos;

    private static float _minimapRadius;

    public static bool ChecksPassed;

    private static unsafe CircleData? CalculateCirclePosition(this KeyValuePair<int, PersonDetails> person)
    {
        // ⚠️ 不要拿 PersonDetails.Ptr(建立時存下的原始指標)去解參考。
        // 那個位址是當初記錄的,物件表的 slot 之後可能被別人重用或釋放;
        // CreateObjectReference(舊指標) 只是把它包起來,之後每次讀屬性都是在
        // 解參考一根可能已經死掉的指標 → 攔不到的 AccessViolation
        // (corrupted-state exception,try/catch 無效)。
        // 原本的守衛也不成立:ObjectTable[person.Key] != null 只說明那個索引
        // 「有東西」,不代表還是同一個人;而名稱比對排在解參考之後。
        // 正解:每次都從物件表以索引重查,並用 Address 比對確認 slot 沒被換人,
        // 之後所有解參考都走這次重查到的位址。
        var personObj = ServiceManager.ObjectTable[person.Key];

        if (personObj == null || personObj.Address != person.Value.Ptr
            || (byte)((Character*)personObj.Address)->GameObject.ObjectKind != (byte)ObjectKind.Player)
        {
            ServiceManager.NaviMapManager.RemoveFromBag(person.Value.Id, person.Value.SourceName);
            return null;
        }

        var isPartyMember = ((Character*)personObj.Address)->IsAllianceMember || ((Character*)personObj.Address)->IsPartyMember;

        if (isPartyMember)
        {
            return null;
        }

        //Calculate the relative position in world coords
        var relativePersonPos = new Vector2(0, 0)
        {
            X = PlayerPos.X - personObj.Position.X,
            Y = PlayerPos.Y - personObj.Position.Z
        };

        //Account for various scales that can affect the minimap
        relativePersonPos *= ServiceManager.NaviMapManager.ZoneScale;
        relativePersonPos *= ServiceManager.NaviMapManager.NaviScale;
        relativePersonPos *= ServiceManager.NaviMapManager.Zoom;

        //The Circle position for the "person" should be the players circle position minus the relativePosition of the person
        var personCirclePos = PlayerCirclePos - relativePersonPos;

        //if the minimap is unlocked, rotate circles around the player (the center of the minimap)
        if (!ServiceManager.NaviMapManager.IsLocked)
        {
            personCirclePos = RotateForMiniMap(PlayerCirclePos, personCirclePos, ServiceManager.NaviMapManager.Rotation);
        }

        //If the circle would leave the minimap, clamp it to the minimap radius
        var distance = Vector2.Distance(PlayerCirclePos, personCirclePos);
        if (distance > _minimapRadius)
        {
            var originToObject = personCirclePos - PlayerCirclePos;
            originToObject *= _minimapRadius / distance;
            personCirclePos = PlayerCirclePos + originToObject;
        }

        return new CircleData(personCirclePos, person.Value.SourceName);
    }

    private static Vector2 RotateForMiniMap(Vector2 center, Vector2 pos, float angle)
    {
        var angleInRadians = angle;
        var cosTheta = Math.Cos(angleInRadians);
        var sinTheta = Math.Sin(angleInRadians);

        var rotatedPoint = pos;

        rotatedPoint.X = (float)(cosTheta * (pos.X - center.X) -
        sinTheta * (pos.Y - center.Y) + center.X);

        rotatedPoint.Y = (float)
        (sinTheta * (pos.X - center.X) +
        cosTheta * (pos.Y - center.Y) + center.Y);

        return rotatedPoint;
    }

    public static void PrepareDrawOnMinimap()
    {
        //Get ffxiv window position on screen
        WindowPos = ImGui.GetWindowViewport().Pos;

        //Player Circle position will always be center of the minimap, this is also our pivot point
        PlayerCirclePos = new Vector2(ServiceManager.NaviMapManager.X + MapSize.X / 2, ServiceManager.NaviMapManager.Y + MapSize.Y / 2) + WindowPos;

        //to line up with minimap pivot better
        PlayerCirclePos.Y -= 5f;

        foreach (var dict in ServiceManager.NaviMapManager.PersonDict)
        {
            var priority = ServiceManager.NaviMapManager.SourceDataDict[dict.Key].Priority;

            if (!ServiceManager.NaviMapManager.CircleData.ContainsKey(priority))
            {
                ServiceManager.NaviMapManager.CircleData[priority] = new Queue<CircleData>();
            }
            foreach (var person in dict.Value)
            {
                var marker = person.CalculateCirclePosition();
                if (marker != null)
                {
                    ServiceManager.NaviMapManager.CircleData[priority].Enqueue(marker);
                }
            }
        }

        ////for debugging center point
        //ServiceManager.NaviMapManager.CircleData.Add(new CircleData(playerCirclePos, circleCategory));
    }

    public static bool RunChecks()
    {
        if (!ServiceManager.ClientState.IsLoggedIn
            || !ServiceManager.Configuration.Enabled)
        {
            return false;
        }
        if (!ServiceManager.NaviMapManager.UpdateNaviMap())
        {
            ChecksPassed = false;
            return false;
        }

        if (!ServiceManager.NaviMapManager.Visible)
        {
            return false;
        }
        if (ServiceManager.NaviMapManager.CheckIfLoading())
        {
            return false;
        }

        MapSize = new Vector2(NaviMapSize * ServiceManager.NaviMapManager.NaviScale, NaviMapSize * ServiceManager.NaviMapManager.NaviScale);
        _minimapRadius = MapSize.X * 0.315f;
        MapPos = new Vector2(ServiceManager.NaviMapManager.X, ServiceManager.NaviMapManager.Y);
        ServiceManager.WindowManager.NaviMapWindow.Size = MapSize;
        ServiceManager.WindowManager.NaviMapWindow.Position = MapPos;

        unsafe
        {
            var player = (Character*)ServiceManager.ObjectTable[0]?.Address;
            if (player == null)
            {
                return false;
            }

            PlayerPos = new Vector2(player->GameObject.Position.X, player->GameObject.Position.Z);
        }
        ChecksPassed = true;
        return true;
    }

    public static int? GetObjIndexById(ulong objId)
    {
        foreach (var x in Enumerable.Range(2, 200).Where(x => x % 2 == 0))
        {
            if (ServiceManager.ObjectTable[x] != null && ServiceManager.ObjectTable[x]?.GameObjectId == objId)
            {
                return x;
            }
        }

        return null;
    }
}
