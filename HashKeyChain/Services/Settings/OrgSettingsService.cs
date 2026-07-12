using System.Text.Json;
using HashKeyChain.Configuration;

namespace HashKeyChain.Services.Settings;

/// <summary>
/// Reads and persists <see cref="OrgSettings"/> (the operating buyer company and its
/// wallet). Storage is a single JSON file under the content root's App_Data folder —
/// deliberately NOT the database, so identifying "self" never risks the existing
/// trade schema or data. Thread-safe; the in-memory snapshot is the source of truth
/// after load.
/// </summary>
public interface IOrgSettingsService
{
    OrgSettings Current { get; }
    Task SaveAsync(OrgSettings settings, CancellationToken ct = default);
    event Action? Changed;
}

public sealed class OrgSettingsService : IOrgSettingsService
{
    private readonly string _filePath;
    private readonly ILogger<OrgSettingsService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private OrgSettings _current = new();

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public OrgSettingsService(IHostEnvironment env, ILogger<OrgSettingsService> logger)
    {
        _logger = logger;
        var dir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "settings.local.json");
        Load();
    }

    public OrgSettings Current => _current;

    public event Action? Changed;

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<OrgSettings>(json, JsonOptions);
                if (loaded is not null)
                    _current = loaded;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load org settings from {Path}; using defaults.", _filePath);
        }
    }

    public async Task SaveAsync(OrgSettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _gate.WaitAsync(ct);
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            await File.WriteAllTextAsync(_filePath, json, ct);
            _current = settings;
        }
        finally
        {
            _gate.Release();
        }
        Changed?.Invoke();
    }
}
