using Linteum.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Linteum.Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ColorsController : ControllerBase
    {
        private readonly RepositoryManager _repoManager;
        private readonly ILogger<ColorsController> _logger;

        public ColorsController(RepositoryManager repoManager, ILogger<ColorsController> logger)
        {
            _repoManager = repoManager;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetColors()
        {
            var colors = (await _repoManager.ColorRepository.GetAllAsync()).ToList();
            _logger.LogInformation("Colors returned successfully. Count={Count}", colors.Count);
            return Ok(colors);
        }
    }
}

