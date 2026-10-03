namespace Ymir.VibeMaker.Domain;

/// <summary>
/// AGENT_RUNTIME.status。依 SA §6.2 lifecycle 圖，補上表格（SA §8）遺漏的 <see cref="NotCreated"/> 與 <see cref="Deleted"/>。
/// </summary>
public enum RuntimeStatus
{
    NotCreated = 0,
    Created = 1,
    Running = 2,
    Busy = 3,
    Stopped = 4,
    Error = 5,
    Deleted = 6,
}
