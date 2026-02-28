using Microsoft.AspNetCore.Mvc;
using Mewgenics.SaveFileViewer.Services;

namespace Mewgenics.SaveFileViewer.Controllers {
    public class HomeController : Controller {
        private readonly ICatService _catService;
        private readonly ILogger<HomeController> _logger;

        public HomeController(ICatService catService, ILogger<HomeController> logger) {
            _catService = catService;
            _logger = logger;
        }

        public async Task<IActionResult> Index() {
            try {
                var houseCats = await _catService.GetHouseCatsAsync();
                ViewData["LastUpdate"] = DateTime.Now;
                return View(houseCats);
            } catch (Exception ex) {
                _logger.LogError(ex, "Error loading house cats");
                return View("Error", new { message = "Failed to load cats" });
            }
        }

        public async Task<IActionResult> Refresh() {
            try {
                var houseCats = await _catService.GetHouseCatsAsync();
                return PartialView("_CatsTable", houseCats);
            } catch (Exception ex) {
                _logger.LogError(ex, "Error refreshing cats");
                return StatusCode(500);
            }
        }
    }
}