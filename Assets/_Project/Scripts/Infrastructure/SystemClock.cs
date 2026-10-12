using System;
using IceP.Application;

namespace IceP.Infrastructure
{
    public sealed class SystemClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
