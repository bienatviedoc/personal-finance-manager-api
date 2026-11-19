using Microsoft.AspNetCore.Http.HttpResults;

namespace Pfm.Api;

/// <summary>What a paged list answers when its paging parameters are wrong (spec section 11).</summary>
public static class Paged
{
    public static ValidationProblem Invalid(string parameter, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [parameter] = [message] });
}
