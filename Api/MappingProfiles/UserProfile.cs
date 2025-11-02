using AutoMapper;
using PersonalFinance.Api.Models;        // RegisterRequestDto

public class UserProfile : Profile
{
    public UserProfile()
    {
        CreateMap<RegisterRequestDto, CreateUserDto>();
    }
}