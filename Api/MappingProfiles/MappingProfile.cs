using AutoMapper;
using PersonalFinance.Api.Models.Dtos.Bank;
using PersonalFinance.Api.Models.Dtos.Expense;
using PersonalFinance.Api.Models.Dtos.Income;
using PersonalFinance.Api.Models.Dtos.User;
using PersonalFinance.Api.Models.Entities;

namespace PersonalFinance.Api.MappingProfiles
{
    // Central AutoMapper profile: ajusta los nombres de DTO si tu proyecto usa convenciones distintas.
    // Este archivo mapea todas las entidades del folder Models/Entities a DTOs esperados en Models/Dtos.
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            // --------------------
            // User / Auth
            // --------------------
            // RegisterRequestDto -> CreateUserDto
            CreateMap<RegisterRequestDto, CreateUserDto>();

            // ApplicationUser -> UserDto
            //CreateMap<ApplicationUser, UserDto>()
            //    .ForMember(d => d.Id, opt => opt.MapFrom(s => s.Id.ToString()))
            //    .ReverseMap()
            //    // Evitar intentar mapear string -> Guid directo desde DTO. Dejar que el servicio controle el Id.
            //    .ForMember(d => d.Id, opt => opt.Ignore());

            // --------------------
            // Bank / Category
            // --------------------
            CreateMap<Bank, BankDto>().ReverseMap();
            //CreateMap<Category, CategoryDto>().ReverseMap();

            // --------------------
            // Income / Expense
            // --------------------
            CreateMap<Income, IncomeDto>()
                .ForMember(d => d.BankName, opt => opt.MapFrom(s => s.Bank != null ? s.Bank.Name : null))
                .ForMember(d => d.CategoryName, opt => opt.MapFrom(s => s.Category != null ? s.Category.Name : null))
                .ForMember(d => d.TransferReference, opt => opt.MapFrom(s => s.TransferReference))
                .ReverseMap()
                // Ignorar navegaciones al mapear desde DTO hacia la entidad; el servicio debe asignarlas por Id.
                .ForMember(d => d.Bank, opt => opt.Ignore())
                .ForMember(d => d.Category, opt => opt.Ignore())
                // Si el DTO contiene BankId/CategoryId como string, manejar parsing en el servicio/handler.
                ;

            CreateMap<Expense, ExpenseDto>()
                .ForMember(d => d.BankName, opt => opt.MapFrom(s => s.Bank != null ? s.Bank.Name : null))
                .ForMember(d => d.CategoryName, opt => opt.MapFrom(s => s.Category != null ? s.Category.Name : null))
                .ForMember(d => d.TransferReference, opt => opt.MapFrom(s => s.TransferReference))
                .ReverseMap()
                .ForMember(d => d.Bank, opt => opt.Ignore())
                .ForMember(d => d.Category, opt => opt.Ignore())
                ;

            // --------------------
            // Loans
            // --------------------
            //CreateMap<Loan, LoanDto>()
            //    .ForMember(d => d.UserId, opt => opt.MapFrom(s => s.UserId.ToString()))
            //    .ReverseMap()
            //    // Ignorar UserId al mapear desde DTO; el servicio debe convertir string->Guid si corresponde.
            //    .ForMember(d => d.UserId, opt => opt.Ignore());

            //CreateMap<LoanPayment, LoanPaymentDto>()
            //    // Si LoanPaymentDto.LoanId es string en DTO y LoanPayment.LoanId es Guid, evitar parseo; el servicio debe hacerlo.
            //    .ForMember(d => d.LoanId, opt => opt.MapFrom(s => s.LoanId))
            //    .ReverseMap();

            // --------------------
            // Pasanaco (grupos/créditos locales)
            // --------------------
            //CreateMap<Pasanaco, PasanacoDto>()
            //    .ForMember(d => d.OwnerId, opt => opt.MapFrom(s => s.UserId.ToString()))
            //    .ReverseMap()
            //    .ForMember(d => d.UserId, opt => opt.Ignore());

            //CreateMap<PasanacoPayment, PasanacoPaymentDto>()
            //    .ReverseMap();

            //// Participant (miembro de pasanaco)
            //CreateMap<Participant, ParticipantDto>()
            //    // si ParticipantDto tiene UserId/OwnerId como string:
            //    .ForMember(d => d.UserId, opt => opt.MapFrom(s => s.UserId.ToString()))
            //    .ForMember(d => d.PasanacoId, opt => opt.MapFrom(s => s.PasanacoId))
            //    .ReverseMap()
            //    // Ignorar guids en ReverseMap para evitar parseo automático desde string.
            //    .ForMember(d => d.UserId, opt => opt.Ignore())
            //    .ForMember(d => d.PasanacoId, opt => opt.Ignore());

            // --------------------
            // Savings
            // --------------------
            //CreateMap<SavingAccount, SavingAccountDto>()
            //    .ForMember(d => d.UserId, opt => opt.MapFrom(s => s.UserId.ToString()))
            //    .ReverseMap()
            //    .ForMember(d => d.UserId, opt => opt.Ignore());

            //CreateMap<SavingMovement, SavingMovementDto>()
            //    // Mapear AccountId desde SavingAccountId
            //    .ForMember(d => d.AccountId, opt => opt.MapFrom(s => s.SavingAccountId))
            //    .ReverseMap()
            //    // Si SavingMovementDto.AccountId es string, no intentar convertir automáticamente; ignorar.
            //    .ForMember(d => d.SavingAccountId, opt => opt.Ignore());

            // --------------------
            // Reconciliation / Other small entities
            // --------------------
            CreateMap<Reconciliation, ReconciliationDto>().ReverseMap();

            //CreateMap<MonthlyIncomeProjection, MonthlyIncomeProjectionDto>().ReverseMap();

            // --------------------
            // Fallbacks / notas:
            // - Si en el futuro agregas DTOs que contienen Ids como string, sigue la misma convención:
            //   entidad.Guid -> dto.string via ToString()
            //   dto.string -> entidad.Guid : Ignorar y hacer parseo en la capa de servicio/handler.
            // - Evitar mapear navegaciones complejas desde DTOs para mantener la responsabilidad clara.
        }
    }
}