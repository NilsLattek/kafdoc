using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Kafdoc.Application.Services;

namespace Kafdoc.Application;

public static class Configuration
{
    public static void ConfigureApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ITopicAppService, TopicAppService>();
    }
}