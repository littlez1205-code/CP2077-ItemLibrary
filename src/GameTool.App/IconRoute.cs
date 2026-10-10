using GameTool.Core;

namespace GameTool.App;

public sealed record IconRequest(string GameId, string RecordName, IconGender Gender);

/// <summary>
/// 桌面端图标地址：<c>https://gametool.local/icon/&lt;gameId&gt;/&lt;record&gt;.png?g=f|m&amp;v=&lt;版本&gt;</c>。
/// 记录名只允许字母、数字、下划线和点（不能以点开头、不能有连续的点），游戏 id 只允许字母、数字、下划线和连字符；
/// 不做百分号解码，带 % 的一律拒绝。记录名不会用来拼文件路径，归档按哈希查找。
/// </summary>
public static class IconRoute
{
    public const string Prefix = "icon/";
    public const int MaxRecordLength = 200;

    /// <summary>path 是地址的路径部分（可带开头的 /），query 是 ? 后面的部分（可带 ?）。</summary>
    public static bool TryParse(string? path, string? query, out IconRequest request)
    {
        request = null!;
        if (string.IsNullOrEmpty(path))
            return false;
        var trimmed = path.TrimStart('/');
        if (!trimmed.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        var parts = trimmed[Prefix.Length..].Split('/');
        if (parts.Length != 2 || !parts[1].EndsWith(".png", StringComparison.Ordinal))
            return false;
        var gameId = parts[0];
        var record = parts[1][..^".png".Length];
        if (!IsValidGameId(gameId) || !IsValidRecordName(record))
            return false;
        request = new IconRequest(gameId, record, ReadGender(query));
        return true;
    }

    public static bool IsValidRecordName(string? record)
    {
        if (string.IsNullOrEmpty(record) || record.Length > MaxRecordLength || record[0] == '.' || record[^1] == '.')
            return false;
        if (record.Contains("..", StringComparison.Ordinal))
            return false;
        return record.All(static ch => ch is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '.');
    }

    public static bool IsValidGameId(string? gameId) =>
        !string.IsNullOrEmpty(gameId) && gameId.Length <= 64
        && gameId.All(static ch => ch is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-');

    private static IconGender ReadGender(string? query)
    {
        if (string.IsNullOrEmpty(query))
            return IconGender.Female;
        foreach (var pair in query.TrimStart('?').Split('&'))
        {
            if (pair == "g=m")
                return IconGender.Male;
        }

        return IconGender.Female;
    }
}
