using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Extensions;
using PersonalFinance.Api.Services.Contracts;

namespace PersonalFinance.Api.Controllers
{
    [ApiController]
    [Route("api/template")]
    [Authorize]
    public class ImportController : ControllerBase
    {
        private readonly IImportExcelService _excelService;

        public ImportController(IImportExcelService excelService)
        {
            _excelService = excelService;
        }

        [HttpGet("export")]
        public IActionResult ExportTemplate()
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            // mode es opcional, solo para generar un ejemplo inicial distinto
            var file = _excelService.GenerateTemplate(userId.Value);
            return File(file,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "template.xlsx");
        }

        // El userId de la query se mantiene por compatibilidad, pero se ignora: siempre se usa el del token.
        [HttpPost("import")]
        public async Task<IActionResult> ImportTemplate(IFormFile file, [FromQuery] Guid? userId)
        {
            var currentUserId = User.GetUserId();
            if (currentUserId == null) return Unauthorized();

            if (file == null || file.Length == 0)
                return BadRequest("Archivo no válido");

            var extension = Path.GetExtension(file.FileName);
            if (!new[] { ".xlsx", ".csv" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
                return BadRequest("Formato no admitido. Usa .xlsx o .csv.");

            try
            {
                var result = await _excelService.ImportTemplateAsync(file, currentUserId.Value);
                return Ok(result);
            }
            catch (InvalidDataException exception)
            {
                return BadRequest(new { message = exception.Message });
            }
        }
    }
}
