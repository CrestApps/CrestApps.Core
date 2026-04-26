using System.ComponentModel.DataAnnotations;

namespace CrestApps.Core.Models;

/// <summary>
/// Holds the outcome of a catalog entry validation pass, including the overall
/// success flag and any collected <see cref="ValidationResult"/> errors.
/// </summary>
public sealed class ValidationResultDetails
{
    private List<ValidationResult> _errors;

    /// <summary>
    /// Gets the list of validation errors collected during the current validation pass.
    /// The collection is empty when validation succeeds.
    /// </summary>
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
