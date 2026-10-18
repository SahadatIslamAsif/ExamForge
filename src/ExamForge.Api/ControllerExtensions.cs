using System.Diagnostics;
using ExamForge.Api.Papers;
using ExamForge.Domain.Catalog;
using Microsoft.AspNetCore.Mvc;

namespace ExamForge.Api;

/// <summary>Result mapping shared by every controller, so each status code is decided in one place.</summary>
public static class ControllerExtensions
{
    /// <summary>400 ValidationProblemDetails with one entry per field.</summary>
    public static IActionResult FieldErrors(this ControllerBase controller, IReadOnlyDictionary<string, List<string>> errors)
    {
        foreach (var (field, messages) in errors)
        {
            foreach (var message in messages)
            {
                controller.ModelState.AddModelError(field, message);
            }
        }

        return controller.ValidationProblem(controller.ModelState);
    }

    public static IActionResult FieldError(this ControllerBase controller, string field, string message) =>
        controller.FieldErrors(new Dictionary<string, List<string>> { [field] = [message] });

    /// <summary>Ok → the given success result; NotFound → 404; Invalid → 400 on its field; Conflict → 409.</summary>
    public static IActionResult FromCatalog<T>(this ControllerBase controller, CatalogResult<T> result, Func<T, IActionResult> onOk) =>
        result switch
        {
            CatalogResult<T>.Ok ok => onOk(ok.Value),
            CatalogResult<T>.NotFound => controller.NotFound(),
            CatalogResult<T>.Invalid invalid => controller.FieldError(invalid.Field, invalid.Message),
            CatalogResult<T>.Conflict conflict => controller.Conflict(new ErrorResponse(conflict.Error, conflict.Message)),
            _ => throw new UnreachableException($"Unhandled catalog result {result.GetType().Name}."),
        };
}

/// <summary>Adds a message under a field, creating the list on first use.</summary>
public static class FieldErrorDictionaryExtensions
{
    public static void AddError(this Dictionary<string, List<string>> errors, string field, string message)
    {
        if (!errors.TryGetValue(field, out var messages))
        {
            errors[field] = messages = [];
        }

        messages.Add(message);
    }
}
