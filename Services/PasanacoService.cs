using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models.Dtos.Pasanaco;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Services.Contracts;

namespace PersonalFinance.Api.Services
{
    public class PasanacoService : IPasanacoService
    {
        private readonly AppDbContext _context;

        public PasanacoService(AppDbContext context)
        {
            _context = context;
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
            var participant = new Participant
            {
                Name = dto.Name,
                AssignedNumber = dto.AssignedNumber,
                PasanacoId = pasanacoId,
                HasReceived = false
            };

            _context.Participants.Add(participant);
            await _context.SaveChangesAsync();
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
                        Pasanaco = await _context.Pasanacos.FindAsync(pasanacoId)!
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
    }

}
