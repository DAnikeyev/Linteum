using Linteum.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Linteum.Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ColorsController : ControllerBase
    {
        private readonly RepositoryManager _repoManager;

        public ColorsController(RepositoryManager repoManager)
        {
            _repoManager = repoManager;
        }

        [HttpGet]
        public async Task<IActionResult> GetColors()
        {
            var colors = await _repoManager.ColorRepository.GetAllAsync();
            return Ok(colors);
        }
    }
}

