using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Services.Contracts;

namespace PersonalFinance.Api.Controllers
{
    [ApiController]
    [Route("api/template")]
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
            // mode es opcional, solo para generar un ejemplo inicial distinto
            var file = _excelService.GenerateTemplate();
            return File(file,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "template.xlsx");
        }

        [HttpPost("import")]
        public async Task<IActionResult> ImportTemplate(IFormFile file, [FromQuery] Guid userId)
        {
            if (file == null || file.Length == 0)
                return BadRequest("Archivo no válido");

            var result = await _excelService.ImportTemplateAsync(file, userId);
            return Ok(result);
        }
    }
}
