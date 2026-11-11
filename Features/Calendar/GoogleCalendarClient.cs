using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using PersonalFinance.Api.Models.Entities;

namespace PersonalFinance.Api.Features.Calendar
{
    public sealed class GoogleCalendarRemoteException(string message, bool requiresReconnect = false) : Exception(message)
    {
        public bool RequiresReconnect { get; } = requiresReconnect;
    }

    public sealed class GoogleCalendarEventDeletedException : Exception;

    public sealed class GoogleCalendarTokens
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;
        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
        [JsonPropertyName("scope")]
        public string? Scope { get; init; }
    }

    public sealed class GoogleCalendarClient(HttpClient http, GoogleCalendarSettings settings)
    {
        public const string Scope = "https://www.googleapis.com/auth/calendar.events";
        public const string TasksScope = "https://www.googleapis.com/auth/tasks";
        private const string EventsUrl = "https://www.googleapis.com/calendar/v3/calendars/primary/events";
        private const string TaskListsUrl = "https://tasks.googleapis.com/tasks/v1/users/@me/lists";
        private const string TasksUrl = "https://tasks.googleapis.com/tasks/v1/lists/";

        public Task<GoogleCalendarTokens> ExchangeAsync(string code, string verifier, CancellationToken ct) =>
            TokenAsync(new()
            {
                ["code"] = code,
                ["code_verifier"] = verifier,
                ["redirect_uri"] = settings.RedirectUri,
                ["grant_type"] = "authorization_code"
            }, ct);

        public Task<GoogleCalendarTokens> RefreshAsync(string refreshToken, CancellationToken ct) =>
            TokenAsync(new()
            {
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token"
            }, ct);

        private async Task<GoogleCalendarTokens> TokenAsync(Dictionary<string, string> fields, CancellationToken ct)
        {
            fields["client_id"] = settings.ClientId;
            fields["client_secret"] = settings.ClientSecret;
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
            {
                Content = new FormUrlEncodedContent(fields)
            };
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var invalidGrant = false;
                if (response.Content.Headers.ContentType?.MediaType == "application/json")
                {
                    using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                    invalidGrant = error.RootElement.TryGetProperty("error", out var value)
                        && value.ValueKind == JsonValueKind.String && value.GetString() == "invalid_grant";
                }
                throw new GoogleCalendarRemoteException(
                    invalidGrant ? "Google necesita que vuelvas a conectar tu cuenta."
                        : "Google no pudo autorizar la conexión. Revisa la configuración o inténtalo más tarde.",
                    invalidGrant);
            }
            var tokens = await response.Content.ReadFromJsonAsync<GoogleCalendarTokens>(ct);
            if (tokens is null || string.IsNullOrWhiteSpace(tokens.AccessToken)
                || tokens.ExpiresIn < 1 || tokens.ExpiresIn > 86400)
                throw new GoogleCalendarRemoteException("Google devolvió credenciales incompletas.");
            if (tokens.Scope is not null && new[] { Scope, TasksScope }
                    .Any(scope => !tokens.Scope.Split(' ').Contains(scope, StringComparer.Ordinal)))
                throw new GoogleCalendarRemoteException("Debes autorizar el acceso a Google Calendar y Google Tasks.", true);
            return tokens;
        }

        public async Task SynchronizeAsync(
            string accessToken, GoogleSyncItem item, CancellationToken ct, bool retryConflict = true)
        {
            if (string.IsNullOrWhiteSpace(item.RemoteId))
                throw new InvalidOperationException("The calendar event identity has not been persisted.");
            var url = EventsUrl + "/" + Uri.EscapeDataString(item.RemoteId);
            using var existing = await SendAsync(HttpMethod.Get, url, accessToken, null, ct);
            var absent = existing.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone;
            string? etag = null;
            if (!absent)
            {
                EnsureSuccess(existing);
                using var document = JsonDocument.Parse(await existing.Content.ReadAsStringAsync(ct));
                var root = document.RootElement;
                if (root.TryGetProperty("status", out var status) && status.GetString() == "cancelled")
                {
                    if (item.Payload is null) return;
                    throw new GoogleCalendarEventDeletedException();
                }
                if (!IsOwned(root, item))
                    throw new GoogleCalendarRemoteException("Un evento de Google no pertenece a esta sincronización; no se modificó.");
                if (root.TryGetProperty("etag", out var tag)) etag = tag.GetString();
            }
            if (item.Payload is null)
            {
                if (absent) return;
                using var deleted = await SendAsync(HttpMethod.Delete, url, accessToken, null, ct, etag);
                if (deleted.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.Gone))
                    EnsureSuccess(deleted);
                return;
            }
            if (!absent)
            {
                using var updated = await SendAsync(HttpMethod.Put, url, accessToken, item.Payload, ct, etag);
                if (updated.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                    throw new GoogleCalendarEventDeletedException();
                EnsureSuccess(updated);
                return;
            }
            if (existing.StatusCode == HttpStatusCode.Gone)
                throw new GoogleCalendarEventDeletedException();

            var payload = JsonNode.Parse(item.Payload)!.AsObject();
            payload["id"] = item.RemoteId;
            using var created = await SendAsync(HttpMethod.Post, EventsUrl, accessToken, payload.ToJsonString(), ct);
            if (created.StatusCode == HttpStatusCode.Conflict && retryConflict)
            {
                await SynchronizeAsync(accessToken, item, ct, retryConflict: false);
                return;
            }
            EnsureSuccess(created);
        }

        public async Task<string> EnsureTaskListAsync(string accessToken, string? listId, CancellationToken ct)
        {
            if (listId is not null)
            {
                using var existing = await SendAsync(HttpMethod.Get, TaskListsUrl + "/" + Uri.EscapeDataString(listId), accessToken, null, ct);
                if (existing.IsSuccessStatusCode) return listId;
                if (existing.StatusCode != HttpStatusCode.NotFound) EnsureSuccess(existing);
            }
            string? pageToken = null;
            do
            {
                var url = QueryHelpers.AddQueryString(TaskListsUrl, new Dictionary<string, string?>
                {
                    ["maxResults"] = "100",
                    ["pageToken"] = pageToken
                });
                using var response = await SendAsync(HttpMethod.Get, url, accessToken, null, ct);
                EnsureSuccess(response);
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if (document.RootElement.TryGetProperty("items", out var lists))
                    foreach (var list in lists.EnumerateArray())
                        if (list.TryGetProperty("title", out var title) && title.GetString() == "PersonalFinance")
                            return RequiredId(list);
                pageToken = document.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
            } while (!string.IsNullOrEmpty(pageToken));
            using var created = await SendAsync(HttpMethod.Post, TaskListsUrl, accessToken,
                JsonSerializer.Serialize(new { title = "PersonalFinance" }), ct);
            EnsureSuccess(created);
            using var result = JsonDocument.Parse(await created.Content.ReadAsStringAsync(ct));
            return RequiredId(result.RootElement);
        }

        public async Task<string?> SynchronizeTaskAsync(
            string accessToken, string listId, GoogleSyncItem item, CancellationToken ct)
        {
            var collection = TasksUrl + Uri.EscapeDataString(listId) + "/tasks";
            var marker = "\n\n[PersonalFinance:" + item.Id.ToString("N") + "]";
            string? taskId = null;
            string? etag = null;
            if (item.RemoteId is not null)
            {
                using var existing = await SendAsync(HttpMethod.Get,
                    collection + "/" + Uri.EscapeDataString(item.RemoteId), accessToken, null, ct);
                if (existing.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.Gone))
                {
                    EnsureSuccess(existing);
                    using var document = JsonDocument.Parse(await existing.Content.ReadAsStringAsync(ct));
                    var root = document.RootElement;
                    var deleted = root.TryGetProperty("deleted", out var value) && value.GetBoolean();
                    if (!deleted)
                    {
                        if (!OwnsTask(root, marker))
                            throw new GoogleCalendarRemoteException("Una tarea de Google ya no tiene su referencia de sincronización; no se modificó.");
                        taskId = RequiredId(root);
                        etag = root.TryGetProperty("etag", out var tag) ? tag.GetString() : null;
                    }
                }
            }
            if (taskId is null)
            {
                string? pageToken = null;
                do
                {
                    var url = QueryHelpers.AddQueryString(collection, new Dictionary<string, string?>
                    {
                        ["maxResults"] = "100", ["showCompleted"] = "true", ["showHidden"] = "true",
                        ["showDeleted"] = "false", ["pageToken"] = pageToken
                    });
                    using var response = await SendAsync(HttpMethod.Get, url, accessToken, null, ct);
                    EnsureSuccess(response);
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                    if (document.RootElement.TryGetProperty("items", out var tasks))
                        foreach (var task in tasks.EnumerateArray())
                            if (OwnsTask(task, marker))
                            {
                                taskId = RequiredId(task);
                                etag = task.TryGetProperty("etag", out var tag) ? tag.GetString() : null;
                                break;
                            }
                    pageToken = document.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
                } while (taskId is null && !string.IsNullOrEmpty(pageToken));
            }
            if (item.Payload is null)
            {
                if (taskId is null) return null;
                using var deleted = await SendAsync(HttpMethod.Delete,
                    collection + "/" + Uri.EscapeDataString(taskId), accessToken, null, ct, etag);
                if (deleted.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.Gone)) EnsureSuccess(deleted);
                return null;
            }
            var payload = JsonNode.Parse(item.Payload)!.AsObject();
            payload["notes"] = (payload["notes"]?.GetValue<string>() ?? string.Empty) + marker;
            var target = taskId is null ? collection : collection + "/" + Uri.EscapeDataString(taskId);
            using var saved = await SendAsync(taskId is null ? HttpMethod.Post : HttpMethod.Patch,
                target, accessToken, payload.ToJsonString(), ct, etag);
            EnsureSuccess(saved);
            using var savedTask = JsonDocument.Parse(await saved.Content.ReadAsStringAsync(ct));
            return RequiredId(savedTask.RootElement);
        }

        private static bool OwnsTask(JsonElement task, string marker) =>
            task.TryGetProperty("notes", out var notes) && notes.ValueKind == JsonValueKind.String
            && notes.GetString()!.EndsWith(marker, StringComparison.Ordinal);

        private static string RequiredId(JsonElement resource) =>
            resource.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(id.GetString())
                ? id.GetString()!
                : throw new GoogleCalendarRemoteException("Google devolvió un identificador de sincronización no válido.");

        private static bool IsOwned(JsonElement root, GoogleSyncItem item) =>
            root.TryGetProperty("extendedProperties", out var properties)
            && properties.TryGetProperty("private", out var values)
            && values.TryGetProperty("app", out var app) && app.GetString() == "personalfinance"
            && values.TryGetProperty("user", out var user) && user.GetString() == item.UserId.ToString("N")
            && values.TryGetProperty("source", out var source) && source.GetString() == item.SourceKey;

        private async Task<HttpResponseMessage> SendAsync(
            HttpMethod method, string url, string accessToken, string? payload, CancellationToken ct, string? etag = null)
        {
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            if (etag is not null) request.Headers.TryAddWithoutValidation("If-Match", etag);
            if (payload is not null) request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            return await http.SendAsync(request, ct);
        }

        private static void EnsureSuccess(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode) return;
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new GoogleCalendarRemoteException("Google necesita que vuelvas a conectar tu cuenta.", true);
            throw new GoogleCalendarRemoteException(
                $"Google no pudo sincronizar el registro (HTTP {(int)response.StatusCode}). Se volverá a intentar.");
        }
    }
}
