using Linteum.Shared.DTO;

namespace Linteum.Domain.Repository;

public interface ILoginEventRepository
{
    public Task<IEnumerable<LoginEventDto>> GetByUserIdAsync(Guid userId);
    public Task<bool> AddLoginEvent(LoginEventDto loginEvent);
}

