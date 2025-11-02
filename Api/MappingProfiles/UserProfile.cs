using AutoMapper;
using PersonalFinance.Api.Models.Dtos.User;        // RegisterRequestDto

public class UserProfile : Profile
{
    public UserProfile()
    {
        CreateMap<RegisterRequestDto, CreateUserDto>();
    }
}