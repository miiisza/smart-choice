using System.ComponentModel.DataAnnotations;

namespace SmartChoice.Api.Validation;

public static class RequestValidation
{
    public static Dictionary<string, string[]> Validate<T>(T request)
    {
        var validationResults = new List<ValidationResult>();
        var context = new ValidationContext(request!);

        Validator.TryValidateObject(request!, context, validationResults, validateAllProperties: true);

        return validationResults
            .SelectMany(result => result.MemberNames.DefaultIfEmpty(string.Empty),
                (result, memberName) => new { memberName, result.ErrorMessage })
            .GroupBy(item => string.IsNullOrWhiteSpace(item.memberName) ? "request" : item.memberName)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(item => item.ErrorMessage)
                    .Where(message => !string.IsNullOrWhiteSpace(message))
                    .Cast<string>()
                    .Distinct(StringComparer.Ordinal)
                    .ToArray());
    }
}
