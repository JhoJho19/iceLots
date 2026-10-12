using System;

namespace IceP.Application
{
    public interface IClock
    {
        DateTimeOffset UtcNow { get; }
    }
}
