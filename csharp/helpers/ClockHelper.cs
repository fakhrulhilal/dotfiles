namespace Dotfiles.Helpers;

public static class ClockHelper {
    extension(DateOnly date) {
        public DateOnly StartOfMonth() => new DateOnly(date.Year, date.Month, 1);
        public DateOnly EndOfMonth() => date.AddMonths(1).AddDays(-1);
        public DateOnly Yesterday() => date.AddDays(-1);

        public DateTime StartOfDay(DateTimeKind kind = DateTimeKind.Local) =>
            date.ToDateTime(TimeOnly.MinValue, kind).ToUniversalTime();
        public DateTime EndOfDay(DateTimeKind kind = DateTimeKind.Local) =>
            date.ToDateTime(TimeOnly.MaxValue, kind).ToUniversalTime();
    }
}
