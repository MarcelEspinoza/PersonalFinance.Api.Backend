using System.Security.Claims;

namespace PersonalFinance.Api.Extensions
{
    public static class ClaimsPrincipalExtensions
    {
        /// <summary>
        /// Obtiene el identificador del usuario autenticado a partir de los claims del token.
        /// Devuelve null si no hay un identificador válido, para que el llamante responda 401
        /// en lugar de consultar la base de datos con un Guid vacío.
        /// </summary>
        public static Guid? GetUserId(this ClaimsPrincipal? principal)
        {
            var claim = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? principal?.FindFirst("id")?.Value;

            if (string.IsNullOrEmpty(claim)) return null;
            return Guid.TryParse(claim, out var userId) ? userId : null;
        }
    }
}
