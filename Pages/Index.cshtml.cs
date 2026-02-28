using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Mewgenics.SaveFileViewer.Services;
using Mewgenics.SaveFileViewer.Models;

namespace Mewgenics.SaveFileViewer.Pages {
    public class IndexModel : PageModel {
        private readonly ICatService _catService;
        private readonly ILogger<IndexModel> _logger;

        public List<HouseCat> HouseCats { get; set; } = new();
        public DateTime LastUpdate { get; set; }

        public IndexModel(ICatService catService, ILogger<IndexModel> logger) {
            _catService = catService;
            _logger = logger;
        }

        public async Task<IActionResult> OnGetAsync() {
            try {
                HouseCats = await _catService.GetHouseCatsAsync();
                LastUpdate = DateTime.Now;
                return Page();
            } catch (Exception ex) {
                _logger.LogError(ex, "Error loading house cats");
                return RedirectToPage("Error");
            }
        }

        public async Task<IActionResult> OnGetRefreshAsync() {
            try {

                var houseCats = await _catService.GetHouseCatsAsync();
                return Partial("_CatsTable", houseCats);
            } catch (Exception ex) {
                _logger.LogError(ex, "Error refreshing cats");
                return StatusCode(500);
            }
        }
    }
}