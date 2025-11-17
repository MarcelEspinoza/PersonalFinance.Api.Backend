using AutoMapper;
using PersonalFinance.Api.Models.Dtos;
using PersonalFinance.Api.Models.Dtos.Bank;
using PersonalFinance.Api.Models.Dtos.Expense;
using PersonalFinance.Api.Models.Dtos.Income;
using PersonalFinance.Api.Models.Dtos.User;
using PersonalFinance.Api.Models.Dtos.Category;
using PersonalFinance.Api.Models.Dtos.Pasanaco;
using PersonalFinance.Api.Models.Entities;

namespace PersonalFinance.Api.MappingProfiles
{
    /*
    PSEUDOCÓDIGO / PLAN (detallado):
    1. Crear mappings para entidades relacionadas usadas por LoanDto:
       - Category <-> CreateCategoryDto
       - LoanPayment <-> LoanPaymentDto
       - Pasanaco <-> PasanacoDto
    2. Definir mapping principal Loan <-> LoanDto:
       - Mapeo directo de propiedades compatibles (Id, UserId, Type, Name, PrincipalAmount, OutstandingAmount, StartDate, DueDate, Status, InterestRate, TAE, InstallmentsPaid, InstallmentsRemaining, NextPaymentAmount, NextPaymentDate, CategoryId, PasanacoId)
       - Mapear navegaciones: Category -> Category DTO, Payments -> List<LoanPaymentDto>, Pasanaco -> PasanacoDto
    3. En ReverseMap (LoanDto -> Loan):
       - Evitar mapear las navegaciones complejas (Category, Payments, Pasanaco) para que la capa de servicio controle la resolución por Id (responsabilidad clara).
       - Permitir mapear datos escalares y GUIDs directamente ya que tanto Loan y LoanDto usan Guid para UserId.
    4. Añadir ReverseMap para las entidades auxiliares y en caso necesario ignorar navegaciones circulares (por ejemplo Loan dentro de LoanPayment).
    5. Mantener coherencia con el estilo del proyecto y comentarios en español explicando decisiones.
    */

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
            CreateMap<Category, CreateCategoryDto>().ReverseMap();

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
            // Loans (mappings añadidos / corregidos)
            // --------------------

            // Mapeo auxiliar para pagos de préstamo
            CreateMap<LoanPayment, LoanPaymentDto>()
                // Evitar mapear la navegación Loan al hacer ReverseMap para prevenir ciclos y delegar resolución en servicio
                .ReverseMap()
                .ForMember(d => d.Loan, opt => opt.Ignore());

            // Mapeo Pasanaco <-> PasanacoDto (si se requiere)
            CreateMap<Pasanaco, PasanacoDto>().ReverseMap();

            // Loan <-> LoanDto: mapear propiedades escalares en automático,
            // mapear colecciones/navegaciones desde entidad hacia DTO,
            // y en ReverseMap ignorar navegaciones para que el servicio use Ids.
            CreateMap<Loan, LoanDto>()
                // propiedades simples y guids se mapearán por convención
                .ForMember(d => d.UserId, opt => opt.MapFrom(s => s.UserId))
                .ForMember(d => d.CategoryId, opt => opt.MapFrom(s => s.CategoryId))
                .ForMember(d => d.PasanacoId, opt => opt.MapFrom(s => s.PasanacoId))
                // Mapear objetos relacionados hacia DTOs (si existen)
                .ForMember(d => d.Category, opt => opt.MapFrom(s => s.Category))
                .ForMember(d => d.Payments, opt => opt.MapFrom(s => s.Payments))
                .ForMember(d => d.Pasanaco, opt => opt.MapFrom(s => s.Pasanaco))
                .ReverseMap()
                // Ignorar navegaciones complejas al mapear desde DTO hacia la entidad; el servicio debe asignarlas por Id.
                .ForMember(d => d.Category, opt => opt.Ignore())
                .ForMember(d => d.Payments, opt => opt.Ignore())
                .ForMember(d => d.Pasanaco, opt => opt.Ignore())
                ;

            // --------------------
            // Reconciliation / Other small entities
            // --------------------
            CreateMap<Reconciliation, ReconciliationDto>().ReverseMap();

            // --------------------
            // Fallbacks / notas:
            // - Si en el futuro agregas DTOs que contienen Ids como string, sigue la misma convención:
            //   entidad.Guid -> dto.string via ToString()
            //   dto.string -> entidad.Guid : Ignorar y hacer parseo en la capa de servicio/handler.
            // - Evitar mapear navegaciones complejas desde DTOs para mantener la responsabilidad clara.
        }
    }
}