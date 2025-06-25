using Linteum.Shared.DTO;

namespace Linteum.Domain.Repository;

public interface IColorRepository
{
    public Task<IEnumerable<ColorDto>> GetAllAsync();
}

