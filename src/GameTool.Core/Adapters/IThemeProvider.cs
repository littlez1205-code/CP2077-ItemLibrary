namespace GameTool.Core;

/// <summary>适配器内嵌的主题描述。没有或打不开时由应用层回落默认主题。</summary>
public interface IThemeProvider
{
    Stream? OpenTheme();
}
