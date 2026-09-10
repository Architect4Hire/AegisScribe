namespace AegisScribe.ApiService.Managers.Models.ViewModels;

public class RegisterViewModel
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
}
