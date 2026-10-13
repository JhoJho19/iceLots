using System;
using System.Globalization;
using IceP.Application;

namespace IceP.Presentation
{
    public sealed class JackpotDisplay
    {
        public string Amount { get; }
        public string Status { get; }
        public string Reset { get; }
        public string Availability { get; }
        public JackpotState State { get; }
        public JackpotDisplay(JackpotStatus status, DateTimeOffset now)
        {
            Amount = status.JackpotAmount.ToString("N0", CultureInfo.InvariantCulture) + " COINS";
            State = status.State;
            Status = State == JackpotState.WonThisWeek ? "WON THIS WEEK" : State == JackpotState.Active ? "ACTIVE" : "INACTIVE";
            Reset = FormatReset(status.NextResetUtc, now);
            Availability = !status.Enabled ? "Event disabled" : status.StartUtc.HasValue && now < status.StartUtc.Value
                ? "Available " + status.StartUtc.Value.UtcDateTime.ToString("dd MMM yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture)
                : !status.IsEligible ? State == JackpotState.WonThisWeek ? "Already won this week" : "Participation unavailable" : "";
        }
        public static string FormatReset(DateTimeOffset? reset, DateTimeOffset now)
        {
            if (!reset.HasValue) return "Next reset unavailable";
            var remaining = reset.Value - now;
            if (remaining <= TimeSpan.Zero) return "Reset due";
            return string.Format(CultureInfo.InvariantCulture, "Next reset in {0:00}d {1:00}h {2:00}m {3:00}s",
                remaining.Days, remaining.Hours, remaining.Minutes, remaining.Seconds);
        }
    }
}
