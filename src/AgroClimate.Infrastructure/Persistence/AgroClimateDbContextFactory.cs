using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AgroClimate.Infrastructure.Persistence;

public sealed class AgroClimateDbContextFactory : IDesignTimeDbContextFactory<AgroClimateDbContext>
{
    public AgroClimateDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AgroClimateDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=localhost;Database=IrGau_AgroClimateDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True");

        return new AgroClimateDbContext(optionsBuilder.Options);
    }
}
