namespace GameTool.Core;

/// <summary>
/// 本进程写存档的次数。每次替换存档文件后加一；只读缓存把它放进缓存键，
/// 自己写过存档就不会再用旧的解析结果（文件时间的精度不够区分两次很快的写入）。
/// </summary>
public static class SaveFileWrites
{
    private static long _generation;

    public static long Generation => Interlocked.Read(ref _generation);

    public static void Record() => Interlocked.Increment(ref _generation);
}
