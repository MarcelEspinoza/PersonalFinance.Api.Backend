using System.Text.Json;
using PersonalFinance.Api.Common.Exceptions;

namespace PersonalFinance.Api.Common.Middleware
{
    /// <summary>
    /// Traduce las excepciones de negocio a códigos HTTP, para que los
    /// controladores no tengan que envolver cada llamada en try/catch.
    /// </summary>
    public class ApiExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ApiExceptionMiddleware> _logger;

        public ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (NotFoundException ex)
            {
                await WriteAsync(context, StatusCodes.Status404NotFound, ex.Message);
            }
            catch (BusinessRuleException ex)
            {
                await WriteAsync(context, StatusCodes.Status400BadRequest, ex.Message);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                await WriteAsync(context, StatusCodes.Status400BadRequest, ex.Message);
            }
            catch (Exception ex)
            {
                // El detalle se queda en el log: al cliente sólo le llega un
                // mensaje genérico para no filtrar la estructura interna.
                _logger.LogError(ex, "Error no controlado en {Method} {Path}",
                    context.Request.Method, context.Request.Path);

                await WriteAsync(context, StatusCodes.Status500InternalServerError,
                    "Se ha producido un error inesperado.");
            }
        }

        private static async Task WriteAsync(HttpContext context, int statusCode, string message)
        {
            if (context.Response.HasStarted) return;

            context.Response.Clear();
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsync(JsonSerializer.Serialize(new { message }));
        }
    }
}
