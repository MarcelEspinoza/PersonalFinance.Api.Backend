using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models.Dtos.Import;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Services.Contracts;
using Microsoft.AspNetCore.Http;

namespace PersonalFinance.Api.Services
{
    public class ImportExcelService : IImportExcelService
    {
        private readonly AppDbContext _context;

        public ImportExcelService(AppDbContext context)
        {
            _context = context;
        }

        private static string CleanString(string? input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var sb = new StringBuilder(input.Length);
            foreach (var c in input)
            {
                if (c == 9 || c == 10 || c == 13 || c >= 0x20)
                    sb.Append(c);
                else
                    sb.Append(' ');
            }
            return sb.ToString().Trim();
        }

        public byte[] GenerateTemplate()
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Template");

            var headers = new[]
            {
                "Description", "Amount", "Date", "Category", "Notes",
                "Type", "Movement Type", "Bank (Origin)", "Is Transfer",
                "Bank (Destination)", "Transfer Reference", "Loan"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Fill.BackgroundColor = XLColor.LightGreen;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            }

            ws.Cell("A2").Value = "";

            // CATEGORÍAS
            var wsCategories = workbook.Worksheets.Add("Categories_aux");
            wsCategories.Cell(1, 1).Value = "Id";
            wsCategories.Cell(1, 2).Value = "Name";
            wsCategories.Row(1).Style.Font.Bold = true;
            wsCategories.Row(1).Style.Fill.BackgroundColor = XLColor.LightGray;

            var categories = _context.Categories.AsNoTracking().ToList();
            for (int i = 0; i < categories.Count; i++)
            {
                wsCategories.Cell(i + 2, 1).Value = categories[i].Id;
                wsCategories.Cell(i + 2, 2).Value = CleanString(categories[i].Name);
            }

            // TIPOS
            var wsTypes = workbook.Worksheets.Add("Types_aux");
            wsTypes.Cell(1, 1).Value = "Type";
            wsTypes.Row(1).Style.Font.Bold = true;
            wsTypes.Row(1).Style.Fill.BackgroundColor = XLColor.LightGray;
            wsTypes.Cell(2, 1).Value = "Fixed";
            wsTypes.Cell(3, 1).Value = "Variable";
            wsTypes.Cell(4, 1).Value = "Temporary";

            // MOVEMENTS
            var wsMovements = workbook.Worksheets.Add("Movements_aux");
            wsMovements.Cell(1, 1).Value = "Movement Type";
            wsMovements.Row(1).Style.Font.Bold = true;
            wsMovements.Row(1).Style.Fill.BackgroundColor = XLColor.LightGray;
            wsMovements.Cell(2, 1).Value = "Income";
            wsMovements.Cell(3, 1).Value = "Expense";

            // BANCOS
            var wsBanks = workbook.Worksheets.Add("Banks_aux");
            wsBanks.Cell(1, 1).Value = "Id";
            wsBanks.Cell(1, 2).Value = "Name";
            wsBanks.Row(1).Style.Font.Bold = true;
            wsBanks.Row(1).Style.Fill.BackgroundColor = XLColor.LightGray;

            var banks = _context.Banks.AsNoTracking().ToList();
            for (int i = 0; i < banks.Count; i++)
            {
                wsBanks.Cell(i + 2, 1).Value = banks[i].Id.ToString();
                wsBanks.Cell(i + 2, 2).Value = CleanString(banks[i].Name +
                    (string.IsNullOrEmpty(banks[i].Entity) ? "" : $" | {banks[i].Entity}"));
            }

            // PRÉSTAMOS
            var wsLoans = workbook.Worksheets.Add("Loans_aux");
            wsLoans.Cell(1, 1).Value = "Id";
            wsLoans.Cell(1, 2).Value = "Name";
            wsLoans.Row(1).Style.Font.Bold = true;
            wsLoans.Row(1).Style.Fill.BackgroundColor = XLColor.LightGray;

            var loans = _context.Set<Loan>().AsNoTracking().ToList();
            for (int i = 0; i < loans.Count; i++)
            {
                wsLoans.Cell(i + 2, 1).Value = loans[i].Id.ToString();
                wsLoans.Cell(i + 2, 2).Value = CleanString(loans[i].Name);
            }

            ws.Columns().AdjustToContents();
            ws.SheetView.FreezeRows(1);

            // VALIDACIONES
            var typesRange = wsTypes.Range("$A$2:$A$4");
            var movementsRange = wsMovements.Range("$A$2:$A$3");
            var categoriesRange = wsCategories.Range($"$B$2:$B${Math.Max(2, categories.Count + 1)}");
            var banksRange = wsBanks.Range($"$B$2:$B${Math.Max(2, banks.Count + 1)}");
            var loansRange = wsLoans.Range($"$B$2:$B${Math.Max(2, loans.Count + 1)}");

            ws.Range("D2:D100").CreateDataValidation().List($"='{wsCategories.Name}'!{categoriesRange.RangeAddress.FirstAddress}:{categoriesRange.RangeAddress.LastAddress}");
            ws.Range("F2:F100").CreateDataValidation().List($"='{wsTypes.Name}'!{typesRange.RangeAddress.FirstAddress}:{typesRange.RangeAddress.LastAddress}");
            ws.Range("G2:G100").CreateDataValidation().List($"='{wsMovements.Name}'!{movementsRange.RangeAddress.FirstAddress}:{movementsRange.RangeAddress.LastAddress}");
            ws.Range("H2:H100").CreateDataValidation().List($"='{wsBanks.Name}'!{banksRange.RangeAddress.FirstAddress}:{banksRange.RangeAddress.LastAddress}");
            ws.Range("J2:J100").CreateDataValidation().List($"='{wsBanks.Name}'!{banksRange.RangeAddress.FirstAddress}:{banksRange.RangeAddress.LastAddress}");
            ws.Range("L2:L100").CreateDataValidation().List($"='{wsLoans.Name}'!{loansRange.RangeAddress.FirstAddress}:{loansRange.RangeAddress.LastAddress}");

            using var ms = new MemoryStream();
            workbook.SaveAs(ms);
            ms.Position = 0;
            return ms.ToArray();
        }

        public async Task<ImportResult> ImportTemplateAsync(IFormFile file, Guid userId)
        {
            var result = new ImportResult();

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            using var workbook = new XLWorkbook(stream);
            var ws = workbook.Worksheet("Template");
            var rows = ws.RangeUsed()?.RowsUsed().Skip(1) ?? Enumerable.Empty<IXLRangeRow>();

            var validTypes = new[] { "Fixed", "Variable", "Temporary" };
            var validMovements = new[] { "Income", "Expense" };

            var categoriesList = await _context.Categories
                .Where(c => c.IsSystem || c.UserId == userId)
                .ToListAsync();

            var categories = categoriesList
                .GroupBy(c => CleanString(c.Name), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

            var banksList = await _context.Banks.ToListAsync();
            var banksDict = banksList
                .GroupBy(b => CleanString(b.Name + (string.IsNullOrEmpty(b.Entity) ? "" : $" | {b.Entity}")), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

            var loansList = await _context.Set<Loan>()
                .Where(l => l.UserId == userId)
                .ToListAsync();

            var loansDict = loansList
                .GroupBy(l => CleanString(l.Name), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                try
                {
                    var description = CleanString(row.Cell(1).GetString());
                    var amountStr = row.Cell(2).GetString();
                    var categoryName = CleanString(row.Cell(4).GetString());
                    var notes = CleanString(row.Cell(5).GetString());
                    var type = CleanString(row.Cell(6).GetString());
                    var movementType = CleanString(row.Cell(7).GetString());
                    var bankOriginName = CleanString(row.Cell(8).GetString());
                    var isTransferStr = CleanString(row.Cell(9).GetString());
                    var bankDestinationName = CleanString(row.Cell(10).GetString());
                    var transferReference = CleanString(row.Cell(11).GetString());
                    var loanName = CleanString(row.Cell(12).GetString());

                    var isTransfer = false;
                    if (!string.IsNullOrEmpty(isTransferStr))
                        bool.TryParse(isTransferStr, out isTransfer);

                    var errors = new List<string>();

                    // ✅ Importe
                    var amountOk = double.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount)
                                   || double.TryParse(amountStr, NumberStyles.Any, CultureInfo.CurrentCulture, out amount);

                    amount = Math.Abs(amount);

                    // ✅ Fecha sin UTC
                    DateTime dateLocal = DateTime.MinValue;
                    bool dateOk = false;
                    try
                    {
                        if (row.Cell(3).DataType == XLDataType.DateTime)
                        {
                            dateLocal = row.Cell(3).GetDateTime().Date;
                            dateOk = true;
                        }
                        else
                        {
                            var dateStr = row.Cell(3).GetString();
                            dateOk = DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate)
                                     || DateTime.TryParse(dateStr, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsedDate);
                            if (dateOk)
                                dateLocal = parsedDate.Date;
                        }
                    }
                    catch
                    {
                        errors.Add("Invalid date format");
                    }

                    // ✅ Validaciones básicas
                    if (string.IsNullOrWhiteSpace(description)) errors.Add("Empty description");
                    if (!amountOk) errors.Add("Invalid amount");
                    if (!dateOk) errors.Add("Invalid date");
                    if (!validTypes.Contains(type, StringComparer.OrdinalIgnoreCase)) errors.Add("Invalid type");
                    if (!validMovements.Contains(movementType, StringComparer.OrdinalIgnoreCase)) errors.Add("Invalid movement type");
                    if (string.IsNullOrWhiteSpace(bankOriginName) || !banksDict.ContainsKey(bankOriginName)) errors.Add("Invalid or empty Bank (Origin)");

                    if (isTransfer && string.IsNullOrWhiteSpace(transferReference))
                        errors.Add("Transfer Reference is required");

                    if (errors.Any())
                    {
                        result.Pending.Add(new { description, amount, date = dateLocal, errors });
                        continue;
                    }

                    var bankOriginId = banksDict[bankOriginName];
                    int? categoryId = null;
                    Guid? loanId = null;

                    // ✅ Categoría
                    if (!categories.ContainsKey(categoryName))
                    {
                        var newCat = new Category
                        {
                            Name = categoryName,
                            Description = "Categoría creada automáticamente por importación",
                            UserId = userId,
                            IsActive = true,
                            IsSystem = false,
                            CreatedAt = DateTime.UtcNow
                        };
                        _context.Categories.Add(newCat);
                        await _context.SaveChangesAsync();
                        categoryId = newCat.Id;
                        categories[categoryName] = newCat.Id;
                    }
                    else
                        categoryId = categories[categoryName];

                    if (!string.IsNullOrWhiteSpace(loanName) && loansDict.ContainsKey(loanName))
                        loanId = loansDict[loanName];

                    // ✅ Fechas según el tipo
                    DateTime startDate = dateLocal;
                    DateTime? endDate = null;
                    bool isIndefinite = false;

                    switch (type.ToLowerInvariant())
                    {
                        case "temporary":
                            startDate = dateLocal;
                            endDate = dateLocal;
                            isIndefinite = false;
                            break;

                        case "variable":
                            startDate = dateLocal;
                            endDate = dateLocal.AddDays(1);
                            isIndefinite = false;
                            break;

                        case "fixed":
                            startDate = dateLocal;
                            endDate = null;
                            isIndefinite = true;
                            break;
                    }

                    // ✅ Crear registro
                    if (movementType.Equals("Income", StringComparison.OrdinalIgnoreCase))
                    {
                        _context.Incomes.Add(new Income
                        {
                            Description = description,
                            Amount = (decimal)amount,
                            Date = dateLocal,
                            CategoryId = categoryId!.Value,
                            Notes = notes,
                            Type = type,
                            UserId = userId,
                            BankId = bankOriginId,
                            IsTransfer = isTransfer,
                            TransferCounterpartyBankId = !string.IsNullOrWhiteSpace(bankDestinationName) && banksDict.ContainsKey(bankDestinationName)
                                ? banksDict[bankDestinationName]
                                : null,
                            TransferReference = string.IsNullOrWhiteSpace(transferReference) ? null : transferReference,
                            LoanId = loanId,
                            Start_Date = startDate,
                            End_Date = endDate,
                            IsIndefinite = isIndefinite
                        });
                    }
                    else
                    {
                        _context.Expenses.Add(new Expense
                        {
                            Description = description,
                            Amount = (decimal)amount,
                            Date = dateLocal,
                            CategoryId = categoryId!.Value,
                            Notes = notes,
                            Type = type,
                            UserId = userId,
                            BankId = bankOriginId,
                            IsTransfer = isTransfer,
                            TransferCounterpartyBankId = !string.IsNullOrWhiteSpace(bankDestinationName) && banksDict.ContainsKey(bankDestinationName)
                                ? banksDict[bankDestinationName]
                                : null,
                            TransferReference = string.IsNullOrWhiteSpace(transferReference) ? null : transferReference,
                            LoanId = loanId,
                            Start_Date = startDate,
                            End_Date = endDate,
                            IsIndefinite = isIndefinite
                        });
                    }

                    result.Imported.Add(new { description, amount, type, dateLocal, startDate, endDate, isIndefinite });
                }
                catch (Exception ex)
                {
                    result.Pending.Add(new { error = ex.Message });
                }
            }

            await _context.SaveChangesAsync();
            return result;
        }

    }
}
