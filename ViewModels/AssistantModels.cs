using System.ComponentModel.DataAnnotations;

namespace CampusCoin.ViewModels;

public class AssistantQuestion
{
    [Required, StringLength(400)]
    public string Message { get; set; } = "";
}

public record AssistantResult(string Answer, string Mode);
