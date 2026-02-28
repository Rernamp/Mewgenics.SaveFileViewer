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
        Task<List<HouseCat>> GetHouseCatsAsync();
        void InvalidateCache();  // Добавляем сюда
        DateTime LastCacheUpdate { get; }  // Добавляем сюда
    }

    public class CatService : ICatService {
        private readonly IServiceProvider _serviceProvider;
        private readonly CatDbContext _context;
        private readonly ICatParser _catParser;
        private readonly ILogger<CatService> _logger;
        private readonly IFileChangeWatcher _fileWatcher;
        private readonly IMemoryCache _cache;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        private List<ParsedCat>? _cachedCats;
        private DateTime _lastCacheUpdate = DateTime.MinValue;

        public DateTime LastCacheUpdate => _lastCacheUpdate;

        public CatService(
        CatDbContext context,
        ICatParser catParser,
        ILogger<CatService> logger,
        IServiceProvider serviceProvider, // Вместо IFileChangeWatcher
        IMemoryCache cache) {
            _context = context;
            _catParser = catParser;
            _logger = logger;
            _serviceProvider = serviceProvider;
            _cache = cache;

            // Получаем watcher при первом использовании
            _fileWatcher = _serviceProvider.GetRequiredService<IFileChangeWatcher>();
            _fileWatcher.FileChanged += OnFileChanged;
        }


        private void OnFileChanged(object? sender, FileSystemEventArgs e) {
            _logger.LogInformation("File changed, invalidating cache...");
            InvalidateCache();
        }

        public void InvalidateCache() {
            _cachedCats = null;
            _cache.Remove("all_parsed_cats");
            _lastCacheUpdate = DateTime.MinValue;
        }

        public async Task<List<ParsedCat>> GetAllCatsAsync() {
            // Быстрая проверка
            if (_cachedCats != null)
                return _cachedCats;

            await _semaphore.WaitAsync();
            try {
                // Double-check
                if (_cachedCats != null)
                    return _cachedCats;

                const string cacheKey = "all_parsed_cats";

                if (_cache.TryGetValue(cacheKey, out List<ParsedCat>? cached) && cached != null) {
                    _cachedCats = cached;
                    _lastCacheUpdate = DateTime.Now;
                    return cached;
                }

                // Загрузка данных
                _logger.LogInformation("Loading cats from database...");
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                var cats = await LoadAllCatsAsync();

                stopwatch.Stop();
                _logger.LogInformation($"Loaded {cats.Count} cats in {stopwatch.ElapsedMilliseconds}ms");

                _cachedCats = cats;
                _lastCacheUpdate = DateTime.Now;
                _cache.Set(cacheKey, cats, TimeSpan.FromMinutes(5));

                return cats;
            } finally {
                _semaphore.Release();
            }
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

        public async Task<List<HouseCat>> GetHouseCatsAsync() {
            var allCats = await GetAllCatsAsync();
            var catsDict = allCats.ToDictionary(c => c.Key, c => c);

            var houseState = await _context.Files
                .Where(f => f.Key == "house_state")
                .Select(f => f.Data)
                .FirstOrDefaultAsync();

            if (houseState == null || houseState.Length == 0) {
                return new List<HouseCat>();
            }

            var houseCatEntries = ParseHouseState(houseState);
            var currentDay = await GetCurrentDayAsync(); // Добавьте этот метод
            var houseCats = new List<HouseCat>();

            foreach (var entry in houseCatEntries) {
                if (catsDict.TryGetValue(entry.Key, out var cat)) {
                    var age = currentDay.HasValue && cat.BirthdayDay.HasValue
                        ? currentDay.Value - cat.BirthdayDay.Value
                        : (int?)null;

                    houseCats.Add(new HouseCat {
                        Key = cat.Key,
                        Name = cat.Name,
                        Sex = cat.Sex,
                        Room = entry.Room,
                        Level = cat.Stats?.Level,
                        ClassName = cat.ClassName,
                        IsDead = cat.Flags?.Dead ?? false,
                        IsSick = cat.Flags?.IsSick ?? false,
                        IsRetired = cat.Flags?.Retired ?? false,
                        BirthdayDay = cat.BirthdayDay,
                        Age = age,
                        Stats = cat.Stats
                    });
                }
            }

            return houseCats.OrderBy(c => c.Name).ToList();
        }

        private async Task<int?> GetCurrentDayAsync() {
            var dayData = await _context.Files
                .Where(f => f.Key == "current_day")
                .Select(f => f.Data)
                .FirstOrDefaultAsync();

            if (dayData == null) return null;

            // Парсинг current_day из BLOB
            try {
                var dayStr = System.Text.Encoding.ASCII.GetString(dayData).TrimEnd('\0');
                if (int.TryParse(dayStr, out var day))
                    return day;
            } catch { }

            return null;
        }
        private List<HouseCatEntry> ParseHouseState(byte[] blob) {
            var result = new List<HouseCatEntry>();

            if (blob.Length < 8) return result;

            int ver = (int)BinaryHelpers.ReadU32LE(blob, 0);
            int cnt = (int)BinaryHelpers.ReadU32LE(blob, 4);

            if (ver != 0 || cnt > 512) return result;

            int off = 8;

            for (int i = 0; i < cnt; i++) {
                if (off + 16 > blob.Length) break;

                int key = (int)BinaryHelpers.ReadU32LE(blob, off);
                int unk = (int)BinaryHelpers.ReadU32LE(blob, off + 4);
                int roomLen = (int)BinaryHelpers.ReadU64LE(blob, off + 8);
                int nameOff = off + 16;

                if (nameOff + roomLen > blob.Length) break;

                string room = BinaryHelpers.ReadAscii(blob, nameOff, roomLen);

                int dOff = nameOff + roomLen;
                if (dOff + 24 > blob.Length) break;

                double p0 = BinaryHelpers.ReadF64LE(blob, dOff);
                double p1 = BinaryHelpers.ReadF64LE(blob, dOff + 8);
                double p2 = BinaryHelpers.ReadF64LE(blob, dOff + 16);

                result.Add(new HouseCatEntry {
                    Key = key,
                    Room = room,
                    UnkU32 = unk,
                    P0 = p0,
                    P1 = p1,
                    P2 = p2
                });

                off = dOff + 24;
            }

            return result;
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

        public void Dispose() {
            _fileWatcher.StopWatching();
            _semaphore.Dispose();
        }
    }
}