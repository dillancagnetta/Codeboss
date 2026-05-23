
using Microsoft.Extensions.Configuration;

namespace CodeBoss.Extensions;

public static class ConfigurationExtensions
{
    public static TModel GetOptions<TModel>(this IConfiguration configuration, string sectionName) where TModel : new()
    {
        TModel instance = new TModel();
        configuration.GetSection(sectionName).Bind((object) instance);
        return instance;
    }
}