using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Extensions;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Services;
using PersonalFinance.Api.Services.Contracts;

namespace PersonalFinance.Api.Controllers
{
    [ApiController]
    [Route("api/loans/{loanId}/payments")]
    [Authorize]
    public class PaymentsController : ControllerBase
    {
        private readonly IPaymentService _paymentService;
        public PaymentsController(IPaymentService paymentService) => _paymentService = paymentService;

        [HttpGet]
        public async Task<IActionResult> GetPayments(Guid loanId)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var payments = await _paymentService.GetPaymentsAsync(userId.Value, loanId);
            if (payments == null) return NotFound();
            return Ok(payments);
        }

        [HttpPost]
        public async Task<IActionResult> CreatePayment(Guid loanId, [FromBody] LoanPayment payment)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var created = await _paymentService.CreatePaymentAsync(userId.Value, loanId, payment);
            if (created == null) return NotFound();
            return Ok(created);
        }
    }

}
