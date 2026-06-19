using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cerdik.Infrastructure.Persistence;

/// <summary>Design-time factory so the EF Core CLI (`dotnet ef migrations add` /
/// `dotnet ef database update`) can construct <see cref="AppDbContext"/> WITHOUT booting the API.
///
/// This is used ONLY by the `dotnet ef` tooling at design time — it is never resolved at runtime,
/// so it does not affect the application's startup or DI. It reads DATABASE_URL when present and
/// otherwise falls back to a local placeholder (generating a migration needs the provider, not a
/// live database connection).</summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? "Server=localhost,1433;Database=cerdikmy;User Id=sa;Password=Cerdik!Passw0rd;TrustServerCertificate=True;Encrypt=False";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, sql =>
                sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        return new AppDbContext(options);
    }
}
