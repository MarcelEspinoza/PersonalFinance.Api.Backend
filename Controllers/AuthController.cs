using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using PersonalFinance.Api.Models.Dtos.Auth;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Services;
using PersonalFinance.Api.Services.Contracts;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace PersonalFinance.Api.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IConfiguration _config;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<AuthController> _logger;

        public AuthController(UserManager<ApplicationUser> userManager,
                              SignInManager<ApplicationUser> signInManager,
                              IConfiguration config,
                              IEmailSender emailSender,
                              ILogger<AuthController> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _config = config;
            _emailSender = emailSender;
            _logger = logger;
        }

        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            var exists = await _userManager.FindByEmailAsync(dto.Email);
            if (exists != null) return BadRequest("Email already registered");

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = dto.Email,
                Email = dto.Email,
                EmailConfirmed = false,
                FullName = dto.FullName
            };

            var create = await _userManager.CreateAsync(user, dto.Password);
            if (!create.Succeeded) return BadRequest(create.Errors);

            // Assign Invited role by default
            await _userManager.AddToRoleAsync(user, "Invited");

            // Generate email confirmation token
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);

            // Build confirmation link (we provide a backend GET endpoint that confirms and redirects)
            var apiBase = _config["ApiBaseUrl"] ?? $"{Request.Scheme}://{Request.Host.Value}";
            var encodedToken = Uri.EscapeDataString(token);
            var callbackUrl = $"{apiBase}/api/auth/confirm-email?email={Uri.EscapeDataString(user.Email)}&token={encodedToken}";

            // Send email using IEmailSender
            var html = EmailTemplates.ConfirmEmailTemplate(user!.FullName, callbackUrl);
            await _emailSender.SendEmailAsync(user.Email, "Confirma tu correo en Personal Finance", html);

            // Return minimal response (token not returned now because we sent it by email)
            return Ok(new { user.Id, user.Email, message = "Usuario creado. Se ha enviado un correo de confirmación." });
        }

        // Existing POST confirm-email kept for API callers (frontend)
        [HttpPost("confirm-email")]
        [AllowAnonymous]
        public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailDto dto)
        {
            var user = await _userManager.FindByEmailAsync(dto.Email);
            if (user == null) return NotFound("Usuario no encontrado");

            var res = await _userManager.ConfirmEmailAsync(user, dto.Token);
            if (!res.Succeeded) return BadRequest(res.Errors);

            // Promote Invited -> User
            if (await _userManager.IsInRoleAsync(user, "Invited"))
            {
                await _userManager.AddToRoleAsync(user, "User");
                await _userManager.RemoveFromRoleAsync(user, "Invited");
            }

            return Ok("Email confirmado");
        }

        // New GET endpoint to confirm via link in email and redirect to frontend
        [HttpGet("confirm-email")]
        [AllowAnonymous]
        public async Task<IActionResult> ConfirmEmailGet([FromQuery] string email, [FromQuery] string token)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
                return BadRequest("Missing email or token");

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null) return NotFound("User not found");

            var res = await _userManager.ConfirmEmailAsync(user, token);
            if (!res.Succeeded)
            {
                // You may want to redirect to a frontend page that informs of failure
                var failUrl = _config["Frontend:EmailConfirmationFailedUrl"] ?? _config["Frontend:Url"] ?? "/";
                return Redirect($"{failUrl}?email={Uri.EscapeDataString(email)}&status=failed");
            }

            // Promote Invited -> User
            if (await _userManager.IsInRoleAsync(user, "Invited"))
            {
                await _userManager.AddToRoleAsync(user, "User");
                await _userManager.RemoveFromRoleAsync(user, "Invited");
            }

            var successUrl = _config["Frontend:EmailConfirmedUrl"] ?? _config["Frontend:Url"] ?? "/";
            return Redirect($"{successUrl}?email={Uri.EscapeDataString(email)}&status=success");
        }

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            var user = await _userManager.FindByEmailAsync(dto.Email);
            if (user == null) return Unauthorized("Invalid credentials");

            var signRes = await _signInManager.CheckPasswordSignInAsync(user, dto.Password, false);
            if (!signRes.Succeeded)
            {
                // Mejor diagnóstico para desarrollo (opcional)
                if (signRes.IsLockedOut)
                    return StatusCode(StatusCodes.Status423Locked, "Account locked");

                if (signRes.IsNotAllowed)
                    // Devuelve 403 explícito con mensaje; NO pasar texto a Forbid()
                    return StatusCode(StatusCodes.Status403Forbidden, "Email not confirmed");

                return Unauthorized("Invalid credentials");
            }

            var roles = await _userManager.GetRolesAsync(user);
            var token = GenerateJwtToken(user, roles);

            return Ok(new { token });
        }

        [HttpPost("request-password-reset")]
        [AllowAnonymous]
        public async Task<IActionResult> RequestPasswordReset([FromBody] RequestPasswordResetDto dto)
        {
            var user = await _userManager.FindByEmailAsync(dto.Email);
            if (user == null) return NotFound("User not found");
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            if (token == null) return BadRequest("Could not generate reset token");
            return Ok(new { email = dto.Email, token });
        }

        [HttpPost("reset-password")]
        [AllowAnonymous]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
        {
            var user = await _userManager.FindByEmailAsync(dto.Email);
            if (user == null) return NotFound("User not found");
            var result = await _userManager.ResetPasswordAsync(user, dto.Token, dto.Password);
            if (!result.Succeeded) return BadRequest(result.Errors);
            return NoContent();
        }


        private string GenerateJwtToken(ApplicationUser user, IList<string> roles)
        {
            // obtenemos la key (seguimos fallando si no existe la clave de firma)
            var key = _config["Jwt:Key"];
            if (string.IsNullOrWhiteSpace(key))
            {
                _logger.LogError("Jwt:Key is not configured. Cannot generate JWT.");
                throw new InvalidOperationException("JWT key is not configured.");
            }

            // leer y normalizar expire minutes
            var expireRaw = _config["Jwt:ExpireMinutes"] ?? string.Empty;
            var expireTrimmed = expireRaw.Trim();
            const double defaultExpireMinutes = 1440; // 24h por defecto
            double expireMinutes;

            if (!double.TryParse(expireTrimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out expireMinutes) || expireMinutes <= 0)
            {
                _logger.LogWarning("Invalid or missing Jwt:ExpireMinutes ('{ValuePreview}'). Falling back to default {Default} minutes.",
                    // mostramos solo una vista corta para depuración; no imprimimos secretos
                    expireTrimmed.Length > 0 ? "[present]" : "[empty]",
                    defaultExpireMinutes);
                expireMinutes = defaultExpireMinutes;
            }

            var claims = new List<Claim>
    {
        new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
        new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
        new Claim(ClaimTypes.Name, user.UserName ?? string.Empty),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
    };

            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var keyBytes = Encoding.UTF8.GetBytes(key);
            var securityKey = new SymmetricSecurityKey(keyBytes);
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(expireMinutes),
                signingCredentials: credentials
            );

            return tokenHandler.WriteToken(token);
        }
    }

    // DTOs (mantengo los records que ya usabas)
    public record RegisterDto(string Email, string Password, string? FullName);
    public record LoginDto(string Email, string Password);
    public record ConfirmEmailDto(string Email, string Token);
}