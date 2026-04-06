using AutoMapper;
using AutoMapper.QueryableExtensions;
using Linteum.Domain.Repository;
using Linteum.Shared.DTO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Linteum.Infrastructure;

public class ColorRepository : IColorRepository
{
    private AppDbContext _context;
    private IMapper _mapper;
    private readonly ILogger<ColorRepository> _logger;

    public ColorRepository(AppDbContext context, IMapper mapper, ILogger<ColorRepository> logger)
    {
        _context = context;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<IEnumerable<ColorDto>> GetAllAsync()
    {
        return await _context.Colors.ProjectTo<ColorDto>(_mapper.ConfigurationProvider).ToListAsync();
    }

    public async Task<ColorDto?> GetDefautColor()
    {
        var defaultColor = await _context.Colors.AsNoTracking().FirstOrDefaultAsync(c => c.HexValue == "#FFFFFF");
        if (defaultColor == null)
        {
            _logger.LogWarning("Default color #FFFFFF not found in the database.");
            return null;
        }
        return _mapper.Map<ColorDto>(defaultColor);
    }
}