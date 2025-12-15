using AutoMapper;
using PersonalFinance.Api.Models.Dtos;
using PersonalFinance.Api.Models.Dtos.Bank;
using PersonalFinance.Api.Models.Dtos.Expense;
using PersonalFinance.Api.Models.Dtos.Income;
using PersonalFinance.Api.Models.Dtos.User;
using PersonalFinance.Api.Models.Dtos.Category;
using PersonalFinance.Api.Models.Dtos.Pasanaco;
using PersonalFinance.Api.Models.Entities;

namespace PersonalFinance.Api.Api.MappingProfiles
{
    /*
    PSEUDOCÓDIGO / PLAN (detallado):
    1. Asegurar mappings bidireccionales entre entidades y DTOs usados por Loan:
       - Category <-> CreateCategoryDto
       - LoanPayment <-> LoanPaymentDto (ignorar navegación a Loan al mapear hacia la entidad)
       - Pasanaco <-> PasanacoDto
    2. Definir mapping explícito Loan <-> LoanDto:
       - Mapear propiedades escalares (Id, UserId, Type, Name, PrincipalAmount, OutstandingAmount, StartDate, DueDate, Status, InterestRate, TAE, InstallmentsPaid, InstallmentsRemaining, NextPaymentAmount, NextPaymentDate, CategoryId, PasanacoId)
       - Mapear navegaciones desde entidad hacia DTO: Category, Payments, Pasanaco
       - En ReverseMap (LoanDto -> Loan) ignorar navegaciones complejas (Category, Payments, Pasanaco) para que la capa de servicio controle la resolución por Id
    3. Mantener ReverseMap para entidades auxiliares y evitar ciclos.
    4. Comentarios en español explicando decisiones.
    */

    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            // --------------------
            // User / Auth
            // --------------------
            CreateMap<RegisterRequestDto, CreateUserDto>();

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
                .ForMember(d => d.Bank, opt => opt.Ignore())
                .ForMember(d => d.Category, opt => opt.Ignore());   

            CreateMap<Expense, ExpenseDto>()
                .ForMember(d => d.BankName, opt => opt.MapFrom(s => s.Bank != null ? s.Bank.Name : null))
                .ForMember(d => d.CategoryName, opt => opt.MapFrom(s => s.Category != null ? s.Category.Name : null))
                .ForMember(d => d.TransferReference, opt => opt.MapFrom(s => s.TransferReference))
                .ReverseMap()
                .ForMember(d => d.Bank, opt => opt.Ignore())
                .ForMember(d => d.Category, opt => opt.Ignore());

            // --------------------
            // Loans (mappings añadidos / corregidos)
            // --------------------

            // Mapeo auxiliar para pagos de préstamo
            CreateMap<LoanPayment, LoanPaymentDto>()
                // Mapear campos simples por convención
                .ForMember(d => d.ExpenseId, opt => opt.MapFrom(s => s.ExpenseId))
                .ReverseMap()
                // Evitar mapear la navegación Loan al hacer ReverseMap para prevenir ciclos y delegar resolución en servicio
                .ForMember(d => d.Loan, opt => opt.Ignore());

            // Mapeo Pasanaco <-> PasanacoDto
            CreateMap<Pasanaco, PasanacoDto>().ReverseMap();

            // Loan <-> LoanDto: mapear propiedades escalares en automático,
            // mapear colecciones/navegaciones desde entidad hacia DTOs,
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
                .ForMember(d => d.Pasanaco, opt => opt.Ignore());

            // --------------------
            // Reconciliation / Other small entities
            // --------------------
            CreateMap<Reconciliation, ReconciliationDto>().ReverseMap();
        }
    }
}