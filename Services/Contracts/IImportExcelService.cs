using Microsoft.AspNetCore.Http;
using PersonalFinance.Api.Models.Dtos.Import;

namespace PersonalFinance.Api.Services.Contracts
{
    public interface IImportExcelService
    {
        byte[] GenerateTemplate(Guid userId);
        Task<ImportResult> ImportTemplateAsync(IFormFile file, Guid userId);
    }
}
