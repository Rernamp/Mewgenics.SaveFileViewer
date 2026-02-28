using Microsoft.AspNetCore.Mvc;
using Mewgenics.SaveFileViewer.Models;
using Mewgenics.SaveFileViewer.Services;

namespace Mewgenics.SaveFileViewer.Controllers {
    [ApiController]
    [Route("api/[controller]")]
    public class CatsController : ControllerBase {
        private readonly ICatService _catService;
        private readonly ILogger<CatsController> _logger;

        public CatsController(ICatService catService, ILogger<CatsController> logger) {
            _catService = catService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<List<ParsedCat>>> GetCats() {
            try {
                var cats = await _catService.GetAllCatsAsync();
                return Ok(cats);
            } catch (Exception ex) {
                _logger.LogError(ex, "Error getting cats");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("{key}")]
        public async Task<ActionResult<ParsedCat>> GetCat(int key) {
            try {
                var cat = await _catService.GetCatByKeyAsync(key);
                if (cat == null) {
                    return NotFound($"Cat with key {key} not found");
                }
                return Ok(cat);
            } catch (Exception ex) {
                _logger.LogError(ex, "Error getting cat {Key}", key);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("count")]
        public async Task<ActionResult<int>> GetCatsCount() {
            var count = await _catService.GetCatsCountAsync();
            return Ok(count);
        }
    }
}