using AeroFlow.BaggageReclaim.Domain;
using Microsoft.AspNetCore.Mvc;

namespace AeroFlow.BaggageReclaim.Controllers;

/// <summary>Maps typed domain errors to ProblemDetails (400 / 404 / 409, ADR-0004) with the error's <c>code</c>.</summary>
internal static class ProblemResults
{
    public static ObjectResult Problem(this ControllerBase controller, Error error)
    {
        var (status, title) = error switch
        {
            ValidationError => (StatusCodes.Status400BadRequest, "Invalid request"),
            NotFoundError => (StatusCodes.Status404NotFound, "Not found"),
            RuleViolation => (StatusCodes.Status409Conflict, "Request rejected"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error"),
        };

        var problem = controller.ProblemDetailsFactory.CreateProblemDetails(
            controller.HttpContext,
            statusCode: status,
            title: title,
            detail: error.Message,
            instance: controller.HttpContext.Request.Path);
        problem.Extensions["code"] = error.Code;

        return new ObjectResult(problem) { StatusCode = status };
    }
}
