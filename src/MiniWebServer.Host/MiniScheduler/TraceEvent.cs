namespace MiniWebServer.Host.MiniScheduler;

/// <summary>
/// One event in a scheduler trace. Each Tick() of the scheduler
/// produces at most one event, describing what happened on that
/// tick (job dispatched, preempted, demoted, finished, idle).
/// </summary>
public sealed record TraceEvent(
    int Tick,
    string Action,    // "dispatch", "preempt", "demote", "finish", "boost", "idle"
    int? JobId,
    string? JobName,
    int? Queue,
    int? BurstRemaining,
    string? Detail
)
{
    public string Format()
    {
        string head = $"t={Tick,4}  {Action,-10}";
        if (JobId is null) return $"{head}  {Detail ?? ""}";
        return $"{head}  J{JobId}({JobName}) q={Queue} rem={BurstRemaining}  {Detail ?? ""}";
    }
}
