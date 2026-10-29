using LedgerCore.Api.Problems;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LedgerCore.Api.Projections;

internal static class ProjectionAdminEndpoints
{
    public const string EnabledSetting = "Admin:Enabled";
    public const string NotFoundCode = "projection.not_found";
    public const string RebuildRunningCode = "projection.rebuild_running";

    private const string GetRebuildRoute = "GetProjectionRebuild";

    /// <summary>Maps the routes only when Admin:Enabled is true; nothing guards them yet.</summary>
    public static IEndpointRouteBuilder MapProjectionAdmin(this IEndpointRouteBuilder routes, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>(EnabledSetting))
        {
            return routes;
        }

        var projections = routes.MapGroup("/admin/projections").WithTags("Admin");
        projections.MapGet("/", List)
            .WithName("ListProjections")
            .WithSummary("List the projections and the last rebuild of each");

        projections.MapPost("/{name}/rebuild", Rebuild)
            .WithName("RebuildProjection")
            .WithSummary("Empty one projection and apply every stored event to it again, in the background")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        projections.MapGet("/{name}/rebuild", GetRebuild)
            .WithName(GetRebuildRoute)
            .WithSummary("Read how far the last rebuild of a projection has got")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return routes;
    }

    private static Ok<IEnumerable<ProjectionResponse>> List(ProjectionRebuilds rebuilds) =>
        TypedResults.Ok(rebuilds.Projections.Select(name => new ProjectionResponse(name, rebuilds.StatusOf(name))));

    private static Results<AcceptedAtRoute<RebuildStatus>, ProblemHttpResult> Rebuild(string name, ProjectionRebuilds rebuilds)
    {
        if (!rebuilds.Knows(name))
        {
            return NotFound(name);
        }

        return rebuilds.Start(name) is { } started
            ? TypedResults.AcceptedAtRoute(started, GetRebuildRoute, new { name })
            : Problem(StatusCodes.Status409Conflict, RebuildRunningCode, $"Projection '{name}' is already being rebuilt.");
    }

    private static Results<Ok<RebuildStatus>, ProblemHttpResult> GetRebuild(string name, ProjectionRebuilds rebuilds)
    {
        if (!rebuilds.Knows(name))
        {
            return NotFound(name);
        }

        return rebuilds.StatusOf(name) is { } status
            ? TypedResults.Ok(status)
            : Problem(StatusCodes.Status404NotFound, NotFoundCode, $"Projection '{name}' hasn't been rebuilt since the API started.");
    }

    private static ProblemHttpResult NotFound(string name) =>
        Problem(StatusCodes.Status404NotFound, NotFoundCode, $"No projection is named '{name}'.");

    private static ProblemHttpResult Problem(int status, string code, string detail) =>
        TypedResults.Problem(
            detail: detail,
            statusCode: status,
            extensions: new Dictionary<string, object?> { [DomainProblems.CodeExtension] = code });
}

internal sealed record ProjectionResponse(string Name, RebuildStatus? LastRebuild);
