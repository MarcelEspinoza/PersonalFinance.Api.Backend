namespace PersonalFinance.Api.Models
{
    public class UpdateUserDto
    {
        public string? Email { get; init; }
        public string? Password { get; init; }
        public string? FullName { get; init; }
    }
}
