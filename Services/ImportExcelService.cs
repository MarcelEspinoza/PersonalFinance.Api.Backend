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

            // NUEVOS HEADERS EN INGLÉS
            var headers = new[]
            {
        "Description", "Amount", "Date", "Category", "Notes",
        "Type", "Movement Type", "Bank (Origin)", "Is Transfer",
        "Bank (Destination)", "Transfer Reference"
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

            // Celda inicial vacía
            ws.Cell("A2").Value = "";

            // HOJA CATEGORÍAS
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

            // HOJA TIPOS
            var wsTypes = workbook.Worksheets.Add("Types_aux");
            wsTypes.Cell(1, 1).Value = "Type";
            wsTypes.Row(1).Style.Font.Bold = true;
            wsTypes.Row(1).Style.Fill.BackgroundColor = XLColor.LightGray;
            wsTypes.Cell(2, 1).Value = "Fixed";
            wsTypes.Cell(3, 1).Value = "Variable";
            wsTypes.Cell(4, 1).Value = "Temporary";

            // HOJA MOVEMENTS
            var wsMovements = workbook.Worksheets.Add("Movements_aux");
            wsMovements.Cell(1, 1).Value = "Movement Type";
            wsMovements.Row(1).Style.Font.Bold = true;
            wsMovements.Row(1).Style.Fill.BackgroundColor = XLColor.LightGray;
            wsMovements.Cell(2, 1).Value = "Income";
            wsMovements.Cell(3, 1).Value = "Expense";

            // HOJA BANCOS
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

            // Ajustar anchos y congelar headers
            ws.Columns().AdjustToContents();
            ws.SheetView.FreezeRows(1);

            // DEFINIR RANGOS PARA VALIDACIONES
            var typesRange = wsTypes.Range("$A$2:$A$4");
            var movementsRange = wsMovements.Range("$A$2:$A$3");
            var categoriesRange = wsCategories.Range($"$B$2:$B${Math.Max(2, categories.Count + 1)}");
            var banksRange = wsBanks.Range($"$B$2:$B${Math.Max(2, banks.Count + 1)}");

            // CREAR DESPLEGABLES EN HOJA PRINCIPAL
            ws.Range("D2:D100").CreateDataValidation()
                .List($"='{wsCategories.Name}'!{categoriesRange.RangeAddress.FirstAddress}:{categoriesRange.RangeAddress.LastAddress}");
            ws.Range("F2:F100").CreateDataValidation()
                .List($"='{wsTypes.Name}'!{typesRange.RangeAddress.FirstAddress}:{typesRange.RangeAddress.LastAddress}");
            ws.Range("G2:G100").CreateDataValidation()
                .List($"='{wsMovements.Name}'!{movementsRange.RangeAddress.FirstAddress}:{movementsRange.RangeAddress.LastAddress}");
            ws.Range("H2:H100").CreateDataValidation()
                .List($"='{wsBanks.Name}'!{banksRange.RangeAddress.FirstAddress}:{banksRange.RangeAddress.LastAddress}");
            ws.Range("J2:J100").CreateDataValidation()
                .List($"='{wsBanks.Name}'!{banksRange.RangeAddress.FirstAddress}:{banksRange.RangeAddress.LastAddress}");

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

            var rows = ws.RangeUsed() != null
                ? ws.RangeUsed().RowsUsed().Skip(1).Cast<IXLRow>()
                : Enumerable.Empty<IXLRow>();

            var validTypes = new[] { "Fixed", "Variable", "Temporary" };
            var validMovements = new[] { "Income", "Expense" };

            var categoriesList = await _context.Categories
                .Where(c => c.IsSystem || c.UserId == userId)
                .ToListAsync();

            var categories = categoriesList
                .ToDictionary(c => CleanString(c.Name), c => c.Id, StringComparer.OrdinalIgnoreCase);

            var banksList = await _context.Banks.ToListAsync();
            var banksDict = banksList.ToDictionary(b => CleanString(b.Name + (string.IsNullOrEmpty(b.Entity) ? "" : $" | {b.Entity}")), b => b.Id, StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                try
                {
                    var description = CleanString(row.Cell(1).GetString());
                    var amountStr = row.Cell(2).GetString();
                    var dateStr = row.Cell(3).GetString();
                    var categoryName = CleanString(row.Cell(4).GetString());
                    var notes = CleanString(row.Cell(5).GetString());
                    var type = CleanString(row.Cell(6).GetString());
                    var movementType = CleanString(row.Cell(7).GetString());
                    var bankOriginName = CleanString(row.Cell(8).GetString());
                    var isTransferStr = CleanString(row.Cell(9).GetString());
                    var bankDestinationName = CleanString(row.Cell(10).GetString());
                    var transferReference = CleanString(row.Cell(11).GetString());

                    var isTransfer = false;
                    if (!string.IsNullOrEmpty(isTransferStr))
                        bool.TryParse(isTransferStr, out isTransfer);

                    var errors = new List<string>();

                    // Parse amount
                    var amountOk = double.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount)
                        || double.TryParse(amountStr, NumberStyles.Any, CultureInfo.CurrentCulture, out amount);

                    // Parse date
                    var dateOk = DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                        || DateTime.TryParse(dateStr, CultureInfo.CurrentCulture, DateTimeStyles.None, out date);

                    // Validaciones básicas
                    if (string.IsNullOrWhiteSpace(description)) errors.Add("Empty description");
                    if (!amountOk) errors.Add("Invalid amount");
                    if (!dateOk) errors.Add("Invalid date");
                    if (!validTypes.Contains(type, StringComparer.OrdinalIgnoreCase)) errors.Add("Invalid type");
                    if (!validMovements.Contains(movementType, StringComparer.OrdinalIgnoreCase)) errors.Add("Invalid movement type");
                    if (string.IsNullOrWhiteSpace(bankOriginName) || !banksDict.ContainsKey(bankOriginName)) errors.Add("Invalid or empty Bank (Origin)");

                    if (isTransfer)
                    {
                        if (string.IsNullOrWhiteSpace(bankOriginName) || !banksDict.ContainsKey(bankOriginName))
                            errors.Add("Invalid or empty Bank (Origin)");
                        if (string.IsNullOrWhiteSpace(bankDestinationName) || !banksDict.ContainsKey(bankDestinationName))
                            errors.Add("Invalid or empty Bank (Destination)");
                        if (string.IsNullOrWhiteSpace(transferReference)) errors.Add("Transfer Reference is required");
                    }

                    if (errors.Any())
                    {
                        result.Pending.Add(new
                        {
                            description,
                            amount = amountOk ? amount : (double?)null,
                            date = dateOk ? date : (DateTime?)null,
                            category = categoryName,
                            type,
                            movementType,
                            bankOrigin = bankOriginName,
                            isTransfer,
                            bankDestination = bankDestinationName,
                            transferReference,
                            errors
                        });
                        continue;
                    }

                    var bankOriginId = banksDict[bankOriginName];
                    int? categoryId = null;

                    if (string.IsNullOrWhiteSpace(categoryName) || !categories.ContainsKey(categoryName))
                    {
                        if (isTransfer)
                        {
                            var transferCatName = "Transfer";
                            var existing = await _context.Categories.FirstOrDefaultAsync(c => c.UserId == userId && c.Name == transferCatName);
                            if (existing == null)
                            {
                                var newCat = new Category
                                {
                                    Name = transferCatName,
                                    Description = "Automatically created category for transfers",
                                    UserId = userId,
                                    IsActive = true,
                                    IsSystem = false,
                                    CreatedAt = DateTime.UtcNow
                                };
                                _context.Categories.Add(newCat);
                                await _context.SaveChangesAsync();
                                categoryId = newCat.Id;
                                categories[newCat.Name] = newCat.Id;
                            }
                            else
                                categoryId = existing.Id;
                        }
                        else
                        {
                            result.Pending.Add(new { description, reason = "Invalid category" });
                            continue;
                        }
                    }
                    else
                        categoryId = categories[categoryName];

                    if (isTransfer)
                    {
                        var bankDestinationId = banksDict[bankDestinationName];
                        var tId = Guid.NewGuid().ToString();

                        var expense = new Expense
                        {
                            Description = description,
                            Amount = (decimal)amount,
                            Date = date,
                            CategoryId = categoryId!.Value,
                            Notes = notes,
                            Type = type,
                            UserId = userId,
                            BankId = bankOriginId,
                            IsTransfer = true,
                            TransferId = tId,
                            TransferCounterpartyBankId = bankDestinationId,
                            TransferReference = string.IsNullOrWhiteSpace(transferReference) ? null : transferReference
                        };

                        var income = new Income
                        {
                            Description = description,
                            Amount = (decimal)amount,
                            Date = date,
                            CategoryId = categoryId!.Value,
                            Notes = notes,
                            Type = type,
                            UserId = userId,
                            BankId = bankDestinationId,
                            IsTransfer = true,
                            TransferId = tId,
                            TransferCounterpartyBankId = bankOriginId,
                            TransferReference = string.IsNullOrWhiteSpace(transferReference) ? null : transferReference
                        };

                        _context.Expenses.Add(expense);
                        _context.Incomes.Add(income);
                        result.Imported.Add(new { description, amount, transfer = true, transferId = tId });
                    }
                    else
                    {
                        if (movementType.Equals("Income", StringComparison.OrdinalIgnoreCase))
                        {
                            _context.Incomes.Add(new Income
                            {
                                Description = description,
                                Amount = (decimal)amount,
                                Date = date,
                                CategoryId = categoryId!.Value,
                                Notes = notes,
                                Type = type,
                                UserId = userId,
                                BankId = bankOriginId
                            });
                        }
                        else
                        {
                            _context.Expenses.Add(new Expense
                            {
                                Description = description,
                                Amount = (decimal)amount,
                                Date = date,
                                CategoryId = categoryId!.Value,
                                Notes = notes,
                                Type = type,
                                UserId = userId,
                                BankId = bankOriginId
                            });
                        }
                        result.Imported.Add(new { description, amount });
                    }
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
