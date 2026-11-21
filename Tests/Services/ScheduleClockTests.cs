using AppServices.Scheduling;
using AppServices.Sonos;

namespace Services;

public sealed class ScheduleClockTests
{
    private static Schedule Example() => new() { Id = 1, Name = "Morning", GroupId = 1, DaysOfWeek = 127, LocalTime = "08:00", TimeZoneId = "America/New_York", LibraryObjectId = "S:folder", Speakers = [new() { SpeakerId = 1 }], UpdatedAt = DateTimeOffset.MinValue };
    private static DateTimeOffset Utc(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

    [Fact]
    public void GraceWeekdaysExceptionsAndPause()
    {
        var s = Example();
        var now = Utc("2026-11-23T13:03:00Z");
        ScheduleClock.GetDueOccurrences(s, now, TimeSpan.FromMinutes(5)).ShouldBe([Utc("2026-11-23T13:00:00Z")]);
        ScheduleClock.GetDueOccurrences(s, now.AddMinutes(3), TimeSpan.FromMinutes(5)).ShouldBeEmpty();
        s.DaysOfWeek = 1;
        ScheduleClock.GetDueOccurrences(s, now, TimeSpan.FromMinutes(5)).ShouldBeEmpty();
        s.DaysOfWeek = 127;
        s.Exceptions.Add(new() { Date = "2026-11-23" });
        ScheduleClock.GetDueOccurrences(s, now, TimeSpan.FromMinutes(5)).ShouldBeEmpty();
        s.Exceptions.Clear();
        s.DisabledUntil = now.AddMinutes(-1);
        ScheduleClock.GetDueOccurrences(s, now, TimeSpan.FromMinutes(5)).ShouldBeEmpty();
    }

    [Fact]
    public void DstSkipsMissingTimeAndUsesEarlierAmbiguousOccurrence()
    {
        var s = Example();
        s.LocalTime = "02:30";
        ScheduleClock.GetDueOccurrences(s, Utc("2026-03-08T07:31:00Z"), TimeSpan.FromHours(2)).ShouldBeEmpty();
        s.LocalTime = "01:30";
        ScheduleClock.GetDueOccurrences(s, Utc("2026-11-01T06:31:00Z"), TimeSpan.FromHours(2)).ShouldBe([Utc("2026-11-01T05:30:00Z")]);
        ScheduleClock.GetDueOccurrences(s, Utc("2026-11-01T06:31:00Z"), TimeSpan.FromMinutes(5)).ShouldBeEmpty();
    }

    [Fact]
    public void MidnightLooksAtPreviousLocalDate()
    {
        var s = Example();
        s.LocalTime = "23:59";
        s.DaysOfWeek = 1;
        ScheduleClock.GetDueOccurrences(s, Utc("2026-11-23T05:02:00Z"), TimeSpan.FromMinutes(5)).ShouldBe([Utc("2026-11-23T04:59:00Z")]);
    }

    [Fact]
    public void EditsNeverReplayEarlierOccurrences()
    {
        var s = Example();
        s.UpdatedAt = Utc("2026-11-23T13:01:00Z");
        ScheduleClock.GetDueOccurrences(s, Utc("2026-11-23T13:02:00Z"), TimeSpan.FromMinutes(5)).ShouldBeEmpty();
    }

    [Fact]
    public void DayOfYearSpansFullFolderIncludingLeapYears()
    {
        SonosService.DayOfYearIndex(Utc("2024-01-01T12:00:00Z"), 100).ShouldBe(0);
        SonosService.DayOfYearIndex(Utc("2024-12-31T12:00:00Z"), 100).ShouldBe(99);
        SonosService.DayOfYearIndex(Utc("2025-12-31T12:00:00Z"), 100).ShouldBe(99);
        SonosService.DayOfYearIndex(Utc("2026-06-01T12:00:00Z"), 1).ShouldBe(0);
    }
}
