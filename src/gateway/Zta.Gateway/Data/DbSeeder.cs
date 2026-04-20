using Microsoft.EntityFrameworkCore;
using Zta.Gateway.Models;

namespace Zta.Gateway.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var policyDb = services.GetRequiredService<PolicyDbContext>();

        if (!await policyDb.AccessPolicies.AnyAsync())
        {
            policyDb.AccessPolicies.AddRange(
                new AccessPolicy
                {
                    UserId = "prayas",
                    PathPrefix = "/api/data",
                    HttpMethod = "GET",
                    IsEnabled = true
                },
                new AccessPolicy
                {
                    UserId = "prayas",
                    PathPrefix = "/api/auth",
                    HttpMethod = "POST",
                    IsEnabled = true
                });

            await policyDb.SaveChangesAsync();
        }
    }
}
