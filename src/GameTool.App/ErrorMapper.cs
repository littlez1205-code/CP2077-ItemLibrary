using GameTool.Core;

namespace GameTool.App;

public sealed record MappedError(int ExitCode, string Code, string Message);

public static class ErrorMapper
{
    public static MappedError Map(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            UnsupportedSaveException => new MappedError(3, "unsupported_save", exception.Message),
            PatchRejectedException or AmbiguousResourceException or SkinRejectedException or ResearchRejectedException or InvalidDataException or IOException or UnauthorizedAccessException
                => new MappedError(1, "failed", exception.Message),
            _ => new MappedError(1, "internal", exception.Message)
        };
    }
}
