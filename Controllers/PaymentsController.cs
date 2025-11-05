using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Services;
using PersonalFinance.Api.Services.Contracts;

namespace PersonalFinance.Api.Controllers
{
    [ApiController]
    [Route("api/loans/{loanId}/payments")]
    public class PaymentsController : ControllerBase
    {
        private readonly IPaymentService _paymentService;
        public PaymentsController(IPaymentService paymentService) => _paymentService = paymentService;

        [HttpGet]
        public async Task<IActionResult> GetPayments(Guid loanId)
        {
            var payments = await _paymentService.GetPaymentsAsync(loanId);
            return Ok(payments);
        }

        [HttpPost]
        public async Task<IActionResult> CreatePayment(Guid loanId, [FromBody] LoanPayment payment)
        {
            
            var created = await _paymentService.CreatePaymentAsync(loanId, payment);
            return Ok(created);
        }
    }

}
