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

            // 👇 Lee la cadena de conexión del entorno o del appsettings.json
            var conn = config.GetConnectionString("DefaultConnection")
                ?? "Server=localhost;Port=3306;Database=PersonalFinanceDb;User=root;Password=1234;SslMode=Preferred;";

            // 👇 Usa el proveedor MySQL (Pomelo)
            builder.UseMySql(conn, ServerVersion.AutoDetect(conn));

            return new AppDbContext(builder.Options);
        }
    }
}
