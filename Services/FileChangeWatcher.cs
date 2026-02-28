using System.IO;
using Mewgenics.SaveFileViewer.Services;

namespace Mewgenics.SaveFileViewer.Services {
    public interface IFileChangeWatcher {
        event EventHandler<FileSystemEventArgs>? FileChanged;
        void StartWatching();
        void StopWatching();
        DateTime GetLastWriteTime();
    }

    public class FileChangeWatcher : IFileChangeWatcher, IDisposable {
        private readonly string _filePath;
        private readonly ILogger<FileChangeWatcher> _logger;
        private FileSystemWatcher? _watcher;
        private DateTime _lastFileTime;
        private readonly object _lock = new object();

        public event EventHandler<FileSystemEventArgs>? FileChanged;

        public FileChangeWatcher(IConfiguration config, ILogger<FileChangeWatcher> logger) {
            _filePath = config["DbPath"] ?? throw new InvalidOperationException("DbPath not configured");
            _logger = logger;
            _lastFileTime = File.Exists(_filePath) ? File.GetLastWriteTimeUtc(_filePath) : DateTime.MinValue;
        }

        public void StartWatching() {
            if (_watcher != null) return;

            var directory = Path.GetDirectoryName(_filePath) ?? ".";
            var fileName = Path.GetFileName(_filePath);

            _logger.LogInformation($"Starting file watcher for: {_filePath}");

            _watcher = new FileSystemWatcher(directory, fileName) {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
        }

        public void StopWatching() {
            if (_watcher != null) {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _watcher = null;
                _logger.LogInformation("File watcher stopped");
            }
        }

        public DateTime GetLastWriteTime() {
            lock (_lock) {
                return _lastFileTime;
            }
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e) {
            // Ждём немного, чтобы файл освободился
            Thread.Sleep(100);

            try {
                if (!File.Exists(_filePath)) return;

                var newTime = File.GetLastWriteTimeUtc(_filePath);

                lock (_lock) {
                    // Проверяем, действительно ли время изменилось
                    if (newTime <= _lastFileTime) return;

                    _logger.LogInformation($"File changed: {e.FullPath} (new time: {newTime:HH:mm:ss.fff})");
                    _lastFileTime = newTime;
                }

                // Уведомляем подписчиков
                FileChanged?.Invoke(this, e);
            } catch (Exception ex) {
                _logger.LogError(ex, "Error handling file change");
            }
        }

        public void Dispose() {
            StopWatching();
            GC.SuppressFinalize(this);
        }
    }
}