namespace GameTool.Core;

public sealed record DiagnosticField(string Name, object Value);

/// <summary>
/// 适配器可选提供的格式诊断。字段名和顺序由适配器决定，应用层按名读取。
/// </summary>
public interface ISaveDiagnostics
{
    IReadOnlyList<DiagnosticField> Fields { get; }
}
