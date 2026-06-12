using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Kafdoc.Domain;

namespace Kafdoc.Infrastructure;

public static class Configuration
{
    public static void ConfigureInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
    }
}
