using System;
using IceP.Application;

namespace IceP.Tests
{
    public sealed class FakeClock : IClock
    {
        public DateTimeOffset Now { get; set; }
        public int ReadCount { get; private set; }
        public Action BeforeRead { get; set; }
        public FakeClock(DateTimeOffset now) { Now = now; }
        public DateTimeOffset UtcNow { get { ReadCount++; BeforeRead?.Invoke(); return Now; } }
        public void Advance(TimeSpan delta) { Now = Now.Add(delta); }
    }
}
