using AutoMapper;
using Linteum.Domain;
using Linteum.Domain.Repository;
using Linteum.Shared.DTO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Linteum.Infrastructure;

//ToDo: Add tests.
public class LoginEventRepository: ILoginEventRepository
{
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;
    private readonly ILogger<LoginEventRepository> _logger;

    public LoginEventRepository(AppDbContext context, IMapper mapper, ILogger<LoginEventRepository> logger)
    {
        _context = context;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<IEnumerable<LoginEventDto>> GetByUserIdAsync(Guid userId)
    {
        var events = await _context.LoginEvents
            .Where(e => e.UserId == userId)
            .Select(e => _mapper.Map<LoginEventDto>(e))
            .ToListAsync();
        return events;
    }

    public async Task<bool> AddLoginEvent(LoginEventDto loginEventDto)
    {
        var loginEvent = _mapper.Map<LoginEvent>(loginEventDto);
        loginEvent.LoggedInAt = DateTime.UtcNow;
        await _context.LoginEvents.AddAsync(loginEvent);
        await _context.SaveChangesAsync();
        return true;
    }
}