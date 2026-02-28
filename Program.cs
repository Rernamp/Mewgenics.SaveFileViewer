using Microsoft.EntityFrameworkCore;
using Mewgenics.SaveFileViewer.Data;
using Mewgenics.SaveFileViewer.Services;

var builder = WebApplication.CreateBuilder(args);

// Get database path from command line arguments
string? dbPath = null;
for (int i = 0; i < args.Length; i++) {
    if (args[i] == "--dbpath" && i + 1 < args.Length) {
        dbPath = args[i + 1];
        break;
    }
}

if (string.IsNullOrEmpty(dbPath)) {
    Console.WriteLine("Error: Database path not specified. Use --dbpath <path_to_sav_file>");
    return 1;
}

Console.WriteLine($"Using database: {dbPath}");
builder.Configuration["DbPath"] = dbPath;

// Configure DbContext with SQLite
builder.Services.AddDbContext<CatDbContext>(options => {
    var connectionString = $"Data Source={dbPath};Mode=ReadOnly";
    options.UseSqlite(connectionString);
    options.EnableSensitiveDataLogging(false);
    options.EnableDetailedErrors(false);
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add memory cache
builder.Services.AddMemoryCache();

// Register services - ВАЖНО: все Scoped
builder.Services.AddSingleton<IFileChangeWatcher, FileChangeWatcher>(); // Singleton
builder.Services.AddScoped<ILZ4Decompressor, LZ4Decompressor>();
builder.Services.AddScoped<ICatParser, CatParser>();
builder.Services.AddScoped<ICatService, CatService>();

builder.Services.AddCors(options => {
    options.AddPolicy("AllowFrontend", policy => {
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment()) {
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");
app.UseAuthorization();
app.MapControllers();

// Предзагрузка данных при старте
try {
    using var scope = app.Services.CreateScope();
    var catService = scope.ServiceProvider.GetRequiredService<ICatService>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    logger.LogInformation("Preloading cats at startup...");
    var cats = await catService.GetAllCatsAsync();
    logger.LogInformation($"Preloaded {cats.Count} cats");
} catch (Exception ex) {
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogError(ex, "Failed to preload cats");
}

await app.RunAsync();

return 0;