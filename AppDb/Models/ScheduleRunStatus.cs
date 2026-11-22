namespace AppDb.Models;

public enum ScheduleRunStatus
{
    Running = 0,
    Completed = 1,
    Skipped = 2,
    Interrupted = 3,
    Failed = 4,
}
