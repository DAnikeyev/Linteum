using AutoMapper;
using AutoMapper.QueryableExtensions;
using Linteum.Domain.Repository;
using Linteum.Shared;
using Linteum.Shared.DTO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Linteum.Infrastructure;

public class ColorRepository : IColorRepository
{
    private AppDbContext _context;
    private IMapper _mapper;
    private readonly ILogger<ColorRepository> _logger;
    private readonly IReadOnlyDictionary<string, int> _colorOrderByHex;

    public ColorRepository(AppDbContext context, IMapper mapper, Config config, ILogger<ColorRepository> logger)
    {
        _context = context;
        _mapper = mapper;
        _logger = logger;
        _colorOrderByHex = config.Colors
            .Select((color, index) => new { HexValue = NormalizeHex(color.HexValue), Index = index })
            .ToDictionary(color => color.HexValue, color => color.Index);
    }

    public async Task<IEnumerable<ColorDto>> GetAllAsync()
    {
        var colors = await _context.Colors
            .AsNoTracking()
            .ProjectTo<ColorDto>(_mapper.ConfigurationProvider)
            .ToListAsync();

        return colors
            .OrderBy(color => _colorOrderByHex.GetValueOrDefault(NormalizeHex(color.HexValue), int.MaxValue))
            .ThenBy(color => color.Name)
            .ThenBy(color => color.HexValue)
            .ToList();
    }

    public async Task<ColorDto?> GetDefautColor()
    {
        var defaultColor = await _context.Colors.AsNoTracking().FirstOrDefaultAsync(c => c.HexValue == "#FFFFFF");
        if (defaultColor == null)
        {
            _logger.LogDebug("Default color #FFFFFF not found in the database.");
            return null;
        }
        return _mapper.Map<ColorDto>(defaultColor);
    }

    private static string NormalizeHex(string hexValue) => hexValue.Trim().ToUpperInvariant();
}
