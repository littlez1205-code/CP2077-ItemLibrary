namespace GameTool.Core;

/// <summary>可选。声明哪些未持有的资源可以插入进存档。</summary>
public interface IInsertCapable
{
    bool CanInsert(ResourceId id);
}
