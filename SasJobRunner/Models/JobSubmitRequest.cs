using System.ComponentModel.DataAnnotations;

namespace SasJobRunner.Models;

public class JobSubmitRequest
{
    [Required] public string SasCode { get; set; } = "";
}
