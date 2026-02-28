using Mewgenics.SaveFileViewer.Services;
using Mewgenics.SaveFileViewer.Data;
using Mewgenics.SaveFileViewer.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mewgenics.SaveFileViewer.Services {
    public class DataInitializer : IHostedService {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DataInitializer> _logger;

        public DataInitializer(IServiceProvider serviceProvider, ILogger<DataInitializer> logger) {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("Starting data preload...");
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try {
                using var scope = _serviceProvider.CreateScope();
                var catService = scope.ServiceProvider.GetRequiredService<ICatService>();

                
                var cats = await catService.GetAllCatsAsync();

                stopwatch.Stop();
                _logger.LogInformation($"Preloaded {cats.Count} cats in {stopwatch.ElapsedMilliseconds}ms");

                
                var lz4 = scope.ServiceProvider.GetRequiredService<ILZ4Decompressor>();
                if (lz4 is LZ4Decompressor lz4Impl) {
                    await lz4Impl.WarmupAsync();
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to preload data");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("Stopping data initializer");
            return Task.CompletedTask;
        }
    }
}