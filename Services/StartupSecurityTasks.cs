using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models.Entities;

namespace PersonalFinance.Api.Services
{
    /// <summary>
    /// Tareas de arranque ligadas a la propiedad de los datos y a la aprobación de cuentas.
    /// Son idempotentes: se pueden ejecutar en cada arranque.
    /// </summary>
    public static class StartupSecurityTasks
    {
        /// <summary>
        /// Asigna los pasanacos sin propietario al único usuario con rol Admin.
        /// Si no hay exactamente un Admin no toca nada y deja un aviso en el log.
        /// </summary>
        public static async Task<int> BackfillPasanacoOwnersAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
        {
            var orphanCount = await db.Pasanacos.CountAsync(p => p.UserId == null, ct);
            if (orphanCount == 0) return 0;

            var adminIds = await (
                from ur in db.UserRoles
                join r in db.Roles on ur.RoleId equals r.Id
                where r.NormalizedName == "ADMIN"
                select ur.UserId).ToListAsync(ct);

            if (adminIds.Count != 1)
            {
                logger.LogWarning(
                    "Hay {Count} pasanacos sin propietario y {AdminCount} usuarios con rol Admin; no se asignan automáticamente (se necesita exactamente un Admin). Nadie podrá verlos hasta que se les asigne UserId.",
                    orphanCount, adminIds.Count);
                return 0;
            }

            var adminId = adminIds[0];
            var assigned = await db.Pasanacos
                .Where(p => p.UserId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.UserId, (Guid?)adminId), ct);

            logger.LogInformation("Asignados {Count} pasanacos sin propietario al administrador {AdminId}.", assigned, adminId);
            return assigned;
        }

        /// <summary>
        /// Usuarios sin rol "Admin" ni "User" (pendientes de aprobación): no pueden usar la API.
        /// </summary>
        public static Task<int> CountUsersPendingApprovalAsync(AppDbContext db, CancellationToken ct = default)
        {
            var approvedRoleIds = db.Roles
                .Where(r => r.NormalizedName == "ADMIN" || r.NormalizedName == "USER")
                .Select(r => r.Id);

            // db.Users es la tabla heredada User; las cuentas de Identity son ApplicationUser.
            return db.Set<ApplicationUser>().CountAsync(
                u => !db.UserRoles.Any(ur => ur.UserId == u.Id && approvedRoleIds.Contains(ur.RoleId)),
                ct);
        }
    }
}
