using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Kafdoc.Domain.Services;

namespace Kafdoc.Domain;

public static class Configuration
{
    public static void ConfigureDomain(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<TopicService>();
    }
}