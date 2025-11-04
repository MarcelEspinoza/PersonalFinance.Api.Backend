using ClosedXML.Excel;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models.Dtos.Import;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Services.Contracts;

namespace PersonalFinance.Api.Services
{
    public class ImportExcelService : IImportExcelService
    {
        private readonly AppDbContext _context;

        public ImportExcelService(AppDbContext context)
        {
            _context = context;
        }

        public byte[] GenerateTemplate()
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Plantilla");

            // Cabeceras
            ws.Cell(1, 1).Value = "description";
            ws.Cell(1, 2).Value = "amount";
            ws.Cell(1, 3).Value = "date";
            ws.Cell(1, 4).Value = "category";   // 👈 ahora será el nombre, no el id
            ws.Cell(1, 5).Value = "notes";
            ws.Cell(1, 6).Value = "type";       // Fixed | Variable | Temporary
            ws.Cell(1, 7).Value = "movementType"; // Income | Expense

            // Hoja de categorías
            var wsCategories = workbook.Worksheets.Add("Categorías");
            wsCategories.Cell(1, 1).Value = "Id";
            wsCategories.Cell(1, 2).Value = "Nombre";
            var categories = _context.Categories.ToList();
            for (int i = 0; i < categories.Count; i++)
            {
                wsCategories.Cell(i + 2, 1).Value = categories[i].Id;
                wsCategories.Cell(i + 2, 2).Value = categories[i].Name;
            }

            // Hoja de tipos
            var wsTypes = workbook.Worksheets.Add("Tipos");
            wsTypes.Cell(1, 1).Value = "Type";
            wsTypes.Cell(2, 1).Value = "Fixed";
            wsTypes.Cell(3, 1).Value = "Variable";
            wsTypes.Cell(4, 1).Value = "Temporary";

            // Hoja de movimientos
            var wsMovements = workbook.Worksheets.Add("Movements");
            wsMovements.Cell(1, 1).Value = "MovementType";
            wsMovements.Cell(2, 1).Value = "Income";
            wsMovements.Cell(3, 1).Value = "Expense";

            // Validaciones
            ws.Range("F2:F1000").SetDataValidation().List(wsTypes.Range("A2:A4"));
            ws.Range("D2:D1000").SetDataValidation().List(wsCategories.Range($"B2:B{categories.Count + 1}")); // 👈 por nombre
            ws.Range("G2:G1000").SetDataValidation().List(wsMovements.Range("A2:A3"));

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }


        public async Task<ImportResult> ImportTemplateAsync(IFormFile file, Guid userId)
        {
            var result = new ImportResult();

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            using var workbook = new XLWorkbook(stream);
            var ws = workbook.Worksheet("Plantilla");

            var rows = ws.RangeUsed().RowsUsed().Skip(1); // saltar cabecera

            // Tipos válidos
            var validTypes = new[] { "Fixed", "Variable", "Temporary" };
            var validMovements = new[] { "Income", "Expense" };

            // Diccionario categorías (Nombre -> Id)
            var categories = _context.Categories
                .ToDictionary(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                try
                {
                    var description = row.Cell(1).GetString().Trim();
                    var amountOk = double.TryParse(row.Cell(2).GetString(), out var amount);
                    var dateOk = DateTime.TryParse(row.Cell(3).GetString(), out var date);
                    var categoryName = row.Cell(4).GetString().Trim();
                    var notes = row.Cell(5).GetString();
                    var type = row.Cell(6).GetString().Trim();
                    var movementType = row.Cell(7).GetString().Trim();

                    // Validaciones
                    var errors = new List<string>();
                    if (string.IsNullOrWhiteSpace(description)) errors.Add("Descripción vacía");
                    if (!amountOk) errors.Add("Monto inválido");
                    if (!dateOk) errors.Add("Fecha inválida");
                    if (string.IsNullOrWhiteSpace(categoryName) || !categories.ContainsKey(categoryName))
                        errors.Add("Categoría inválida");
                    if (!validTypes.Contains(type)) errors.Add("Tipo inválido");
                    if (!validMovements.Contains(movementType)) errors.Add("MovementType inválido");

                    if (errors.Any())
                    {
                        result.Pending.Add(new
                        {
                            description,
                            amount,
                            date,
                            category = categoryName,
                            type,
                            movementType,
                            errors
                        });
                        continue; // saltar inserción
                    }

                    var categoryId = categories[categoryName];

                    if (movementType == "Income")
                    {
                        var income = new Income
                        {
                            Description = description,
                            Amount = (decimal)amount,
                            Date = date,
                            CategoryId = categoryId,
                            Notes = notes,
                            Type = type,
                            UserId = userId
                        };
                        _context.Incomes.Add(income);
                        result.Imported.Add(new { description, amount });
                    }
                    else // Expense
                    {
                        var expense = new Expense
                        {
                            Description = description,
                            Amount = (decimal)amount,
                            Date = date,
                            CategoryId = categoryId,
                            Notes = notes,
                            Type = type,
                            UserId = userId
                        };
                        _context.Expenses.Add(expense);
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
