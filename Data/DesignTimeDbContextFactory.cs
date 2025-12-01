namespace PersonalFinance.Api.Data
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Design;
    using Microsoft.Extensions.Configuration;
    using System.IO;

    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            var builder = new DbContextOptionsBuilder<AppDbContext>();

            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            // 💡 CORRECCIÓN: Cadena de conexión de SQL Server local/de desarrollo
            var conn = config.GetConnectionString("DefaultConnection")
                // Usa una cadena por defecto de SQL Server para desarrollo local, 
                // o asegúrate que tu appsettings.json tenga una sección DefaultConnection de SQL Server.
                // Reemplaza esta línea si usas una instancia local diferente.
                ?? "Server=(localdb)\\mssqllocaldb;Database=PersonalFinanceDb;Trusted_Connection=True;MultipleActiveResultSets=true";

            // 💡 CORRECCIÓN: Cambiamos UseNpgsql a UseSqlServer
            builder.UseSqlServer(conn);

            return new AppDbContext(builder.Options);
        }
    }
}