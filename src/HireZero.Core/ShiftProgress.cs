namespace HireZero.Core;

public sealed record ShiftProgress(string State, string Summary, string? At, string? NextCheckAt, string Freshness);
public static class ShiftProgressView
{
    public static ShiftProgress For(AgentStore store, bool scanning, DateTimeOffset now)
    {
        var shift = store.Shift();
        var active = store.OpenTasks().FirstOrDefault(task => task.State == "active" && AgentActivity.IsRunning(task.Id));
        if (active is not null) return new("Running", active.Title, active.UpdatedAt, null, "live");
        if (!shift.On || !DateTimeOffset.TryParse(shift.EndsAt, out var end) || now >= end)
            return new("Paused", "Shift is off. Direct requests can still be answered.", shift.LastSenseAt, null, "recorded");
        if (scanning) return new("Scanning", "Checking the enabled sources within their existing scope.", shift.LastSenseAt, null, "live");
        if (!DateTimeOffset.TryParse(shift.LastSenseAt, out var sensed)) return new("Waiting", "The shift is on; the first bounded check has not completed.", null, null, "unknown");
        var next = sensed + AgentWorker.SenseInterval;
        return new("Observing", shift.SenseNote ?? "Last check completed.", shift.LastSenseAt,
            next < end ? next.ToString("O") : null, now - sensed > AgentWorker.SenseInterval + TimeSpan.FromMinutes(2) ? "stale" : "recorded");
    }
}
