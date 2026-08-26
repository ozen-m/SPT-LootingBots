using System.Reflection;
using System.Text.Json;
using LootingBotsServerMod.Models;
using Microsoft.Extensions.DependencyInjection;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils.Json.Converters;

namespace LootingBotsServerMod;

public class OnDIConstruct : IOnDIConstruct
{
    private static readonly JsonSerializerOptions _options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Converters = { new StringToMongoIdConverter() },
    };

    public static async Task OnDIConstructAsync(IServiceCollection serviceCollection, CancellationToken cancellationToken)
    {
        var modPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var configPath = Path.Combine(modPath, "Config", "config.json");

        var config = await LoadAsync(configPath, cancellationToken);
        await SaveAsync(config, configPath, cancellationToken);

        serviceCollection.AddSingleton(config);
    }

    private static async Task<ConfigModel> LoadAsync(string filePath, CancellationToken token = default)
    {
        await using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        return await JsonSerializer.DeserializeAsync<ConfigModel>(fs, _options, token) ?? new ConfigModel();
    }

    private static async Task SaveAsync(ConfigModel config, string configPath, CancellationToken token = default)
    {
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config, _options), token);
    }
}
