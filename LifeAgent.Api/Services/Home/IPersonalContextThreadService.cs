using LifeAgent.Api.Models;
using LifeAgent.Api.Services.PersonalContext;

namespace LifeAgent.Api.Services.Home;

public interface IPersonalContextThreadService
{
    IReadOnlyList<HomeOverviewContextThreadDto> BuildThreads(
        PersonalContextSnapshot context,
        string? timeZoneId = null);
}
