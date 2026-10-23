using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PersonalFinance.Api.Controllers;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Services.Contracts;

namespace PersonalFinance.Api.Tests;

/// <summary>
/// Registro con aprobación: la cuenta nueva queda pendiente (aunque confirme el
/// correo) hasta que un administrador la aprueba.
/// </summary>
public sealed class AccountApprovalTests : LedgerTestBase, IAsyncLifetime
{
    private const string AdminEmail = "admin@test.local";
    private const string Password = "secreto123";

    private readonly ServiceProvider _identity;
    private readonly IServiceScope _scope;
    private readonly RecordingEmailSender _emails = new();
    private readonly IConfiguration _configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
            ["Jwt:Issuer"] = "PersonalFinance.Api.Tests",
            ["Jwt:Audience"] = "PersonalFinance.Api.Tests",
            ["ApiBaseUrl"] = "https://api.test.local"
        })
        .Build();

    public AccountApprovalTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Db);
        services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = true;
                options.Password.RequireDigit = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();
        // Claves en memoria: la protección de datos por defecto las guardaría en disco.
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());

        _identity = services.BuildServiceProvider();
        _scope = _identity.CreateScope();
    }

    private UserManager<ApplicationUser> Users => _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    public async Task InitializeAsync()
    {
        var roles = _scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in new[] { "Admin", "User", "Invited" })
            Assert.True((await roles.CreateAsync(new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = role })).Succeeded);

        var admin = new ApplicationUser { Id = Guid.NewGuid(), UserName = AdminEmail, Email = AdminEmail, EmailConfirmed = true, FullName = "Admin" };
        Assert.True((await Users.CreateAsync(admin, Password)).Succeeded);
        Assert.True((await Users.AddToRoleAsync(admin, "Admin")).Succeeded);
    }

    public Task DisposeAsync()
    {
        _scope.Dispose();
        _identity.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Una_cuenta_nueva_queda_pendiente_hasta_que_el_admin_la_aprueba()
    {
        var auth = CreateAuthController();

        var registered = Assert.IsType<OkObjectResult>(
            await auth.Register(new RegisterDto("ana@test.local", Password, "Ana <b>Lopez</b>")));
        Assert.Equal(
            "Usuario creado. Revisa tu correo para confirmarlo. Después, el administrador debe aprobar tu cuenta.",
            Json(registered.Value).GetProperty("message").GetString());

        var notification = Assert.Single(_emails.Sent, e => e.To == AdminEmail);
        Assert.Equal("Nueva solicitud de registro en Personal Finance", notification.Subject);
        Assert.Contains("Ana &lt;b&gt;Lopez&lt;/b&gt;", notification.Html);
        Assert.Contains("ana@test.local", notification.Html);
        Assert.Contains("Ajustes → Administración", notification.Html);

        // Confirmar el correo ya no aprueba la cuenta.
        var user = await Users.FindByEmailAsync("ana@test.local");
        Assert.NotNull(user);
        var token = await Users.GenerateEmailConfirmationTokenAsync(user!);
        Assert.IsType<OkObjectResult>(await auth.ConfirmEmail(new ConfirmEmailDto(user!.Email!, token)));
        Assert.Equal(new[] { "Invited" }, await Users.GetRolesAsync(user));

        var pending = Assert.IsType<ObjectResult>(await auth.Login(new LoginDto("ana@test.local", Password)));
        Assert.Equal(StatusCodes.Status403Forbidden, pending.StatusCode);
        Assert.Equal("account_pending_approval", Json(pending.Value).GetProperty("error").GetString());

        var admin = CreateAdminController();
        var listed = Json(Assert.IsType<OkObjectResult>(await admin.GetUsers()).Value).EnumerateArray()
            .Single(u => u.GetProperty("email").GetString() == "ana@test.local");
        Assert.True(listed.GetProperty("pendingApproval").GetBoolean());
        Assert.True(listed.GetProperty("emailConfirmed").GetBoolean());

        var approved = Assert.IsType<OkObjectResult>(await admin.ApproveUser(user.Id));
        Assert.Equal(new[] { "User" }, Assert.IsAssignableFrom<IEnumerable<string>>(approved.Value));
        Assert.Single(_emails.Sent, e => e.To == "ana@test.local" && e.Subject == "Tu cuenta en Personal Finance ha sido aprobada");

        var login = Assert.IsType<OkObjectResult>(await auth.Login(new LoginDto("ana@test.local", Password)));
        Assert.False(string.IsNullOrEmpty(Json(login.Value).GetProperty("token").GetString()));

        // Aprobar de nuevo no cambia nada ni reenvía el aviso.
        Assert.IsType<OkObjectResult>(await admin.ApproveUser(user.Id));
        Assert.Single(_emails.Sent, e => e.Subject == "Tu cuenta en Personal Finance ha sido aprobada");
    }

    [Fact]
    public async Task Los_fallos_de_correo_no_rompen_el_registro_ni_la_aprobacion()
    {
        _emails.FailWhen = subject => subject != "Confirma tu correo en Personal Finance";
        var auth = CreateAuthController();

        Assert.IsType<OkObjectResult>(await auth.Register(new RegisterDto("luis@test.local", Password, "Luis")));
        var user = await Users.FindByEmailAsync("luis@test.local");
        Assert.NotNull(user);

        var approved = Assert.IsType<OkObjectResult>(await CreateAdminController().ApproveUser(user!.Id));
        Assert.Equal(new[] { "User" }, Assert.IsAssignableFrom<IEnumerable<string>>(approved.Value));
    }

    [Fact]
    public async Task Aprobar_un_usuario_inexistente_devuelve_404()
    {
        Assert.IsType<NotFoundResult>(await CreateAdminController().ApproveUser(Guid.NewGuid()));
    }

    private AuthController CreateAuthController() => WithHttpContext(new AuthController(
        Users,
        _scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>(),
        _configuration,
        _emails,
        NullLogger<AuthController>.Instance));

    private AdminController CreateAdminController() => WithHttpContext(new AdminController(
        Users,
        _scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>(),
        _emails,
        NullLogger<AdminController>.Instance));

    private static T WithHttpContext<T>(T controller) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value);

    /// <summary>Guarda los correos en memoria y puede simular fallos de envío.</summary>
    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<(string To, string Subject, string Html)> Sent { get; } = new();

        public Func<string, bool> FailWhen { get; set; } = _ => false;

        public Task SendEmailAsync(string toEmail, string subject, string htmlContent)
        {
            if (FailWhen(subject))
                throw new InvalidOperationException("Fallo simulado de SendGrid");

            Sent.Add((toEmail, subject, htmlContent));
            return Task.CompletedTask;
        }
    }
}
