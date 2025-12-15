using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PersonalFinance.Api.Api.Utils;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Services;
using PersonalFinance.Api.Services.Contracts;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;
var env = builder.Environment;

// -------------------------------
// JSON / Controllers configuration
// -------------------------------
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        );

        opts.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        opts.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        opts.JsonSerializerOptions.Converters.Add(new UtcDateTimeConverter());
    });

// -------------------------------
// Data protection keys
// -------------------------------
// Las claves se almacenarán en memoria (funciona para una instancia única en Cloud Run).
builder.Services.AddDataProtection()
    .SetApplicationName("PersonalFinance");

// -------------------------------
// CORS - prefer a policy over manual OPTIONS handler
// -------------------------------
var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultCors", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
        }
        else
        {
            // Development-friendly fallback: allow all origins (no credentials)
            policy.AllowAnyOrigin()
                    .AllowAnyHeader()
                    .AllowAnyMethod();
        }
    });
});

// -------------------------------
// EF / DbContext
// -------------------------------
// 💡 CONFIGURACIÓN PARA SQL SERVER (CONFIRMADA)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// -------------------------------
// Identity (Option B): ASP.NET Core Identity with Guid keys
// -------------------------------
builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedEmail = true; // we want confirmed emails before role upgrade
    // Password policy - relax as required (example)
    options.Password.RequireDigit = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

// -------------------------------
// JWT Authentication
// -------------------------------
var jwtSection = configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"] ?? throw new InvalidOperationException("Jwt:Key not configured");
var jwtIssuer = jwtSection["Issuer"] ?? "PersonalFinance.Api";
var jwtAudience = jwtSection["Audience"] ?? "PersonalFinance.Api.Client";
var key = Encoding.UTF8.GetBytes(jwtKey);

// LOG DE DEBUG TEMPORAL: Esto aparecerá en los logs de Cloud Run (si llega hasta aquí)
Console.WriteLine($"[DEBUG] JWT Key Length: {jwtKey.Length > 0}, Issuer: {jwtIssuer}");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = !env.IsDevelopment();
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier,
        RoleClaimType = System.Security.Claims.ClaimTypes.Role
    };
});

// -------------------------------
// Swagger & OpenAPI (with Bearer auth)
// -------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter: 'Bearer {token}'",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement {
        {
            new OpenApiSecurityScheme {
                Reference = new OpenApiReference {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddAutoMapper(AppDomain.CurrentDomain.GetAssemblies());

// -------------------------------
// Application services registration
// -------------------------------
builder.Services.AddScoped<IRoleSeeder, RoleSeeder>();
builder.Services.AddHttpContextAccessor();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
});

// Register your application services here
builder.Services.AddScoped<IPasswordHasher<ApplicationUser>, PasswordHasher<ApplicationUser>>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IIncomeService, IncomeService>();
builder.Services.AddScoped<IExpenseService, ExpenseService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IMonthlyService, MonthlyService>();
builder.Services.AddScoped<IImportExcelService, ImportExcelService>();
builder.Services.AddScoped<ILoanService, LoanService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<ISavingService, SavingService>();
builder.Services.AddScoped<IPasanacoService, PasanacoService>();
builder.Services.AddSingleton<IEmailSender, SendGridEmailSender>();
builder.Services.AddScoped<IBankService, BankService>();
builder.Services.AddScoped<IReconciliationService, ReconciliationService>();
builder.Services.AddScoped<ITransferService, TransferService>();
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();


// -------------------------------
// Build application
// -------------------------------
var app = builder.Build();

// -------------------------------
// Seed roles and optionally an admin user on startup
// -------------------------------
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();

    try
    {
        var db = services.GetRequiredService<AppDbContext>();

        // Aplicar migraciones pendientes. Si la conexión/credenciales de SQL Server fallan, la aplicación morirá aquí.
        var pending = await db.Database.GetPendingMigrationsAsync();
        if (pending != null && pending.Any())
        {
            logger.LogInformation("Se han detectado {Count} migraciones pendientes. Aplicando migraciones...", pending.Count());
            db.Database.Migrate();
            logger.LogInformation("Migraciones aplicadas correctamente.");
        }
        else
        {
            logger.LogInformation("No hay migraciones pendientes.");
        }

        // Ejecutar el seeder (roles / admin)
        var seeder = services.GetService<IRoleSeeder>();
        if (seeder != null)
        {
            try
            {
                await seeder.EnsureRolesAndAdminAsync();
                logger.LogInformation("RoleSeeder ejecutado correctamente.");
            }
            catch (Exception seederEx)
            {
                logger.LogError(seederEx, "Error ejecutando RoleSeeder");
            }
        }
        else
        {
            logger.LogWarning("No se encontró IRoleSeeder en DI; no se creó ningún role/usuario inicial.");
        }
    }
    catch (Exception ex)
    {
        // 🚨 SI LA APLICACIÓN FALLA AQUÍ, CLOUD RUN MUESTRA EL ERROR "failed to start and listen"
        logger.LogError(ex, "Error CRÍTICO durante la inicialización (migrations/seeder). LA CADENA DE CONEXIÓN O EL FIREWALL ES PROBABLEMENTE EL PROBLEMA.");
        throw; // Es crítico que el contenedor falle si no puede conectarse a la DB.
    }
}

// -------------------------------
// Middleware pipeline
// -------------------------------

app.UseSwagger();
app.UseSwaggerUI();


app.UseHttpsRedirection();

// Use CORS BEFORE authentication so preflight isn't blocked
app.UseCors("DefaultCors");

app.UseAuthentication();
app.UseAuthorization();

// Map minimal helpful endpoints (keeps your previous "/" and config/cors)
app.MapGet("/", () => Results.Ok("Personal Finance API is running"));
app.MapGet("/config/cors", (IConfiguration config) =>
{
    var origins = config.GetSection("Cors:AllowedOrigins").Get<string[]>();
    return Results.Json(origins ?? new[] { "No origins found" });
});

// Map controllers (your API endpoints)
app.MapControllers();

var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
app.Run($"http://0.0.0.0:{port}");
