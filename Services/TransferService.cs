using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models.Dtos.Category;
using PersonalFinance.Api.Models.Dtos.Income;
using PersonalFinance.Api.Models.Dtos.Expense;
using PersonalFinance.Api.Services.Contracts;

namespace PersonalFinance.Api.Services
{
    public class TransferService : ITransferService
    {
        private readonly AppDbContext _db;
        private readonly IIncomeService _incomeService;
        private readonly IExpenseService _expenseService;
        private readonly ICategoryService _categoryService;

        public TransferService(AppDbContext db,
                               IIncomeService incomeService,
                               IExpenseService expenseService,
                               ICategoryService categoryService)
        {
            _db = db;
            _incomeService = incomeService;
            _expenseService = expenseService;
            _categoryService = categoryService;
        }

        public async Task<string> CreateTransferAsync(Guid userId, CreateTransferRequest dto, CancellationToken cancellationToken = default)
        {
            if (dto.FromBankId == dto.ToBankId) throw new ArgumentException("FromBankId and ToBankId must be different.");

            // Aseguramos una categoría "Transferencia" para el usuario (puedes cambiar el nombre)
            const string transferCategoryName = "Transferencia";
            int categoryId;
            var existing = await _categoryService.FindByNameAsync(userId, transferCategoryName, cancellationToken);
            if (existing != null)
            {
                categoryId = existing.Id;
            }
            else
            {
                // CreateCategoryDto asume que tu servicio acepta este DTO con al menos Name y Description
                var createCatDto = new CreateCategoryDto
                {
                    Name = transferCategoryName,
                    Description = "Categoría generada automáticamente para traspasos"
                };
                var createdCat = await _categoryService.CreateAsync(userId, createCatDto, cancellationToken);
                categoryId = createdCat.Id;
            }

            var transferId = Guid.NewGuid().ToString();

            // Usamos una transacción DB para que ambas creaciones sean atómicas
            using (var tx = await _db.Database.BeginTransactionAsync(cancellationToken))
            {
                try
                {
                    // Mapear a CreateExpenseDto y CreateIncomeDto según tus DTOs reales.
                    // Aquí uso las propiedades comunes que suelen existir; ajusta si tus DTOs difieren.
                    var createExpenseDto = new CreateExpenseDto
                    {
                        Date = dto.Date,
                        Amount = dto.Amount,
                        Description = dto.Description,
                        Notes = dto.Notes,
                        CategoryId = categoryId,
                        BankId = dto.FromBankId,
                        // campos opcionales: Start_Date, End_Date, LoanId, etc. los dejamos null
                        // AÑADIR los campos para transfer metadata (si tus DTOs lo permiten)
                    };

                    // Si tus CreateExpenseDto/ CreateIncomeDto no tienen propiedades para TransferId etc.
                    // tenemos que asignarlas manualmente en la entidad o ampliar los DTOs en los servicios.
                    // A continuación se asume que los servicios admiten esos campos opcionalmente.
                    // Si no, habrá que ampliar los DTOs de create.

                    // Si tus servicios no soportan transfer metadata en el DTO, en su lugar
                    // podrías crear la entidad directamente en DbContext como alternativa.

                    // Llamadas a servicios
                    var createdExpense = await _expenseService.CreateAsync(userId, createExpenseDto, cancellationToken);

                    var createIncomeDto = new CreateIncomeDto
                    {
                        Date = dto.Date,
                        Amount = dto.Amount,
                        Description = dto.Description,
                        Notes = dto.Notes,
                        CategoryId = categoryId,
                        BankId = dto.ToBankId
                    };

                    var createdIncome = await _incomeService.CreateAsync(userId, createIncomeDto, cancellationToken);

                    // Ahora — algunos servicios no devuelven el objeto creado o no exponen la
                    // asignación de TransferId/IsTransfer. Si tus servicios exponen Update,
                    // hacemos un update posterior para guardar TransferId/IsTransfer.
                    //
                    // Intentamos actualizar ambos registros con metadata del transfer;
                    // para ello asumo que CreateAsync devuelve un objeto con Id.

                    // Intento setear metadata en las tablas directas (si tus servicios no lo hacen)
                    // Puedes implementar en tus servicios la capacidad de recibir metadata en el DTO,
                    // pero aquí dejo un fallback directo sobre DbContext para escribir el transfer metadata.

                    // obtener los ids creados (ajusta según lo que devuelva CreateAsync)
                    int expenseId = (createdExpense?.Id) ?? 0;
                    int incomeId = (createdIncome?.Id) ?? 0;

                    // Escribe metadata directamente si existe la fila (fallback)
                    if (expenseId > 0)
                    {
                        var expEntry = await _db.Expenses.FindAsync(new object[] { expenseId }, cancellationToken);
                        if (expEntry != null)
                        {
                            expEntry.IsTransfer = true;
                            expEntry.TransferId = transferId;
                            expEntry.TransferCounterpartyBankId = dto.ToBankId;
                            expEntry.TransferReference = dto.Reference;
                            _db.Expenses.Update(expEntry);
                        }
                    }

                    if (incomeId > 0)
                    {
                        var incEntry = await _db.Incomes.FindAsync(new object[] { incomeId }, cancellationToken);
                        if (incEntry != null)
                        {
                            incEntry.IsTransfer = true;
                            incEntry.TransferId = transferId;
                            incEntry.TransferCounterpartyBankId = dto.FromBankId;
                            incEntry.TransferReference = dto.Reference;
                            _db.Incomes.Update(incEntry);
                        }
                    }

                    await _db.SaveChangesAsync(cancellationToken);
                    await tx.CommitAsync(cancellationToken);

                    return transferId;
                }
                catch
                {
                    await tx.RollbackAsync(cancellationToken);
                    throw;
                }
            }
        }

        public async Task<object> GetByTransferIdAsync(string transferId, CancellationToken cancellationToken = default)
        {
            var incomes = await _db.Incomes.AsNoTracking().Where(i => i.TransferId == transferId).ToListAsync(cancellationToken);
            var expenses = await _db.Expenses.AsNoTracking().Where(e => e.TransferId == transferId).ToListAsync(cancellationToken);
            return new { incomes, expenses };
        }
    }
}