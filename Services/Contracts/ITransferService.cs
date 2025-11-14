namespace PersonalFinance.Api.Services.Contracts
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

        public interface ITransferService
        {
            /// <summary>
            /// Crea una transferencia atómica: una Expense en el banco origen y una Income en el banco destino,
            /// ambas marcadas con el mismo transferId.
            /// </summary>
            Task<string> CreateTransferAsync(Guid userId, CreateTransferRequest dto, CancellationToken cancellationToken = default);

            /// <summary>
            /// Obtiene los movimientos (income/expense) asociados a un transferId.
            /// </summary>
            Task<object> GetByTransferIdAsync(string transferId, CancellationToken cancellationToken = default);
        }

        public class CreateTransferRequest
        {
            public DateTime Date { get; set; }
            public decimal Amount { get; set; }
            public Guid FromBankId { get; set; }
            public Guid ToBankId { get; set; }
            public string Description { get; set; } = string.Empty;
            public string? Notes { get; set; }
            public string? Reference { get; set; }
        }
    
}
