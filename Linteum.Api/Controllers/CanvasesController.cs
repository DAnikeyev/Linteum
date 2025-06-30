using Linteum.Infrastructure;
using Linteum.Shared.DTO;
using Microsoft.AspNetCore.Mvc;

namespace Linteum.Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class CanvasesController : ControllerBase
    {
        private readonly RepositoryManager _repoManager;

        public CanvasesController(RepositoryManager repoManager)
        {
            _repoManager = repoManager;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] bool includePrivate = false)
        {
            var canvases = await _repoManager.CanvasRepository.GetAllAsync(includePrivate);
            return Ok(canvases);
        }

        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetByUserId(Guid userId)
        {
            var canvases = await _repoManager.CanvasRepository.GetByUserIdAsync(userId);
            return Ok(canvases);
        }

        [HttpGet("name/{name}")]
        public async Task<IActionResult> GetByName(string name)
        {
            var canvas = await _repoManager.CanvasRepository.GetByNameAsync(name);
            if (canvas == null)
                return NotFound();
            return Ok(canvas);
        }

        [HttpPost]
        public async Task<IActionResult> AddCanvas([FromBody] CanvasDto canvas, [FromQuery] string? passwordHash)
        {
            var result = await _repoManager.CanvasRepository.TryAddCanvas(canvas, passwordHash);
            if (result == null)
                return BadRequest("Canvas could not be created.");
            return Ok(result);
        }

        [HttpDelete("name/{name}")]
        public async Task<IActionResult> DeleteCanvas(string name, [FromQuery] string passwordHash)
        {
            var result = await _repoManager.CanvasRepository.TryDeleteCanvasByName(name, passwordHash);
            if (!result)
                return BadRequest("Canvas could not be deleted.");
            return Ok();
        }

        [HttpPost("check-password")]
        public async Task<IActionResult> CheckPassword([FromBody] CanvasDto canvas, [FromQuery] string? passwordHash)
        {
            var result = await _repoManager.CanvasRepository.CheckPassword(canvas, passwordHash);
            return Ok(result);
        }
    }
}

