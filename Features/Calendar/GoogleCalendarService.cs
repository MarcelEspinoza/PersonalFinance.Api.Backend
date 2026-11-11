using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Exceptions;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models.Entities;

namespace PersonalFinance.Api.Features.Calendar
{
    public sealed record GoogleCalendarStateDto(
        bool Configured, bool Connected, bool NeedsReconnect, int PendingCount,
        DateTime? LastSyncedAt, string? Error, string TimeZone, int ZeusColorId, int PasanacoColorId);

    public sealed class GoogleCalendarService(
        AppDbContext db, GoogleCalendarSettings settings, GoogleCalendarTokenProtector protector,
        GoogleCalendarClient client, GoogleCalendarProjection projection, IClock clock,
        ILogger<GoogleCalendarService> logger)
    {
        public async Task<GoogleCalendarStateDto> GetStateAsync(Guid userId, CancellationToken ct)
        {
            var connection = await db.GoogleCalendarConnections.AsNoTracking()
                .FirstOrDefaultAsync(item => item.UserId == userId, ct);
            var pending = db.GoogleSyncItems.AsNoTracking().Where(item => item.UserId == userId && item.NeedsSync);
            var pendingError = await pending.Where(item => item.LastError != null)
                .OrderBy(item => item.RetryAt).Select(item => item.LastError).FirstOrDefaultAsync(ct);
            var lastSyncedAt = connection?.LastSyncedAt is { } last
                ? DateTime.SpecifyKind(last, DateTimeKind.Utc) : (DateTime?)null;
            return new(settings.IsConfigured, connection?.RefreshToken is not null && !connection.NeedsReconnect,
                connection?.NeedsReconnect ?? false, await pending.CountAsync(ct), lastSyncedAt,
                connection?.LastError ?? pendingError, settings.TimeZone,
                connection?.ZeusColorId ?? GoogleCalendarConnection.DefaultZeusColorId,
                connection?.PasanacoColorId ?? GoogleCalendarConnection.DefaultPasanacoColorId);
        }

        public async Task UpdateColorsAsync(Guid userId, int zeusColorId, int pasanacoColorId, CancellationToken ct)
        {
            if (zeusColorId is < 1 or > 11 || pasanacoColorId is < 1 or > 11)
                throw new BusinessRuleException("Selecciona colores válidos de Google Calendar.");
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var connection = await db.GoogleCalendarConnections.FirstOrDefaultAsync(item => item.UserId == userId, ct);
            if (connection is null)
            {
                connection = new GoogleCalendarConnection { UserId = userId };
                db.GoogleCalendarConnections.Add(connection);
            }
            else if (connection.ZeusColorId == zeusColorId && connection.PasanacoColorId == pasanacoColorId)
            {
                await transaction.CommitAsync(ct);
                return;
            }
            connection.ZeusColorId = zeusColorId;
            connection.PasanacoColorId = pasanacoColorId;
            connection.Generation = Guid.NewGuid();
            connection.LeaseId = null;
            connection.LeaseUntil = null;
            connection.LastSyncedAt = null;
            connection.LastError = null;
            connection.Attempts = 0;
            connection.RetryAt = null;
            await db.GoogleSyncItems.Where(item => item.UserId == userId && item.ResourceKind == "event")
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.NeedsSync, true)
                    .SetProperty(item => item.RetryAt, clock.UtcNow)
                    .SetProperty(item => item.Attempts, 0)
                    .SetProperty(item => item.LastError, (string?)null), ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        public async Task<string> StartConnectionAsync(Guid userId, string returnPath, CancellationToken ct)
        {
            settings.EnsureConfigured();
            if (returnPath is not ("/zeus" or "/pasanaco" or "/reminders" or "/settings"))
                throw new BusinessRuleException("La página de retorno no es válida.");
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var connection = await db.GoogleCalendarConnections.FirstOrDefaultAsync(item => item.UserId == userId, ct);
            if (connection is null)
            {
                connection = new GoogleCalendarConnection { UserId = userId };
                db.GoogleCalendarConnections.Add(connection);
            }
            await db.GoogleCalendarAuthorizations.Where(item => item.UserId == userId).ExecuteDeleteAsync(ct);
            var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            db.GoogleCalendarAuthorizations.Add(new GoogleCalendarAuthorization
            {
                StateHash = Hash(state),
                UserId = userId,
                ConnectionGeneration = connection.Generation,
                CodeVerifier = protector.Protect(verifier, userId),
                ReturnPath = returnPath,
                ExpiresAt = clock.UtcNow.AddMinutes(10)
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return QueryHelpers.AddQueryString("https://accounts.google.com/o/oauth2/v2/auth",
                new Dictionary<string, string?>
                {
                    ["client_id"] = settings.ClientId,
                    ["redirect_uri"] = settings.RedirectUri,
                    ["response_type"] = "code",
                    ["scope"] = GoogleCalendarClient.Scope + " " + GoogleCalendarClient.TasksScope,
                    ["access_type"] = "offline",
                    ["prompt"] = "consent select_account",
                    ["state"] = state,
                    ["code_challenge"] = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
                    ["code_challenge_method"] = "S256"
                });
        }

        public async Task<string> CompleteConnectionAsync(string? state, string? code, string? error, CancellationToken ct)
        {
            settings.EnsureConfigured();
            if (string.IsNullOrWhiteSpace(state))
                throw new BusinessRuleException("La autorización de Google no es válida. Vuelve a conectar.");
            var hash = Hash(state);
            var authorization = await db.GoogleCalendarAuthorizations.AsNoTracking()
                .FirstOrDefaultAsync(item => item.StateHash == hash && item.ExpiresAt > clock.UtcNow, ct);
            if (authorization is null || await db.GoogleCalendarAuthorizations
                    .Where(item => item.StateHash == hash && item.ExpiresAt > clock.UtcNow).ExecuteDeleteAsync(ct) != 1)
                throw new BusinessRuleException("La autorización de Google caducó o ya se utilizó. Vuelve a conectar.");
            if (!string.IsNullOrEmpty(error))
                return settings.ReturnUrl(authorization.ReturnPath, "denied");
            try
            {
                if (string.IsNullOrWhiteSpace(code))
                    throw new GoogleCalendarRemoteException("Google no devolvió el código de autorización.");
                var tokens = await client.ExchangeAsync(code, protector.Unprotect(authorization.CodeVerifier, authorization.UserId), ct);
                if (string.IsNullOrWhiteSpace(tokens.RefreshToken))
                    throw new GoogleCalendarRemoteException("Google no concedió acceso sin conexión. Vuelve a conectar y acepta el permiso.");
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var connection = await db.GoogleCalendarConnections.FirstOrDefaultAsync(
                    item => item.UserId == authorization.UserId, ct);
                if (connection is null || connection.Generation != authorization.ConnectionGeneration)
                    throw new GoogleCalendarRemoteException("La conexión cambió durante la autorización. Vuelve a conectar.");
                connection.Generation = Guid.NewGuid();
                connection.AccessToken = protector.Protect(tokens.AccessToken, connection.UserId);
                connection.RefreshToken = protector.Protect(tokens.RefreshToken, connection.UserId);
                connection.TaskListId = null;
                connection.AccessTokenExpiresAt = clock.UtcNow.AddSeconds(tokens.ExpiresIn);
                connection.ConnectedAt = clock.UtcNow;
                connection.NeedsReconnect = false;
                connection.Attempts = 0;
                connection.RetryAt = null;
                connection.LastError = null;
                connection.LastSyncedAt = null;
                connection.LeaseId = null;
                connection.LeaseUntil = null;
                await db.GoogleSyncItems.Where(item => item.UserId == connection.UserId).ExecuteUpdateAsync(
                    setters => setters.SetProperty(item => item.NeedsSync, true)
                        .SetProperty(item => item.RetryAt, clock.UtcNow)
                        .SetProperty(item => item.Attempts, 0)
                        .SetProperty(item => item.LastError, (string?)null), ct);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return settings.ReturnUrl(authorization.ReturnPath, "connected");
            }
            catch (Exception ex) when (ex is GoogleCalendarRemoteException or HttpRequestException
                or JsonException or CryptographicException
                || ex is OperationCanceledException && !ct.IsCancellationRequested)
            {
                var message = ex is GoogleCalendarRemoteException ? ex.Message
                    : "No se pudo completar la conexión con Google. Vuelve a intentarlo.";
                logger.LogWarning(ex, "Google authorization failed for {UserId}.", authorization.UserId);
                await db.GoogleCalendarConnections
                    .Where(item => item.UserId == authorization.UserId && item.Generation == authorization.ConnectionGeneration)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.LastError, message), ct);
                return settings.ReturnUrl(authorization.ReturnPath, "error");
            }
        }

        public async Task DisconnectAsync(Guid userId, CancellationToken ct)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.GoogleCalendarAuthorizations.Where(item => item.UserId == userId).ExecuteDeleteAsync(ct);
            await db.GoogleCalendarConnections.Where(item => item.UserId == userId).ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.Generation, Guid.NewGuid())
                    .SetProperty(item => item.AccessToken, (string?)null)
                    .SetProperty(item => item.RefreshToken, (string?)null)
                    .SetProperty(item => item.AccessTokenExpiresAt, (DateTime?)null)
                    .SetProperty(item => item.ConnectedAt, (DateTime?)null)
                    .SetProperty(item => item.LastSyncedAt, (DateTime?)null)
                    .SetProperty(item => item.LastError, (string?)null)
                    .SetProperty(item => item.NeedsReconnect, false)
                    .SetProperty(item => item.Attempts, 0)
                    .SetProperty(item => item.RetryAt, (DateTime?)null)
                    .SetProperty(item => item.LeaseId, (Guid?)null)
                    .SetProperty(item => item.LeaseUntil, (DateTime?)null), ct);
            await transaction.CommitAsync(ct);
        }

        public async Task RetryAsync(Guid userId, CancellationToken ct)
        {
            settings.EnsureConfigured();
            if (!await db.GoogleCalendarConnections.AnyAsync(
                    item => item.UserId == userId && item.RefreshToken != null && !item.NeedsReconnect, ct))
                throw new BusinessRuleException("Primero conecta tu cuenta de Google.");
            await db.GoogleSyncItems.Where(item => item.UserId == userId).ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.NeedsSync, true)
                    .SetProperty(item => item.RetryAt, clock.UtcNow)
                    .SetProperty(item => item.Attempts, 0)
                    .SetProperty(item => item.LastError, (string?)null), ct);
            await db.GoogleCalendarConnections.Where(item => item.UserId == userId).ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.RetryAt, (DateTime?)null)
                    .SetProperty(item => item.Attempts, 0)
                    .SetProperty(item => item.LastError, (string?)null), ct);
        }

        public async Task SynchronizeAsync(Guid userId, CancellationToken ct)
        {
            if (!settings.IsConfigured) return;
            var lease = Guid.NewGuid();
            var now = clock.UtcNow;
            var claimed = await db.GoogleCalendarConnections.Where(item => item.UserId == userId
                    && item.RefreshToken != null && !item.NeedsReconnect
                    && (item.LeaseUntil == null || item.LeaseUntil < now)
                    && (item.RetryAt == null || item.RetryAt <= now))
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.LeaseId, lease)
                    .SetProperty(item => item.LeaseUntil, now.AddMinutes(5)), ct);
            if (claimed == 0) return;
            var connection = await db.GoogleCalendarConnections.AsNoTracking().SingleAsync(item => item.UserId == userId, ct);
            try
            {
                await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
                {
                    await projection.ReconcileAsync(connection, now, ct);
                    await transaction.CommitAsync(ct);
                }
                var pending = await db.GoogleSyncItems.AsNoTracking().Where(item => item.UserId == userId
                    && item.NeedsSync && item.RetryAt <= now).OrderBy(item => item.RetryAt).Take(50).ToListAsync(ct);
                if (pending.Count == 0)
                {
                    if (!await db.GoogleSyncItems.AnyAsync(item => item.UserId == userId && item.NeedsSync, ct))
                        await ConnectionQuery(connection, lease).ExecuteUpdateAsync(
                            setters => setters.SetProperty(item => item.LastSyncedAt, clock.UtcNow)
                                .SetProperty(item => item.LastError, (string?)null), ct);
                    return;
                }
                var accessToken = await AccessTokenAsync(connection, lease, ct);
                foreach (var item in pending)
                {
                    if (!await RenewLeaseAsync(connection, lease, ct)) return;
                    try
                    {
                        try
                        {
                            if (!await SendItemAsync(accessToken, connection, lease, item, ct)) return;
                        }
                        catch (GoogleCalendarRemoteException ex) when (ex.RequiresReconnect)
                        {
                            accessToken = await AccessTokenAsync(connection, lease, ct, forceRefresh: true);
                            if (!await SendItemAsync(accessToken, connection, lease, item, ct)) return;
                        }
                        if (!await OwnsLeaseAsync(connection, lease, ct)) return;
                        if (item.Payload is null)
                            await EventQuery(item, connection, lease).ExecuteDeleteAsync(ct);
                        else
                            await EventQuery(item, connection, lease).ExecuteUpdateAsync(
                                setters => setters.SetProperty(value => value.NeedsSync, false)
                                    .SetProperty(value => value.Attempts, 0)
                                    .SetProperty(value => value.LastError, (string?)null), ct);
                        await ConnectionQuery(connection, lease).ExecuteUpdateAsync(
                            setters => setters.SetProperty(value => value.LastSyncedAt, clock.UtcNow)
                                .SetProperty(value => value.LastError, (string?)null)
                                .SetProperty(value => value.Attempts, 0)
                                .SetProperty(value => value.RetryAt, (DateTime?)null), ct);
                    }
                    catch (GoogleCalendarRemoteException ex) when (!ex.RequiresReconnect)
                    {
                        logger.LogWarning("Google Calendar event {EventId} failed for {UserId}: {Reason}", item.Id, userId, ex.Message);
                        if (!await OwnsLeaseAsync(connection, lease, ct)) return;
                        await RecordEventFailureAsync(item, connection, lease, ex.Message, ct);
                    }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
                    {
                        if (ct.IsCancellationRequested) throw;
                        logger.LogWarning(ex, "Google Calendar event {EventId} could not be sent for {UserId}.", item.Id, userId);
                        await RecordEventFailureAsync(item, connection, lease,
                            "No se pudo contactar con Google Calendar. Se volverá a intentar.", ct);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Google Calendar synchronization failed for {UserId}.", userId);
                var reconnect = ex is GoogleCalendarRemoteException { RequiresReconnect: true } or CryptographicException;
                var message = ex is GoogleCalendarRemoteException ? ex.Message
                    : reconnect ? "No se pudieron leer las credenciales de Google. Vuelve a conectar."
                    : "No se pudo completar la sincronización con Google. Se volverá a intentar.";
                var retryAt = NextRetry(connection.Attempts);
                await ConnectionQuery(connection, lease).ExecuteUpdateAsync(
                    setters => setters.SetProperty(item => item.LastError, message)
                        .SetProperty(item => item.NeedsReconnect, reconnect)
                        .SetProperty(item => item.Attempts, item => item.Attempts + 1)
                        .SetProperty(item => item.RetryAt, retryAt), ct);
            }
            finally
            {
                using var release = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await ConnectionQuery(connection, lease).ExecuteUpdateAsync(
                    setters => setters.SetProperty(item => item.LeaseId, (Guid?)null)
                        .SetProperty(item => item.LeaseUntil, (DateTime?)null), release.Token);
            }
        }

        private async Task<string> AccessTokenAsync(
            GoogleCalendarConnection connection, Guid lease, CancellationToken ct, bool forceRefresh = false)
        {
            if (!forceRefresh && connection.AccessToken is not null
                && connection.AccessTokenExpiresAt > clock.UtcNow.AddMinutes(2))
                return protector.Unprotect(connection.AccessToken, connection.UserId);
            var tokens = await client.RefreshAsync(protector.Unprotect(connection.RefreshToken!, connection.UserId), ct);
            var accessToken = protector.Protect(tokens.AccessToken, connection.UserId);
            var refreshToken = string.IsNullOrWhiteSpace(tokens.RefreshToken)
                ? connection.RefreshToken : protector.Protect(tokens.RefreshToken, connection.UserId);
            var updated = await ConnectionQuery(connection, lease).ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.AccessToken, accessToken)
                    .SetProperty(item => item.RefreshToken, refreshToken)
                    .SetProperty(item => item.AccessTokenExpiresAt, clock.UtcNow.AddSeconds(tokens.ExpiresIn)), ct);
            if (updated != 1) throw new GoogleCalendarRemoteException("La conexión de Google cambió durante la sincronización.");
            return tokens.AccessToken;
        }

        private async Task<bool> SendItemAsync(string accessToken, GoogleCalendarConnection connection,
            Guid lease, GoogleSyncItem item, CancellationToken ct)
        {
            if (item.ResourceKind == "task")
            {
                var listId = await client.EnsureTaskListAsync(accessToken, connection.TaskListId, ct);
                if (listId != connection.TaskListId)
                {
                    if (await ConnectionQuery(connection, lease).ExecuteUpdateAsync(
                            setters => setters.SetProperty(value => value.TaskListId, listId), ct) != 1) return false;
                    connection.TaskListId = listId;
                }
                var remoteId = await client.SynchronizeTaskAsync(accessToken, listId, item, ct);
                if (remoteId != item.RemoteId && item.Payload is not null)
                {
                    if (await EventQuery(item, connection, lease).ExecuteUpdateAsync(
                            setters => setters.SetProperty(value => value.RemoteId, remoteId), ct) != 1) return false;
                    item.RemoteId = remoteId;
                }
                return true;
            }
            if (item.ResourceKind != "event")
                throw new InvalidOperationException("Unknown Google synchronization resource.");
            try
            {
                await client.SynchronizeAsync(accessToken, item, ct);
            }
            catch (GoogleCalendarEventDeletedException)
            {
                item.RemoteId = Guid.NewGuid().ToString("N");
                if (await EventQuery(item, connection, lease).ExecuteUpdateAsync(
                        setters => setters.SetProperty(value => value.RemoteId, item.RemoteId), ct) != 1) return false;
                await client.SynchronizeAsync(accessToken, item, ct);
            }
            return true;
        }

        private IQueryable<GoogleCalendarConnection> ConnectionQuery(GoogleCalendarConnection connection, Guid lease) =>
            db.GoogleCalendarConnections.Where(item => item.UserId == connection.UserId
                && item.Generation == connection.Generation && item.LeaseId == lease);

        private IQueryable<GoogleSyncItem> EventQuery(
            GoogleSyncItem item, GoogleCalendarConnection connection, Guid lease) =>
            db.GoogleSyncItems.Where(value => value.Id == item.Id && value.UserId == connection.UserId
                && value.DesiredHash == item.DesiredHash
                && db.GoogleCalendarConnections.Any(owner => owner.UserId == connection.UserId
                    && owner.Generation == connection.Generation && owner.LeaseId == lease));

        private async Task RecordEventFailureAsync(GoogleSyncItem item, GoogleCalendarConnection connection,
            Guid lease, string message, CancellationToken ct)
        {
            var retryAt = NextRetry(item.Attempts);
            await EventQuery(item, connection, lease).ExecuteUpdateAsync(
                setters => setters.SetProperty(value => value.Attempts, value => value.Attempts + 1)
                    .SetProperty(value => value.LastError, message)
                    .SetProperty(value => value.RetryAt, retryAt), ct);
        }

        private DateTime NextRetry(int attempts) =>
            clock.UtcNow.AddSeconds(Math.Min(3600, 30 * Math.Pow(2, Math.Min(attempts, 7))));

        private Task<bool> OwnsLeaseAsync(GoogleCalendarConnection connection, Guid lease, CancellationToken ct) =>
            ConnectionQuery(connection, lease).AnyAsync(ct);

        private async Task<bool> RenewLeaseAsync(GoogleCalendarConnection connection, Guid lease, CancellationToken ct) =>
            await ConnectionQuery(connection, lease).ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.LeaseUntil, clock.UtcNow.AddMinutes(5)), ct) == 1;

        private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
