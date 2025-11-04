using Microsoft.AspNetCore.Http;
using PersonalFinance.Api.Models.Dtos.Import;

namespace PersonalFinance.Api.Services.Contracts
{
    public interface IImportExcelService
    {
        byte[] GenerateTemplate();
        Task<ImportResult> ImportTemplateAsync(IFormFile file, Guid userId);
    }
}
