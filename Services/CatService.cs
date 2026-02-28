using Microsoft.EntityFrameworkCore;
using Mewgenics.SaveFileViewer.Data;
using Mewgenics.SaveFileViewer.Models;
using Mewgenics.SaveFileViewer.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Mewgenics.SaveFileViewer.Services {
    public interface ICatService {
        Task<List<ParsedCat>> GetAllCatsAsync();
        Task<ParsedCat?> GetCatByKeyAsync(int key);
        Task<int> GetCatsCountAsync();
    }

    public class CatService : ICatService {
        private readonly CatDbContext _context;
        private readonly ICatParser _catParser;
        private readonly ILogger<CatService> _logger;
        private readonly IMemoryCache _cache;

        
        private static List<ParsedCat>? _cachedCats;
        private static readonly object _cacheLock = new object();

        public CatService(CatDbContext context, ICatParser catParser,
         ILogger<CatService> logger, IMemoryCache cache) {
            _context = context;
            _catParser = catParser;
            _logger = logger;
            _cache = cache;
        }

        public async Task<List<ParsedCat>> GetAllCatsAsync() {
            
            if (_cachedCats != null)
                return _cachedCats;

            
            const string cacheKey = "all_parsed_cats";
            if (_cache.TryGetValue(cacheKey, out List<ParsedCat>? cached) && cached != null) {
                _cachedCats = cached; 
                return cached;
            }

            
            var cats = await LoadAllCatsAsync();

            
            _cachedCats = cats;
            _cache.Set(cacheKey, cats, TimeSpan.FromMinutes(30));

            return cats;
        }
        public async Task<ParsedCat?> GetCatByKeyAsync(int key) {
            var entity = await _context.Cats.FindAsync(key);
            if (entity == null)
                return null;

            try {
                return await _catParser.ParseCatAsync(entity.Key, entity.Data);
            } catch (Exception ex) {
                _logger.LogError(ex, $"Failed to parse cat {key}");
                return null;
            }
        }

        public async Task<int> GetCatsCountAsync() {
            return await _context.Cats.CountAsync();
        }

        private async Task<List<ParsedCat>> LoadAllCatsAsync() {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var catEntities = await _context.Cats.ToListAsync();
            var cats = new List<ParsedCat>();

            
            var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

            await Task.Run(() =>
            {
                Parallel.ForEach(catEntities, options, entity =>
                {
                    try {
                        var parsed = _catParser.ParseCatAsync(entity.Key, entity.Data).GetAwaiter().GetResult();
                        lock (cats) {
                            cats.Add(parsed);
                        }
                    } catch (Exception ex) {
                        _logger.LogError(ex, $"Failed to parse cat {entity.Key}");
                    }
                });
            });

            var result = cats.OrderBy(c => c.Name).ToList();

            stopwatch.Stop();
            _logger.LogInformation($"Loaded {result.Count} cats in {stopwatch.ElapsedMilliseconds}ms");

            return result;
        }

        
        public void InvalidateCache() {
            _cachedCats = null;
            _cache.Remove("all_parsed_cats");
        }
    }
}