public interface IAuthService
{
    Task RegisterAsync(RegisterRequestDto dto);
    Task<string> LoginAsync(LoginRequestDto dto);
}