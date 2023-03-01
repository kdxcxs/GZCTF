using System.ComponentModel;
using CTFServer.Controllers;
using CTFServer.Models.Data;
using CTFServer.Services.Interface;
using CTFServer.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace CTFServer.Services;

public class ConfigService : IConfigService
{
    private readonly ILogger<ConfigService> logger;
    private readonly IConfigurationRoot? configuration;
    private readonly AppDbContext context;
    private readonly IStringLocalizer<ServiceResource> localizer;

    public ConfigService(AppDbContext _context,
        ILogger<ConfigService> _logger,
        IStringLocalizer<ServiceResource> _localizer,
        IConfiguration _configuration)
    {
        context = _context;
        logger = _logger;
        localizer = _localizer;
        configuration = _configuration as IConfigurationRoot;
    }

    private static void MapConfigsInternal(string key, HashSet<Config> configs, Type? type, object? value)
    {
        if (value is null || type is null)
            return;

        if (type.IsArray || IsArrayLikeInterface(type))
            throw new InvalidOperationException("Unsupported config item type");

        TypeConverter converter = TypeDescriptor.GetConverter(type);
        if (type == typeof(string) || type.IsValueType)
        {
            configs.Add(new(key, converter.ConvertToString(value) ?? String.Empty));
        }
        else if (type.IsClass)
        {
            foreach (var item in type.GetProperties())
                MapConfigsInternal($"{key}:{item.Name}", configs, item.PropertyType, item.GetValue(value));
        }
    }

    public static HashSet<Config> GetConfigs(Type type, object? value)
    {
        HashSet<Config> configs = new();

        foreach (var item in type.GetProperties())
            MapConfigsInternal($"{type.Name}:{item.Name}", configs, item.PropertyType, item.GetValue(value));

        return configs;
    }

    public static HashSet<Config> GetConfigs<T>(T config) where T : class
    {
        HashSet<Config> configs = new();
        var type = typeof(T);

        foreach (var item in type.GetProperties())
            MapConfigsInternal($"{type.Name}:{item.Name}", configs, item.PropertyType, item.GetValue(config));

        return configs;
    }

    public Task SaveConfig(Type type, object? value, CancellationToken token = default)
        => SaveConfigInternal(GetConfigs(type, value), token);

    public Task SaveConfig<T>(T config, CancellationToken token = default) where T : class
        => SaveConfigInternal(GetConfigs(config), token);

    private async Task SaveConfigInternal(HashSet<Config> configs, CancellationToken token = default)
    {
        var dbConfigs = await context.Configs.ToDictionaryAsync(c => c.ConfigKey, c => c, token);
        foreach (var conf in configs)
        {
            if (dbConfigs.TryGetValue(conf.ConfigKey, out var dbConf))
            {
                if (dbConf.Value != conf.Value)
                {
                    dbConf.Value = conf.Value;
                    logger.SystemLog($"{localizer["Updated global config"]}: {conf.ConfigKey} => {conf.Value}", TaskStatus.Success, LogLevel.Debug);
                }
            }
            else
            {
                logger.SystemLog($"{localizer["Added global config"]}：{conf.ConfigKey} => {conf.Value}", TaskStatus.Success, LogLevel.Debug);
                await context.Configs.AddAsync(conf, token);
            }
        }

        await context.SaveChangesAsync(token);
        configuration?.Reload();
    }

    private static bool IsArrayLikeInterface(Type type)
    {
        if (!type.IsInterface || !type.IsConstructedGenericType)
        { return false; }

        Type genericTypeDefinition = type.GetGenericTypeDefinition();
        return genericTypeDefinition == typeof(IEnumerable<>)
            || genericTypeDefinition == typeof(ICollection<>)
            || genericTypeDefinition == typeof(IList<>)
            || genericTypeDefinition == typeof(IDictionary<,>)
            || genericTypeDefinition == typeof(ISet<>);
    }

    public void ReloadConfig() => configuration?.Reload();
}
