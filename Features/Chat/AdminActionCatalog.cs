using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing.Patterns;

namespace PersonalFinance.Api.Features.Chat
{
    public enum AdminRouteConstraint
    {
        None,
        Guid,
        Int
    }

    /// <summary>A route segment: either a literal or a parameter.</summary>
    public sealed record AdminRouteSegment(string? Literal, string? Parameter, AdminRouteConstraint Constraint);

    /// <summary>Compact schema of a body field, an array item or a query parameter.</summary>
    public sealed class AdminValueSchema
    {
        /// <summary>string, integer, number, boolean, date, guid, enum, object or array.</summary>
        public required string Kind { get; init; }
        public IReadOnlyList<string>? EnumValues { get; init; }
        /// <summary>Known fields of an object; null means a free-form object.</summary>
        public IReadOnlyList<AdminBodyField>? Fields { get; init; }
        public AdminValueSchema? Items { get; init; }
        public bool WideInteger { get; init; }
    }

    public sealed record AdminBodyField(string Name, AdminValueSchema Schema, bool Required);

    public sealed record AdminQueryParameter(string Name, AdminValueSchema Schema);

    public sealed class AdminEndpoint
    {
        public required string Method { get; init; }
        /// <summary>Route template without "api/" and without constraints, e.g. "Expense/{id}".</summary>
        public required string Template { get; init; }
        public required string Controller { get; init; }
        public required string Action { get; init; }
        public required IReadOnlyList<AdminRouteSegment> Segments { get; init; }
        public required IReadOnlyList<AdminQueryParameter> Query { get; init; }
        public AdminValueSchema? Body { get; init; }
        public bool BodyRequired { get; init; }
        public required string Label { get; init; }
        public bool Danger { get; init; }
    }

    /// <summary>A proposed call matched against the catalog, with its normalised path.</summary>
    public sealed record AdminCallMatch(
        AdminEndpoint Endpoint,
        IReadOnlyList<KeyValuePair<string, string>> RouteValues,
        IReadOnlyList<KeyValuePair<string, string>> Query,
        string Path)
    {
        public bool Danger => Endpoint.Danger || Query.Any(q =>
            string.Equals(q.Key, "force", StringComparison.OrdinalIgnoreCase) && q.Value == "true");

        public string? RouteValue(string parameter) => RouteValues
            .FirstOrDefault(v => string.Equals(v.Key, parameter, StringComparison.OrdinalIgnoreCase))
            .Value;
    }

    /// <summary>
    /// Write operations of the API (POST/PUT/PATCH/DELETE) that the global assistant may
    /// propose to admins as "api_call". Built lazily from ApiExplorer and cached. The
    /// frontend executes a confirmed call with the user's own token, so every endpoint keeps
    /// its own authorization; this catalog only limits what may be proposed and how.
    /// </summary>
    public sealed class AdminActionCatalog
    {
        public const int MaxBodyBytes = 8 * 1024;
        private const int MaxPathLength = 512;

        private static readonly string[] WriteMethods = { "POST", "PUT", "PATCH", "DELETE" };

        private static readonly HashSet<string> ExcludedControllers = new(StringComparer.OrdinalIgnoreCase)
        {
            "Auth",
            "Chat"
        };

        // AI conversations that write nothing, and Users/{id}, which only deletes the caller's own account.
        private static readonly HashSet<string> ExcludedEndpoints = new(StringComparer.OrdinalIgnoreCase)
        {
            "POST Dashboard/advice",
            "POST Dashboard/advice/reply",
            "POST imports/{batchId}/chat",
            "DELETE Users/{id}"
        };

        // Compared after lower-casing and removing '-' and '_'.
        private static readonly string[] SensitiveWords =
        {
            "password", "passwd", "token", "login", "logout", "signin", "signup",
            "register", "registration", "confirmemail", "emailconfirm", "secret"
        };

        private static readonly Dictionary<string, string> SpecialLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            ["POST Admin/users/{id}/approve"] = "Aprobar cuenta",
            ["POST Admin/users/{id}/roles"] = "Dar rol",
            ["DELETE Admin/users/{id}/roles/{roleName}"] = "Quitar rol",
            ["DELETE Admin/users/{id}"] = "Borrar usuario"
        };

        private static readonly Dictionary<string, string> ControllerNouns = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Expense"] = "gasto",
            ["Income"] = "ingreso",
            ["Category"] = "categoría",
            ["Budgets"] = "presupuesto",
            ["Commitments"] = "compromiso",
            ["Loans"] = "préstamo",
            ["Pasanaco"] = "pasanaco",
            ["Accounts"] = "cuenta",
            ["Banks"] = "banco",
            ["Savings"] = "ahorro",
            ["Transfers"] = "transferencia",
            ["Reminders"] = "recordatorio",
            ["LedgerEntries"] = "previsto",
            ["Settlements"] = "liquidación",
            ["Periods"] = "mes",
            ["Reconciliations"] = "conciliación",
            ["Payments"] = "pago",
            ["ChartOfAccounts"] = "plan de cuentas",
            ["Imports"] = "importación",
            ["Import"] = "importación",
            ["ExpensePlanning"] = "planificación",
            ["Users"] = "usuario",
            ["Admin"] = "usuario"
        };

        // Literal segments below the controller root that name a sub-resource.
        private static readonly Dictionary<string, string> SubResourceNouns = new(StringComparer.OrdinalIgnoreCase)
        {
            ["participants"] = "participante",
            ["lines"] = "línea",
            ["people"] = "persona",
            ["roles"] = "rol",
            ["payments"] = "pago",
            ["concepts"] = "concepto",
            ["rows"] = "fila importada",
            ["groups"] = "grupo importado",
            ["loan"] = "préstamo",
            ["users"] = "usuario",
            ["mappings"] = "reglas"
        };

        // Trailing literal segments that name an operation; "METHOD segment" keys win.
        private static readonly Dictionary<string, string> SubActionVerbs = new(StringComparer.OrdinalIgnoreCase)
        {
            ["complete"] = "Completar",
            ["reopen"] = "Reabrir",
            ["confirm"] = "Confirmar",
            ["unconfirm"] = "Desconfirmar",
            ["skip"] = "Saltar",
            ["unskip"] = "Recuperar",
            ["advance"] = "Avanzar ronda",
            ["retreat"] = "Retroceder ronda",
            ["generate-payments"] = "Generar pagos",
            ["mark-paid"] = "Marcar pagado",
            ["close"] = "Cerrar",
            ["mark"] = "Marcar conciliada",
            ["snooze"] = "Posponer",
            ["seed"] = "Generar",
            ["apply"] = "Aplicar",
            ["suggest"] = "Sugerir",
            ["sent"] = "Marcar enviada",
            ["from-entry"] = "Crear desde movimiento",
            ["assign-entry"] = "Asignar movimiento",
            ["plan"] = "Planificar",
            ["split"] = "Dividir",
            ["planned-entry"] = "Vincular previsto",
            ["approve"] = "Aprobar",
            ["PUT concept"] = "Elegir concepto",
            ["POST concept"] = "Crear concepto",
            ["PUT loan"] = "Vincular préstamo"
        };

        private static readonly Regex IntValue = new("^-?[0-9]{1,10}$", RegexOptions.CultureInvariant);
        private static readonly Regex PlainValue = new("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant);
        private static readonly Regex QueryName = new("^[A-Za-z0-9_]{1,64}$", RegexOptions.CultureInvariant);
        private static readonly Regex QueryValue = new("^[A-Za-z0-9_.:-]{1,64}$", RegexOptions.CultureInvariant);

        private readonly IApiDescriptionGroupCollectionProvider _apiDescriptions;
        private readonly ILogger<AdminActionCatalog> _logger;
        private readonly Lazy<IReadOnlyList<AdminEndpoint>> _endpoints;
        private readonly Lazy<string> _promptText;

        public AdminActionCatalog(
            IApiDescriptionGroupCollectionProvider apiDescriptions,
            ILogger<AdminActionCatalog> logger)
        {
            _apiDescriptions = apiDescriptions;
            _logger = logger;
            _endpoints = new Lazy<IReadOnlyList<AdminEndpoint>>(Build, LazyThreadSafetyMode.ExecutionAndPublication);
            _promptText = new Lazy<string>(() =>
            {
                var text = Render(Endpoints);
                _logger.LogInformation(
                    "Admin action catalog ready: {Count} endpoints, {Characters} characters.",
                    Endpoints.Count,
                    text.Length);
                return text;
            }, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public IReadOnlyList<AdminEndpoint> Endpoints => _endpoints.Value;

        /// <summary>One line per endpoint, for the admin section of the prompt.</summary>
        public string PromptText => _promptText.Value;

        /// <summary>
        /// Matches a proposed method + path (relative, optionally prefixed with "/api") with
        /// exactly one endpoint and returns the normalised path ("/" + filled template + query).
        /// </summary>
        public bool TryMatch(string? method, string? path, [NotNullWhen(true)] out AdminCallMatch? match)
        {
            match = null;
            var verb = method?.Trim().ToUpperInvariant();
            if (verb is null || Array.IndexOf(WriteMethods, verb) < 0) return false;
            if (!TrySplitPath(path, out var segments, out var queryText)) return false;

            AdminEndpoint? best = null;
            List<KeyValuePair<string, string>>? bestValues = null;
            var bestScore = -1;
            var ambiguous = false;
            foreach (var endpoint in Endpoints)
            {
                if (endpoint.Method != verb || endpoint.Segments.Count != segments.Length) continue;

                var values = new List<KeyValuePair<string, string>>();
                var score = 0;
                var matches = true;
                for (var i = 0; i < segments.Length && matches; i++)
                {
                    var segment = endpoint.Segments[i];
                    if (segment.Literal is not null)
                    {
                        matches = string.Equals(segment.Literal, segments[i], StringComparison.OrdinalIgnoreCase);
                        score++;
                    }
                    else if (TryNormalizeRouteValue(segments[i], segment.Constraint, out var value))
                    {
                        values.Add(new KeyValuePair<string, string>(segment.Parameter!, value));
                    }
                    else
                    {
                        matches = false;
                    }
                }

                if (!matches) continue;
                if (score > bestScore)
                {
                    best = endpoint;
                    bestValues = values;
                    bestScore = score;
                    ambiguous = false;
                }
                else if (score == bestScore)
                {
                    ambiguous = true;
                }
            }

            if (best is null || bestValues is null || ambiguous) return false;
            if (!TryParseQuery(best, queryText, out var query)) return false;

            var normalized = new StringBuilder();
            var valueIndex = 0;
            foreach (var segment in best.Segments)
            {
                normalized.Append('/').Append(segment.Literal ?? bestValues[valueIndex++].Value);
            }
            if (query.Count > 0)
            {
                normalized.Append('?').AppendJoin('&', query.Select(q => q.Key + "=" + q.Value));
            }

            match = new AdminCallMatch(best, bestValues, query, normalized.ToString());
            return true;
        }

        /// <summary>
        /// Validates a proposed body (a JSON object or null, at most <see cref="MaxBodyBytes"/>),
        /// keeps only the fields of the endpoint schema and restores masked texts with
        /// <paramref name="restore"/>. Endpoints without body always get null.
        /// </summary>
        public bool TryNormalizeBody(
            AdminEndpoint endpoint,
            JsonElement? body,
            Func<string, string> restore,
            out JsonElement? normalized)
        {
            normalized = null;
            if (endpoint.Body is null) return true;

            try
            {
                JsonObject? source = null;
                if (body is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } element)
                {
                    var raw = element.ValueKind == JsonValueKind.String
                        ? element.GetString() ?? string.Empty
                        : element.GetRawText();
                    if (Encoding.UTF8.GetByteCount(raw) > MaxBodyBytes) return false;
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        var node = JsonNode.Parse(raw);
                        if (node is JsonObject parsed) source = parsed;
                        else if (node is not null) return false;
                    }
                }

                if (source is null)
                {
                    if (!endpoint.BodyRequired) return true;
                    if (endpoint.Body.Fields?.Any(f => f.Required) ?? false) return false;
                    source = new JsonObject();
                }

                var filtered = Filter(source, endpoint.Body);
                RestoreStrings(filtered, restore);
                var text = filtered.ToJsonString();
                if (Encoding.UTF8.GetByteCount(text) > MaxBodyBytes) return false;

                using var document = JsonDocument.Parse(text);
                normalized = document.RootElement.Clone();
                return true;
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or FormatException)
            {
                normalized = null;
                return false;
            }
        }

        private IReadOnlyList<AdminEndpoint> Build()
        {
            var endpoints = new List<AdminEndpoint>();
            try
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var nullability = new NullabilityInfoContext();
                foreach (var group in _apiDescriptions.ApiDescriptionGroups.Items)
                {
                    foreach (var description in group.Items)
                    {
                        try
                        {
                            var endpoint = Describe(description, nullability);
                            if (endpoint is not null && seen.Add(endpoint.Method + " " + endpoint.Template))
                                endpoints.Add(endpoint);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex,
                                "Admin action catalog: skipped {Method} {Path}.",
                                description.HttpMethod,
                                description.RelativePath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The admin action catalog could not be built.");
                return Array.Empty<AdminEndpoint>();
            }

            return endpoints
                .OrderBy(e => e.Template, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => Array.IndexOf(WriteMethods, e.Method))
                .ToList();
        }

        private static AdminEndpoint? Describe(ApiDescription description, NullabilityInfoContext nullability)
        {
            var method = description.HttpMethod?.Trim().ToUpperInvariant();
            if (method is null || Array.IndexOf(WriteMethods, method) < 0) return null;
            if (description.ActionDescriptor is not ControllerActionDescriptor action) return null;
            if (ExcludedControllers.Contains(action.ControllerName) || AllowsAnonymous(action)) return null;

            // Uploads are not supported.
            var parameters = description.ParameterDescriptions;
            if (parameters.Any(p => p.Source == BindingSource.Form || p.Source == BindingSource.FormFile || IsFileType(p.Type)) ||
                action.Parameters.Any(p => IsFileType(p.ParameterType)))
                return null;

            var segments = ParseRoute(action.AttributeRouteInfo?.Template, description.RelativePath, action);
            if (segments is null) return null;
            var template = string.Join('/', segments.Select(s => s.Literal ?? "{" + s.Parameter + "}"));
            if (ExcludedEndpoints.Contains(method + " " + template)) return null;
            if (IsSensitive(template) || IsSensitive(action.ActionName)) return null;

            var query = new List<AdminQueryParameter>();
            foreach (var parameter in parameters.Where(p => p.Source == BindingSource.Query))
            {
                if (IsSensitive(parameter.Name)) return null;
                // The API always takes the user from the token.
                if (string.Equals(parameter.Name, "userId", StringComparison.OrdinalIgnoreCase)) continue;
                var schema = parameter.Type is null ? null : DescribeSimple(parameter.Type);
                if (schema is null ||
                    !QueryName.IsMatch(parameter.Name) ||
                    query.Any(q => string.Equals(q.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                query.Add(new AdminQueryParameter(parameter.Name, schema));
            }

            AdminValueSchema? body = null;
            var bodyRequired = false;
            var bodyParameter = parameters.FirstOrDefault(p => p.Source == BindingSource.Body);
            if (bodyParameter?.Type is not null)
            {
                body = DescribeValue(bodyParameter.Type, 0, nullability);
                if (body.Kind != "object" || HasSensitiveField(body)) return null;
                bodyRequired = IsBodyRequired(bodyParameter, action, nullability);
            }

            return new AdminEndpoint
            {
                Method = method,
                Template = template,
                Controller = action.ControllerName,
                Action = action.ActionName,
                Segments = segments,
                Query = query,
                Body = body,
                BodyRequired = bodyRequired,
                Label = BuildLabel(method, template, segments, action),
                Danger = method == "DELETE" || string.Equals(action.ControllerName, "Admin", StringComparison.OrdinalIgnoreCase)
            };
        }

        private static List<AdminRouteSegment>? ParseRoute(
            string? attributeTemplate,
            string? relativePath,
            ControllerActionDescriptor action)
        {
            var raw = attributeTemplate;
            if (string.IsNullOrWhiteSpace(raw) || raw.Contains('[')) raw = relativePath;
            if (string.IsNullOrWhiteSpace(raw)) return null;

            raw = raw.Trim();
            if (raw.StartsWith("~/", StringComparison.Ordinal)) raw = raw[2..];
            raw = raw.Trim('/');
            if (!raw.StartsWith("api/", StringComparison.OrdinalIgnoreCase)) return null;
            raw = raw[4..];

            RoutePattern pattern;
            try
            {
                pattern = RoutePatternFactory.Parse(raw);
            }
            catch (RoutePatternException)
            {
                return null;
            }

            var segments = new List<AdminRouteSegment>();
            foreach (var pathSegment in pattern.PathSegments)
            {
                if (pathSegment.Parts.Count != 1) return null;
                switch (pathSegment.Parts[0])
                {
                    case RoutePatternLiteralPart literal:
                        segments.Add(new AdminRouteSegment(literal.Content, null, AdminRouteConstraint.None));
                        break;
                    case RoutePatternParameterPart parameter
                        when !parameter.IsOptional && !parameter.IsCatchAll && parameter.Default is null:
                        segments.Add(new AdminRouteSegment(null, parameter.Name, ResolveConstraint(parameter, action)));
                        break;
                    default:
                        return null;
                }
            }

            return segments.Count == 0 ? null : segments;
        }

        private static AdminRouteConstraint ResolveConstraint(RoutePatternParameterPart parameter, ControllerActionDescriptor action)
        {
            foreach (var policy in parameter.ParameterPolicies)
            {
                var content = policy.Content?.Trim();
                if (string.Equals(content, "guid", StringComparison.OrdinalIgnoreCase)) return AdminRouteConstraint.Guid;
                if (string.Equals(content, "int", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(content, "long", StringComparison.OrdinalIgnoreCase))
                    return AdminRouteConstraint.Int;
            }

            var type = action.Parameters
                .FirstOrDefault(p => string.Equals(p.Name, parameter.Name, StringComparison.OrdinalIgnoreCase))
                ?.ParameterType;
            if (type is null) return AdminRouteConstraint.None;
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type == typeof(Guid)) return AdminRouteConstraint.Guid;
            if (type == typeof(int) || type == typeof(long) || type == typeof(short)) return AdminRouteConstraint.Int;
            return AdminRouteConstraint.None;
        }

        private static bool AllowsAnonymous(ControllerActionDescriptor action) =>
            (action.EndpointMetadata?.OfType<IAllowAnonymous>().Any() ?? false) ||
            action.MethodInfo.IsDefined(typeof(AllowAnonymousAttribute), true) ||
            action.ControllerTypeInfo.IsDefined(typeof(AllowAnonymousAttribute), true);

        private static bool IsFileType(Type? type)
        {
            if (type is null) return false;
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (typeof(IFormFile).IsAssignableFrom(type) || typeof(IFormFileCollection).IsAssignableFrom(type)) return true;
            var element = GetElementType(type);
            return element is not null && typeof(IFormFile).IsAssignableFrom(element);
        }

        private static bool IsSensitive(string? text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            var normalized = text.Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
            return SensitiveWords.Any(word => normalized.Contains(word, StringComparison.Ordinal));
        }

        private static bool HasSensitiveField(AdminValueSchema schema) =>
            (schema.Items is not null && HasSensitiveField(schema.Items)) ||
            (schema.Fields?.Any(f => IsSensitive(f.Name) || HasSensitiveField(f.Schema)) ?? false);

        private static bool IsBodyRequired(
            ApiParameterDescription parameter,
            ControllerActionDescriptor action,
            NullabilityInfoContext nullability)
        {
            if (parameter.Type is not null && Nullable.GetUnderlyingType(parameter.Type) is not null) return false;

            var info = (parameter.ParameterDescriptor as ControllerParameterDescriptor)?.ParameterInfo
                ?? action.Parameters
                    .OfType<ControllerParameterDescriptor>()
                    .FirstOrDefault(p => p.BindingInfo?.BindingSource == BindingSource.Body)
                    ?.ParameterInfo;
            if (info is null) return true;
            if (info.HasDefaultValue) return false;
            if (info.GetCustomAttribute<FromBodyAttribute>()?.EmptyBodyBehavior == EmptyBodyBehavior.Allow) return false;

            try
            {
                var state = nullability.Create(info);
                return state.ReadState != NullabilityState.Nullable && state.WriteState != NullabilityState.Nullable;
            }
            catch (Exception)
            {
                return true;
            }
        }

        // level 0 is the body itself; objects nested more than one level become free-form.
        private static AdminValueSchema DescribeValue(Type type, int level, NullabilityInfoContext nullability)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            var simple = DescribeSimple(type);
            if (simple is not null) return simple;
            if (IsFreeForm(type)) return new AdminValueSchema { Kind = "object" };

            var element = GetElementType(type);
            if (element is not null)
            {
                return new AdminValueSchema { Kind = "array", Items = DescribeValue(element, level, nullability) };
            }

            if (level > 1) return new AdminValueSchema { Kind = "object" };
            return new AdminValueSchema { Kind = "object", Fields = DescribeFields(type, level, nullability) };
        }

        private static IReadOnlyList<AdminBodyField> DescribeFields(Type type, int level, NullabilityInfoContext nullability)
        {
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0)
                .ToList();
            var propertyNames = new HashSet<string>(properties.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
            var constructorParameters = type.GetConstructors()
                .SelectMany(c => c.GetParameters())
                .Where(p => !string.IsNullOrEmpty(p.Name))
                .GroupBy(p => p.Name!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var fields = new List<AdminBodyField>();
            foreach (var property in properties)
            {
                constructorParameters.TryGetValue(property.Name, out var constructorParameter);
                if (property.GetMethod is not { IsPublic: true }) continue;
                if (property.SetMethod is not { IsPublic: true } && constructorParameter is null) continue;
                if (property.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always }) continue;

                var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                    ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
                // The API takes the owner from the token and the id from the route.
                if (string.Equals(name, "userId", StringComparison.OrdinalIgnoreCase)) continue;
                if (level == 0 && string.Equals(name, "id", StringComparison.OrdinalIgnoreCase)) continue;
                if (IsNavigation(property, propertyNames)) continue;
                if (fields.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))) continue;

                var schema = DescribeValue(property.PropertyType, level + 1, nullability);
                fields.Add(new AdminBodyField(name, schema, IsRequired(property, constructorParameter, schema, nullability)));
            }

            return fields;
        }

        // EF navigations (entity types, or an object next to its "{Name}Id" foreign key) are not sent.
        private static bool IsNavigation(PropertyInfo property, HashSet<string> propertyNames)
        {
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (DescribeSimple(type) is not null || IsFreeForm(type)) return false;
            var target = GetElementType(type) ?? type;
            target = Nullable.GetUnderlyingType(target) ?? target;
            if (DescribeSimple(target) is not null || IsFreeForm(target)) return false;

            return (target.Namespace?.EndsWith(".Entities", StringComparison.Ordinal) ?? false) ||
                propertyNames.Contains(property.Name + "Id");
        }

        private static bool IsRequired(
            PropertyInfo property,
            ParameterInfo? constructorParameter,
            AdminValueSchema schema,
            NullabilityInfoContext nullability)
        {
            if (property.IsDefined(typeof(RequiredAttribute), true) ||
                property.IsDefined(typeof(JsonRequiredAttribute), true) ||
                property.IsDefined(typeof(System.Runtime.CompilerServices.RequiredMemberAttribute), true) ||
                (constructorParameter?.IsDefined(typeof(RequiredAttribute), true) ?? false))
                return true;

            var type = property.PropertyType;
            if (type.IsValueType) return Nullable.GetUnderlyingType(type) is null;
            if (schema.Kind == "array" || (schema.Kind == "object" && schema.Fields is null)) return false;

            try
            {
                var info = nullability.Create(property);
                return info.WriteState == NullabilityState.NotNull ||
                    (info.WriteState == NullabilityState.Unknown && info.ReadState == NullabilityState.NotNull);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static AdminValueSchema? DescribeSimple(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type == typeof(string) || type == typeof(char) || type == typeof(TimeOnly) ||
                type == typeof(TimeSpan) || type == typeof(Uri))
                return new AdminValueSchema { Kind = "string" };
            if (type == typeof(bool)) return new AdminValueSchema { Kind = "boolean" };
            if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) ||
                type == typeof(ushort) || type == typeof(int))
                return new AdminValueSchema { Kind = "integer" };
            if (type == typeof(uint) || type == typeof(long) || type == typeof(ulong))
                return new AdminValueSchema { Kind = "integer", WideInteger = true };
            if (type == typeof(decimal) || type == typeof(double) || type == typeof(float))
                return new AdminValueSchema { Kind = "number" };
            if (type == typeof(Guid)) return new AdminValueSchema { Kind = "guid" };
            if (type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly))
                return new AdminValueSchema { Kind = "date" };
            if (type.IsEnum)
            {
                return new AdminValueSchema
                {
                    Kind = "enum",
                    EnumValues = Enum.GetNames(type).Select(JsonNamingPolicy.CamelCase.ConvertName).ToList()
                };
            }

            return null;
        }

        private static bool IsFreeForm(Type type) =>
            type == typeof(object) ||
            type == typeof(JsonElement) ||
            type == typeof(JsonDocument) ||
            typeof(JsonNode).IsAssignableFrom(type) ||
            typeof(IDictionary).IsAssignableFrom(type) ||
            (type.IsGenericType && IsDictionaryDefinition(type.GetGenericTypeDefinition())) ||
            type.GetInterfaces().Any(i => i.IsGenericType && IsDictionaryDefinition(i.GetGenericTypeDefinition()));

        private static bool IsDictionaryDefinition(Type definition) =>
            definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>);

        private static Type? GetElementType(Type type)
        {
            if (type == typeof(string)) return null;
            if (type.IsArray) return type.GetElementType();

            var enumerable = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                ? type
                : type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            if (enumerable is not null) return enumerable.GetGenericArguments()[0];
            return typeof(IEnumerable).IsAssignableFrom(type) ? typeof(object) : null;
        }

        private static string BuildLabel(
            string method,
            string template,
            IReadOnlyList<AdminRouteSegment> segments,
            ControllerActionDescriptor action)
        {
            if (SpecialLabels.TryGetValue(method + " " + template, out var special)) return special;

            var verb = method switch
            {
                "POST" => "Crear",
                "DELETE" => "Borrar",
                _ => "Editar"
            };
            var controllerNoun = ControllerNouns.TryGetValue(action.ControllerName, out var known) ? known : action.ActionName;
            var last = segments[^1];

            if (last.Literal is not null && segments.Count > 1)
            {
                if (SubActionVerbs.TryGetValue(method + " " + last.Literal, out var subVerb) ||
                    SubActionVerbs.TryGetValue(last.Literal, out subVerb))
                    return Compose(subVerb, NearestSubResourceNoun(segments, segments.Count - 2) ?? controllerNoun);
                if (SubResourceNouns.TryGetValue(last.Literal, out var resourceNoun))
                    return Compose(verb, resourceNoun);
                return Compose(action.ActionName, controllerNoun);
            }

            if (last.Parameter is not null && segments.Count > 2 &&
                segments[^2].Literal is { } parent && SubResourceNouns.TryGetValue(parent, out var parentNoun))
                return Compose(verb, parentNoun);

            return Compose(verb, controllerNoun);
        }

        private static string? NearestSubResourceNoun(IReadOnlyList<AdminRouteSegment> segments, int from)
        {
            for (var i = from; i >= 1; i--)
            {
                if (segments[i].Literal is { } literal && SubResourceNouns.TryGetValue(literal, out var noun)) return noun;
            }

            return null;
        }

        private static string Compose(string verb, string noun) =>
            noun.Length == 0 ? verb : verb + " · " + char.ToUpperInvariant(noun[0]) + noun[1..];

        private static string Render(IReadOnlyList<AdminEndpoint> endpoints)
        {
            var text = new StringBuilder();
            var bodies = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var endpoint in endpoints)
            {
                var path = RenderPath(endpoint);
                if (text.Length > 0) text.Append('\n');
                text.Append(endpoint.Method).Append(' ').Append(path);
                if (endpoint.Query.Count > 0)
                {
                    text.Append('?').AppendJoin('&', endpoint.Query.Select(q => q.Name + ":" + RenderSchema(q.Schema)));
                }
                text.Append(" [").Append(endpoint.Label).Append(endpoint.Danger ? " ⚠]" : "]");

                if (endpoint.Body is null) continue;
                var body = RenderSchema(endpoint.Body);
                text.Append(endpoint.BodyRequired ? " body" : " body?");
                if (bodies.TryGetValue(body, out var first))
                {
                    text.Append('=').Append(first);
                }
                else
                {
                    bodies[body] = endpoint.Method + " " + path;
                    text.Append(body);
                }
            }

            return text.ToString();
        }

        private static string RenderPath(AdminEndpoint endpoint) =>
            "/" + string.Join('/', endpoint.Segments.Select(s => s.Literal ?? "{" + s.Parameter + (s.Constraint switch
            {
                AdminRouteConstraint.Guid => ":guid",
                AdminRouteConstraint.Int => ":int",
                _ => string.Empty
            }) + "}"));

        private static string RenderSchema(AdminValueSchema schema) => schema.Kind switch
        {
            "enum" => string.Join('|', schema.EnumValues ?? Array.Empty<string>()),
            "array" => "[" + (schema.Items is null ? "object" : RenderSchema(schema.Items)) + "]",
            "object" when schema.Fields is not null => "{" + string.Join(',', schema.Fields.Select(f =>
                f.Name + (f.Required ? "*" : string.Empty) + ":" + RenderSchema(f.Schema))) + "}",
            _ => schema.Kind
        };

        private static bool TrySplitPath(string? path, out string[] segments, out string? query)
        {
            segments = Array.Empty<string>();
            query = null;
            if (string.IsNullOrWhiteSpace(path)) return false;

            var text = path.Trim();
            if (text.Length > MaxPathLength ||
                text.StartsWith("//", StringComparison.Ordinal) ||
                text.Contains("://", StringComparison.Ordinal) ||
                text.Contains("..", StringComparison.Ordinal) ||
                text.Contains('\\') ||
                text.Contains('%') ||
                text.Contains('#') ||
                text.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
                return false;

            if (!text.StartsWith('/')) text = "/" + text;
            if (text.StartsWith("/api", StringComparison.OrdinalIgnoreCase) && (text.Length == 4 || text[4] is '/' or '?'))
                text = text[4..];

            var queryStart = text.IndexOf('?');
            if (queryStart >= 0)
            {
                query = text[(queryStart + 1)..];
                text = text[..queryStart];
                if (query.Contains('?')) return false;
            }

            if (text.EndsWith('/')) text = text[..^1];
            if (text.StartsWith('/')) text = text[1..];
            if (text.Length == 0) return false;

            segments = text.Split('/');
            return segments.All(s => s.Length > 0);
        }

        private static bool TryNormalizeRouteValue(string raw, AdminRouteConstraint constraint, out string value)
        {
            value = string.Empty;
            switch (constraint)
            {
                case AdminRouteConstraint.Guid:
                    if (!Guid.TryParse(raw, out var guid)) return false;
                    value = guid.ToString("D");
                    return true;
                case AdminRouteConstraint.Int:
                    if (!IntValue.IsMatch(raw) ||
                        !int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
                        return false;
                    value = number.ToString(CultureInfo.InvariantCulture);
                    return true;
                default:
                    if (!PlainValue.IsMatch(raw)) return false;
                    value = raw;
                    return true;
            }
        }

        private static bool TryParseQuery(
            AdminEndpoint endpoint,
            string? text,
            out List<KeyValuePair<string, string>> query)
        {
            query = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrEmpty(text)) return true;

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in text.Split('&'))
            {
                if (pair.Length == 0) continue;
                var separator = pair.IndexOf('=');
                if (separator <= 0) return false;

                var name = pair[..separator];
                var raw = pair[(separator + 1)..];
                var parameter = endpoint.Query.FirstOrDefault(q => string.Equals(q.Name, name, StringComparison.OrdinalIgnoreCase));
                if (parameter is null || values.ContainsKey(parameter.Name)) return false;
                if (!QueryValue.IsMatch(raw) || !TryNormalizeQueryValue(raw, parameter.Schema, out var value)) return false;
                values[parameter.Name] = value;
            }

            foreach (var parameter in endpoint.Query)
            {
                if (values.TryGetValue(parameter.Name, out var value))
                    query.Add(new KeyValuePair<string, string>(parameter.Name, value));
            }

            return true;
        }

        private static bool TryNormalizeQueryValue(string raw, AdminValueSchema schema, out string value)
        {
            value = string.Empty;
            switch (schema.Kind)
            {
                case "boolean":
                    if (!bool.TryParse(raw, out var flag)) return false;
                    value = flag ? "true" : "false";
                    return true;
                case "integer" when schema.WideInteger:
                    if (!long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var wide)) return false;
                    value = wide.ToString(CultureInfo.InvariantCulture);
                    return true;
                case "integer":
                    if (!int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer)) return false;
                    value = integer.ToString(CultureInfo.InvariantCulture);
                    return true;
                case "number":
                    if (!decimal.TryParse(raw, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                            CultureInfo.InvariantCulture, out var amount))
                        return false;
                    value = amount.ToString(CultureInfo.InvariantCulture);
                    return true;
                case "guid":
                    if (!Guid.TryParse(raw, out var guid)) return false;
                    value = guid.ToString("D");
                    return true;
                case "date":
                    if (DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                    {
                        value = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                        return true;
                    }
                    if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var moment))
                        return false;
                    value = moment.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
                    return true;
                case "enum":
                    var known = schema.EnumValues?.FirstOrDefault(v => string.Equals(v, raw, StringComparison.OrdinalIgnoreCase));
                    if (known is null) return false;
                    value = known;
                    return true;
                case "string":
                    value = raw;
                    return true;
                default:
                    return false;
            }
        }

        private static JsonObject Filter(JsonObject source, AdminValueSchema schema)
        {
            if (schema.Fields is null) return (JsonObject)source.DeepClone();

            var result = new JsonObject();
            foreach (var (key, value) in source)
            {
                var field = schema.Fields.FirstOrDefault(f => string.Equals(f.Name, key, StringComparison.OrdinalIgnoreCase));
                if (field is null || result.ContainsKey(field.Name)) continue;

                result[field.Name] = value switch
                {
                    JsonObject nested when field.Schema.Kind == "object" => Filter(nested, field.Schema),
                    JsonArray array when field.Schema.Items is { Kind: "object", Fields: not null } items =>
                        new JsonArray(array.Select(item => item is JsonObject itemObject ? Filter(itemObject, items) : item?.DeepClone()).ToArray()),
                    _ => value?.DeepClone()
                };
            }

            return result;
        }

        private static void RestoreStrings(JsonNode? node, Func<string, string> restore)
        {
            switch (node)
            {
                case JsonObject jsonObject:
                    foreach (var key in jsonObject.Select(p => p.Key).ToList())
                    {
                        if (TryRestore(jsonObject[key], restore, out var replacement)) jsonObject[key] = replacement;
                        else RestoreStrings(jsonObject[key], restore);
                    }
                    break;
                case JsonArray jsonArray:
                    for (var i = 0; i < jsonArray.Count; i++)
                    {
                        if (TryRestore(jsonArray[i], restore, out var replacement)) jsonArray[i] = replacement;
                        else RestoreStrings(jsonArray[i], restore);
                    }
                    break;
            }
        }

        private static bool TryRestore(JsonNode? node, Func<string, string> restore, [NotNullWhen(true)] out JsonNode? replacement)
        {
            replacement = null;
            if (node is not JsonValue value || value.GetValueKind() != JsonValueKind.String) return false;

            var text = value.GetValue<string>();
            var restored = restore(text);
            if (string.Equals(restored, text, StringComparison.Ordinal)) return false;

            replacement = JsonValue.Create(restored)!;
            return true;
        }
    }
}
