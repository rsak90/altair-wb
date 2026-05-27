using System.ComponentModel.DataAnnotations;

namespace SasJobRunner.Models;

public class LoginViewModel
{
    [Required]
    public string Username { get; set; } = "";

    [Required]
    public string Password { get; set; } = "";

    public string? ErrorMessage { get; set; }
}
