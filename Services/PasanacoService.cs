using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models;
using PersonalFinance.Api.Models.Dtos;
using PersonalFinance.Api.Models.Dtos.Expense;
using PersonalFinance.Api.Models.Dtos.Pasanaco;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Services.Contracts;
using System.ComponentModel.DataAnnotations;

namespace PersonalFinance.Api.Services
{
    public class PasanacoService : IPasanacoService
    {
        private readonly AppDbContext _context;
        private readonly ILoanService loanService;
        private readonly IExpenseService expenseService;
        private readonly IIncomeService incomeService;

        public PasanacoService(AppDbContext context, ILoanService loanService, IExpenseService expenseService, IIncomeService incomeService)
        {
            _context = context;
            this.loanService = loanService;
            this.expenseService = expenseService;
            this.incomeService = incomeService;
        }

        public async Task<IEnumerable<PasanacoDto>> GetAllAsync()
        {
            return await _context.Pasanacos
                .Select(p => new PasanacoDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    MonthlyAmount = p.MonthlyAmount,
                    TotalParticipants = p.TotalParticipants,
                    CurrentRound = p.CurrentRound,
                    StartMonth = p.StartMonth,
                    StartYear = p.StartYear
                })
                .ToListAsync();
        }

        public async Task<PasanacoDto> GetByIdAsync(string id)
        {
            var pasanaco = await _context.Pasanacos.FindAsync(id);
            if (pasanaco == null) throw new Exception("Pasanaco no encontrado");

            return new PasanacoDto
            {
                Id = pasanaco.Id,
                Name = pasanaco.Name,
                MonthlyAmount = pasanaco.MonthlyAmount,
                TotalParticipants = pasanaco.TotalParticipants,
                CurrentRound = pasanaco.CurrentRound,
                StartMonth = pasanaco.StartMonth,
                StartYear = pasanaco.StartYear
            };
        }

        public async Task<PasanacoDto> CreateAsync(CreatePasanacoDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new ValidationException("El nombre es obligatorio");

            if (dto.MonthlyAmount <= 0)
                throw new ValidationException("El monto mensual debe ser mayor a 0");

            if (dto.TotalParticipants < 2)
                throw new ValidationException("Debe haber al menos 2 participantes");

            if (dto.StartMonth < 1 || dto.StartMonth > 12)
                throw new ValidationException("Mes de inicio inválido");

            if (dto.StartYear < 2000)
                throw new ValidationException("Año de inicio inválido");

            var entity = new Pasanaco
            {
                Name = dto.Name,
                MonthlyAmount = dto.MonthlyAmount,
                TotalParticipants = dto.TotalParticipants,
                CurrentRound = dto.CurrentRound,
                StartMonth = dto.StartMonth,
                StartYear = dto.StartYear
            };

            _context.Pasanacos.Add(entity);
            await _context.SaveChangesAsync();

            return await GetByIdAsync(entity.Id);
        }

        public async Task UpdateAsync(string id, UpdatePasanacoDto dto)
        {
            var pasanaco = await _context.Pasanacos.FindAsync(id);
            if (pasanaco == null) throw new Exception("Pasanaco no encontrado");

            pasanaco.Name = dto.Name;
            pasanaco.MonthlyAmount = dto.MonthlyAmount;
            pasanaco.TotalParticipants = dto.TotalParticipants;
            pasanaco.CurrentRound = dto.CurrentRound;
            pasanaco.StartMonth = dto.StartMonth;
            pasanaco.StartYear = dto.StartYear;

            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(string id)
        {
            var pasanaco = await _context.Pasanacos.FindAsync(id);
            if (pasanaco == null) return;

            _context.Pasanacos.Remove(pasanaco);
            await _context.SaveChangesAsync();
        }

        public async Task<IEnumerable<ParticipantDto>> GetParticipantsAsync(string pasanacoId)
        {
            return await _context.Participants
                .Where(p => p.PasanacoId == pasanacoId)
                .Select(p => new ParticipantDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    AssignedNumber = p.AssignedNumber,
                    HasReceived = p.HasReceived
                })
                .ToListAsync();
        }

        public async Task AddParticipantAsync(string pasanacoId, CreateParticipantDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new ValidationException("El nombre es obligatorio");

            if (dto.AssignedNumber < 1)
                throw new ValidationException("El número asignado debe ser mayor o igual a 1");

            var pasanaco = await _context.Pasanacos.FindAsync(pasanacoId);
            if (pasanaco == null) throw new ValidationException("Pasanaco no encontrado");

            if (dto.AssignedNumber > pasanaco.TotalParticipants)
                throw new ValidationException($"El número asignado debe estar entre 1 y {pasanaco.TotalParticipants}");

            var currentCount = await _context.Participants.CountAsync(p => p.PasanacoId == pasanacoId);
            if (currentCount >= pasanaco.TotalParticipants)
                throw new ValidationException($"No se pueden añadir más participantes. Límite: {pasanaco.TotalParticipants}");

            var exists = await _context.Participants.AnyAsync(p =>
                p.PasanacoId == pasanacoId && p.AssignedNumber == dto.AssignedNumber);
            if (exists)
                throw new ValidationException($"El número {dto.AssignedNumber} ya está asignado a otro participante");

            var participant = new Participant
            {
                Id = Guid.NewGuid().ToString(),
                PasanacoId = pasanacoId,
                Name = dto.Name.Trim(),
                AssignedNumber = dto.AssignedNumber,
                HasReceived = false
            };

            _context.Participants.Add(participant);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                throw new ValidationException("No se pudo añadir el participante. Verifica que el número no esté duplicado.");
            }
        }

        public async Task DeleteParticipantAsync(string pasanacoId, string participantId)
        {
            var participant = await _context.Participants
                .FirstOrDefaultAsync(p => p.Id == participantId && p.PasanacoId == pasanacoId);

            if (participant != null)
            {
                _context.Participants.Remove(participant);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<IEnumerable<PasanacoPaymentDto>> GetPaymentsAsync(string pasanacoId, int month, int year)
        {
            return await _context.PasanacoPayments
                .Where(p => p.PasanacoId == pasanacoId && p.Month == month && p.Year == year)
                .Select(p => new PasanacoPaymentDto
                {
                    Id = p.Id,
                    PasanacoId = p.PasanacoId,
                    ParticipantId = p.ParticipantId,
                    Month = p.Month,
                    Year = p.Year,
                    Paid = p.Paid,
                    PaymentDate = p.PaymentDate,
                    TransactionId = p.TransactionId
                })
                .ToListAsync();
        }

        public async Task GeneratePaymentsAsync(string pasanacoId, int month, int year)
        {
            var participants = await _context.Participants
                .Where(p => p.PasanacoId == pasanacoId)
                .ToListAsync();

            foreach (var participant in participants)
            {
                var exists = await _context.PasanacoPayments.AnyAsync(p =>
                    p.PasanacoId == pasanacoId &&
                    p.ParticipantId == participant.Id &&
                    p.Month == month &&
                    p.Year == year);

                if (!exists)
                {
                    _context.PasanacoPayments.Add(new PasanacoPayment
                    {
                        PasanacoId = pasanacoId,
                        ParticipantId = participant.Id,
                        Month = month,
                        Year = year,
                        Paid = false,
                        Participant = participant,
                        Pasanaco = await _context.Pasanacos.FindAsync(pasanacoId)
                    });
                }
            }

            await _context.SaveChangesAsync();
        }

        public async Task MarkPaymentAsPaidAsync(string paymentId, int? transactionId)
        {
            var payment = await _context.PasanacoPayments.FindAsync(paymentId);
            if (payment == null) throw new Exception("Pago no encontrado");

            payment.Paid = true;
            payment.PaymentDate = DateTime.UtcNow;
            payment.TransactionId = transactionId;

            await _context.SaveChangesAsync();
        }

        public async Task<bool> MarkPaymentAsPaidAsync(Guid paymentId, Guid userId)
        {
            var payment = await _context.PasanacoPayments.FindAsync(paymentId.ToString());
            if (payment == null || payment.Paid) return false;

            var pasanaco = await _context.Pasanacos.FindAsync(payment.PasanacoId);
            var participant = await _context.Participants.FindAsync(payment.ParticipantId);

            payment.Paid = true;
            payment.PaymentDate = DateTime.UtcNow;

            _context.Incomes.Add(new Income
            {
                Amount = pasanaco!.MonthlyAmount,
                Date = payment.PaymentDate.Value,
                Description = $"Pago de {participant!.Name} en pasanaco {pasanaco.Name}",
                Type = "Fixed",
                CategoryId = 300, // categoría "Pasanaco" ya existente
                UserId = userId,
                IsIndefinite = false,
                Start_Date = DateTime.UtcNow,
                End_Date = DateTime.UtcNow,

            });

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<Loan> CreateLoanForParticipantAsync(string pasanacoId, string participantId, decimal amount, Guid userId, string? note = null)
        {
            var pasanaco = await _context.Pasanacos.FindAsync(pasanacoId);
            if (pasanaco == null) throw new ValidationException("Pasanaco no encontrado");

            var participant = await _context.Participants.FindAsync(participantId);
            if (participant == null || participant.PasanacoId != pasanacoId) throw new ValidationException("Participante no válido");

            if (amount <= 0) throw new ValidationException("El importe del préstamo debe ser mayor que 0");

            // Crear Loan: ahora guardamos PasanacoId para relacionarlo
            var loan = new LoanDto
            {
                Id = Guid.NewGuid(),
                UserId = userId, // propietario del pasanaco
                Type = PersonalFinance.Api.Models.Enums.LoanType.Given,
                Name = $"Préstamo a {participant.Name} (Pasanaco {pasanaco.Name})",
                PrincipalAmount = amount,
                OutstandingAmount = amount,
                StartDate = DateTime.UtcNow,
                Status = "active",
                CategoryId = DefaultCategories.PersonalLoan,
                PasanacoId = pasanacoId 
            };

            var createdLoan = await loanService.CreateLoanAsync(loan);

            var expenseDto = new CreateExpenseDto
            {
                Amount = amount,
                Description = $"Préstamo otorgado a {participant.Name} (pasanaco {pasanaco.Name})",
                Date = DateTime.UtcNow,
                CategoryId = DefaultCategories.PersonalLoan, // o la categoría que quieras usar para el desembolso
                Notes = $"Préstamo manual por impago - LoanId:{createdLoan.Id} - {note ?? string.Empty}",
                Type = "Temporary",
                Start_Date = DateTime.UtcNow
            };

            // Ajusta la llamada según la firma real de tu ExpenseService.
            await expenseService.CreateAsync(userId, expenseDto, CancellationToken.None);

            // Marcar pago de este mes como pagado y referenciar Loan
            var date = new DateTime(pasanaco.StartYear, pasanaco.StartMonth, 1).AddMonths(pasanaco.CurrentRound - 1);
            var month = date.Month;
            var year = date.Year;

            var payment = await _context.PasanacoPayments
                .FirstOrDefaultAsync(p => p.PasanacoId == pasanacoId && p.ParticipantId == participantId && p.Month == month && p.Year == year);

            if (payment != null && !payment.Paid)
            {
                payment.Paid = true;
                payment.PaymentDate = DateTime.UtcNow;
                payment.PaidByLoanId = createdLoan.Id;
                _context.PasanacoPayments.Update(payment);
                await _context.SaveChangesAsync();
            }

            return createdLoan;
        }

        public async Task<int> DisbursePasanacoRoundAsync(string pasanacoId, Guid userId)
        {
            var pasanaco = await _context.Pasanacos.FindAsync(pasanacoId);
            if (pasanaco == null) throw new ValidationException("Pasanaco no encontrado");

            var date = new DateTime(pasanaco.StartYear, pasanaco.StartMonth, 1).AddMonths(pasanaco.CurrentRound - 1);
            var month = date.Month;
            var year = date.Year;

            // comprobar que todos los pagos del mes estén (pagados o gestionados mediante préstamo)
            var unpaid = await _context.PasanacoPayments
                .Where(p => p.PasanacoId == pasanacoId && p.Month == month && p.Year == year && !p.Paid)
                .ToListAsync();

            if (unpaid.Any())
                throw new ValidationException("No se puede distribuir: hay pagos pendientes. Resuélvelos o crea préstamos manualmente.");

            // destinatario: participante cuyo AssignedNumber == currentRound
            var recipient = await _context.Participants
                .FirstOrDefaultAsync(p => p.PasanacoId == pasanacoId && p.AssignedNumber == pasanaco.CurrentRound);

            if (recipient == null) throw new ValidationException("No se encontró el destinatario para la ronda actual");

            // calcular monto total a pagar (suma de los pagos del mes o mensualidad * totalParticipants)
            var totalAmount = pasanaco.MonthlyAmount * pasanaco.TotalParticipants;

            // Crear Expense pagado al destinatario (salida de dinero)
            var expenseDto = new CreateExpenseDto
            {
                Amount = totalAmount,
                Description = $"Pago pasanaco a {recipient.Name} (ronda {pasanaco.CurrentRound})",
                Date = DateTime.UtcNow,
                CategoryId = 300, // ajusta a la categoría 'Pasanaco' que uses
                Notes = $"Distribución del pasanaco {pasanaco.Name} - ronda {pasanaco.CurrentRound}"
            };

            var createdExpense = await expenseService.CreateAsync(userId, expenseDto, CancellationToken.None);

            // Enlazar TransactionId en PasanacoPayments si procede
            var payments = await _context.PasanacoPayments
                .Where(p => p.PasanacoId == pasanacoId && p.Month == month && p.Year == year)
                .ToListAsync();

            foreach (var pay in payments)
            {
                pay.TransactionId = createdExpense.Id; // si tu schema lo admite
                _context.PasanacoPayments.Update(pay);
            }
            await _context.SaveChangesAsync();

            return createdExpense.Id;
        }

        // AdvanceRoundAsync con opción createLoans (ya existente en tu repo)
        public async Task<bool> AdvanceRoundAsync(string pasanacoId, Guid userId, bool createLoans = false)
        {
            var pasanaco = await _context.Pasanacos.FindAsync(pasanacoId);
            if (pasanaco == null) throw new ValidationException("Pasanaco no encontrado");

            var date = new DateTime(pasanaco.StartYear, pasanaco.StartMonth, 1, 0, 0, 0, DateTimeKind.Utc)
                       .AddMonths(pasanaco.CurrentRound - 1);
            var month = date.Month;
            var year = date.Year;

            var payments = await _context.PasanacoPayments
                .Where(p => p.PasanacoId == pasanacoId && p.Month == month && p.Year == year)
                .Include(p => p.Participant)
                .ToListAsync();

            var unpaid = payments.Where(p => !p.Paid).ToList();

            if (unpaid.Any() && !createLoans)
            {
                return false;
            }

            if (unpaid.Any() && createLoans)
            {
                using var tx = await _context.Database.BeginTransactionAsync();
                try
                {
                    foreach (var up in unpaid)
                    {
                        var loan = new LoanDto
                        {
                            Id = Guid.NewGuid(),
                            UserId = userId,
                            Type = PersonalFinance.Api.Models.Enums.LoanType.Given,
                            Name = $"Préstamo por impago - {pasanaco.Name} - {up.Participant?.Name ?? up.ParticipantId}",
                            PrincipalAmount = pasanaco.MonthlyAmount,
                            OutstandingAmount = pasanaco.MonthlyAmount,
                            StartDate = DateTime.UtcNow,
                            Status = "active",
                            CategoryId = DefaultCategories.PersonalLoan,
                            PasanacoId = pasanacoId // relacionar loan con pasanaco
                        };

                        var createdLoan = await loanService.CreateLoanAsync(loan);

                        var expenseDto = new CreateExpenseDto
                        {
                            Amount = pasanaco.MonthlyAmount,
                            Description = $"Préstamo por impago a {up.Participant?.Name ?? up.ParticipantId} (pasanaco {pasanaco.Name})",
                            Date = DateTime.UtcNow,
                            CategoryId = DefaultCategories.PersonalLoan,
                            Notes = $"Autogenerado por avance de ronda: pasa id {pasanaco.Id}",
                            LoanId = createdLoan.Id
                        };

                        await expenseService.CreateAsync(userId, expenseDto, CancellationToken.None);

                        up.Paid = true;
                        up.PaymentDate = DateTime.UtcNow;
                        up.PaidByLoanId = createdLoan.Id;

                        _context.PasanacoPayments.Update(up);
                    }

                    await _context.SaveChangesAsync();
                    await tx.CommitAsync();
                }
                catch
                {
                    await tx.RollbackAsync();
                    throw;
                }
            }

            pasanaco.CurrentRound++;
            await _context.SaveChangesAsync();

            return true;
        }

        public async Task<bool> RetreatRoundAsync(string pasanacoId)
        {
            var pasanaco = await _context.Pasanacos.FindAsync(pasanacoId);
            if (pasanaco == null) throw new ValidationException("Pasanaco no encontrado");

            if (pasanaco.CurrentRound <= 1) return false;

            pasanaco.CurrentRound--;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<PasanacoPayment?> GetPaymentByTransactionIdAsync(int transactionId)
        {
            return await _context.PasanacoPayments
                .FirstOrDefaultAsync(p => p.TransactionId == transactionId);
        }

        public (int month, int year) GetCurrentMonthYearForPasanaco(PasanacoDto pasanaco)
        {
            var d = new DateTime(pasanaco.StartYear, pasanaco.StartMonth, 1).AddMonths(pasanaco.CurrentRound - 1);
            return (d.Month, d.Year);
        }

        public async Task<PasanacoPayment?> GetPaymentByLoanIdAsync(Guid loanId)
        {
            var loanIdVal = loanId;
            return await _context.PasanacoPayments
                .FirstOrDefaultAsync(p => p.PaidByLoanId != Guid.Empty && p.PaidByLoanId == loanIdVal);
        }

        public async Task<bool> UndoPaymentAsync(string paymentId, Guid userId)
        {
            var payment = await _context.PasanacoPayments.FirstOrDefaultAsync(p => p.Id == paymentId);
            if (payment == null) throw new ValidationException("Pago no encontrado");

            var pasanaco = await _context.Pasanacos.FindAsync(payment.PasanacoId);
            if (pasanaco == null) throw new ValidationException("Pasanaco no encontrado");

            var currentDate = new DateTime(pasanaco.StartYear, pasanaco.StartMonth, 1).AddMonths(pasanaco.CurrentRound - 1);
            var currentMonth = currentDate.Month;
            var currentYear = currentDate.Year;

            var isCurrentRoundPayment = (payment.Month == currentMonth && payment.Year == currentYear);

            if (!isCurrentRoundPayment)
            {
                throw new ValidationException("No se puede deshacer el pago: no corresponde a la ronda actual.");
            }

            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                if (payment.TransactionId != null)
                {
                    try
                    {
                        await incomeService.DeleteAsync(payment.TransactionId.Value, userId);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException("No se pudo eliminar la transacción asociada al pago.", ex);
                    }
                }

                if (payment.PaidByLoanId != Guid.Empty)
                {
                    try
                    {
                        await loanService.DeleteLoanAsync(payment.PaidByLoanId);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException("No se pudo eliminar el préstamo asociado al pago.", ex);
                    }

                }

                payment.Paid = false;
                payment.PaymentDate = null;
                payment.TransactionId = null;
                payment.PaidByLoanId = Guid.Empty;

                _context.PasanacoPayments.Update(payment);
                await _context.SaveChangesAsync();

                await tx.CommitAsync();

                return true;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // --- NUEVOS MÉTODOS: resumen de relaciones y borrado en cascada controlado ---

        public async Task<Services.Contracts.RelatedSummaryDto> GetRelatedSummaryAsync(string pasanacoId)
        {
            // Count PasanacoPayments
            var paymentsCount = await _context.PasanacoPayments.CountAsync(p => p.PasanacoId == pasanacoId);

            // Loans: count by PasanacoId if present (we populate PasanacoId when creating loans from pasanaco)
            var loansCount = 0;
            try
            {
                loansCount = await _context.Loans.CountAsync(l => l.PasanacoId != null && l.PasanacoId == pasanacoId);
            }
            catch
            {
                // fallback (best-effort) if Loans model differs; try searching by Name/Notes
                loansCount = await _context.Loans.CountAsync(l => EF.Functions.ILike(l.Name ?? "", $"%{pasanacoId}%"));
            }

            // Expenses & Incomes: try PasanacoId or Notes containing pasanacoId (best-effort)
            var expensesCount = await _context.Expenses.CountAsync(e => (e.PasanacoId != null && e.PasanacoId == pasanacoId) || EF.Functions.ILike(e.Notes ?? "", $"%{pasanacoId}%"));
            var incomesCount = await _context.Incomes.CountAsync(i => (i.PasanacoId != null && i.PasanacoId == pasanacoId) || EF.Functions.ILike(i.Notes ?? "", $"%{pasanacoId}%"));

            return new Services.Contracts.RelatedSummaryDto
            {
                PaymentsCount = paymentsCount,
                LoansCount = loansCount,
                ExpensesCount = expensesCount,
                IncomesCount = incomesCount
            };
        }

        public async Task DeletePasanacoCascadeAsync(string pasanacoId, Guid performedByUserId)
        {
            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1) Remove payments first
                var payments = _context.PasanacoPayments.Where(p => p.PasanacoId == pasanacoId);
                _context.PasanacoPayments.RemoveRange(payments);

                // 2) Remove loans that reference pasanaco (prefer service delete to keep invariants)
                try
                {
                    var loans = await _context.Loans.Where(l => l.PasanacoId != null && l.PasanacoId == pasanacoId).ToListAsync();
                    foreach (var loan in loans)
                    {
                        // use loan service to delete properly (it should handle related loan payments etc.)
                        await loanService.DeleteLoanAsync(loan.Id);
                    }
                }
                catch
                {
                    // best-effort: if loan deletion via service fails, let saveChanges handle constraints/throw
                }

                // 3) Remove expenses/incomes referencing pasanaco (best-effort by PasanacoId or Notes)
                var expenses = _context.Expenses.Where(e => (e.PasanacoId != null && e.PasanacoId == pasanacoId) || EF.Functions.ILike(e.Notes ?? "", $"%{pasanacoId}%"));
                _context.Expenses.RemoveRange(expenses);

                var incomes = _context.Incomes.Where(i => (i.PasanacoId != null && i.PasanacoId == pasanacoId) || EF.Functions.ILike(i.Notes ?? "", $"%{pasanacoId}%"));
                _context.Incomes.RemoveRange(incomes);

                // 4) Finally remove pasanaco itself
                var pasanaco = await _context.Pasanacos.FindAsync(pasanacoId);
                if (pasanaco != null) _context.Pasanacos.Remove(pasanaco);

                await _context.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
    }

}