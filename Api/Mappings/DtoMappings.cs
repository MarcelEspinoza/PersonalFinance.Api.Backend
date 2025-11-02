using PersonalFinance.Api.Models; // RegisterRequestDto

internal static class DtoMappings
{
    public static CreateUserDto ToCreateUserDto(this RegisterRequestDto r) =>
        new CreateUserDto { Email = r.Email, Password = r.Password, FullName = r.FullName };
}