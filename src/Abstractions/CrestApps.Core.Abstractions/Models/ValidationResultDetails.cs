using System.ComponentModel.DataAnnotations;

namespace CrestApps.Core.Models;

public sealed class ValidationResultDetails
{
    private List<ValidationResult> _errors;
    public IReadOnlyList<ValidationResult> Errors
    {
        get
        {
            return _errors ??= [];
        }
    }

    /// <summary>
    /// Success may be altered by a handler during the validating async event.
    /// </summary>
    public bool Succeeded { get; set; } = true;

    public void Fail(ValidationResult error)
    {
        Succeeded = false;
        _errors ??= [];
        _errors.Add(error);
    }
}
