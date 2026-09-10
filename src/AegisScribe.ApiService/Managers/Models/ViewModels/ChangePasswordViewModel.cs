namespace AegisScribe.ApiService.Managers.Models.ViewModels;

public class ChangePasswordViewModel
{
    public string Email { get; set; } = string.Empty;
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
