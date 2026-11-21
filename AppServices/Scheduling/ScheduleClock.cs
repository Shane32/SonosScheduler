using AppServices.Sonos;

namespace AppServices.Scheduling;

public static class ScheduleClock
{
    public static IReadOnlyList<DateTimeOffset> GetDueOccurrences(Schedule schedule, DateTimeOffset now, TimeSpan grace)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(grace, TimeSpan.Zero);
        if (!schedule.Enabled)
            return [];
        var zone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId);
        var time = TimeOnly.ParseExact(schedule.LocalTime, "HH:mm", CultureInfo.InvariantCulture);
        var start = now - grace;
        var firstDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, zone).DateTime);
        var lastDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var result = new List<DateTimeOffset>();
        for (var date = firstDate; date <= lastDate; date = date.AddDays(1)) {
            if ((schedule.DaysOfWeek & (1 << (int)date.DayOfWeek)) == 0 || schedule.Exceptions.Any(x => x.Date == date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
                continue;
            var local = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(local))
                continue;
            var utc = zone.IsAmbiguousTime(local)
                ? new DateTimeOffset(local, zone.GetAmbiguousTimeOffsets(local).Max()).ToUniversalTime()
                : new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
            if (utc >= start && utc <= now && utc >= schedule.UpdatedAt && (schedule.DisabledUntil is null || utc >= schedule.DisabledUntil))
                result.Add(utc);
        }
        return result;
    }

    public static void Validate(Schedule schedule)
    {
        if (string.IsNullOrWhiteSpace(schedule.Name) || schedule.Name.Length > 100)
            throw new ClientError("Enter a schedule name of at most 100 characters.");
        if (schedule.DaysOfWeek is < 1 or > 127)
            throw new ClientError("Select at least one weekday.");
        if (!TimeOnly.TryParseExact(schedule.LocalTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new ClientError("Time must use HH:mm.");
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(schedule.TimeZoneId, out _))
            throw new ClientError("Select a valid time zone.");
        if (!SonosService.SelectionModes.Contains(schedule.SelectionMode))
            throw new ClientError("Select a valid music selection mode.");
        if (schedule.LibraryObjectId.Length > 2000 || !(schedule.LibraryObjectId.StartsWith("A:", StringComparison.Ordinal) || schedule.LibraryObjectId.StartsWith("S:", StringComparison.Ordinal)))
            throw new ClientError("Select music from the NAS library.");
        if (schedule.LibraryTitle.Length > 500)
            throw new ClientError("Library title is too long.");
        if (!schedule.Speakers.Any(x => x.Enabled))
            throw new ClientError("Enable at least one speaker.");
        if (schedule.Speakers.Select(x => x.SpeakerId).Distinct().Count() != schedule.Speakers.Count)
            throw new ClientError("Select each speaker only once.");
        if (schedule.Speakers.Any(x => x.Volume is < 0 or > 100))
            throw new ClientError("Volume must be between 0 and 100, or current.");
        if (schedule.Exceptions.Any(x => !DateOnly.TryParseExact(x.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) || x.Reason?.Length > 500))
            throw new ClientError("Exceptions require a valid yyyy-MM-dd date and a reason up to 500 characters.");
        if (schedule.Exceptions.Select(x => x.Date).Distinct().Count() != schedule.Exceptions.Count)
            throw new ClientError("Exception dates must be unique.");
    }
}
