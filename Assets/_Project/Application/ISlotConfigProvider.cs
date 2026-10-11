using IceP.Domain.Configuration;

namespace IceP.Application
{
    public interface ISlotConfigProvider
    {
        // Return an independent validated snapshot, or throw ConfigValidationException.
        SlotRuntimeConfig Load();
    }
}
