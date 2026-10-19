using System.Numerics;

namespace MiniMappingway.Model;

// 每個標記每幀一筆，所以是值型別：直接存在佇列的陣列裡，不另外配置。
public readonly struct CircleData
{
    public readonly Vector2 Position;
    public readonly string SourceName;

    public CircleData(Vector2 position, string sourceName)
    {
        Position = position;
        SourceName = sourceName;
    }

}
