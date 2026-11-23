using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pfm.Application.Abstractions;

namespace Pfm.Infrastructure.Storage;

internal static class StorageServices
{
    /// <summary>Private object storage, where attachments live.</summary>
    /// <param name="validateOnStart">Whether a host refuses to start with storage settings that cannot work.</param>
    public static IServiceCollection AddObjectStorage(this IServiceCollection services, bool validateOnStart)
    {
        var storage = services.AddOptions<StorageOptions>().BindConfiguration(StorageOptions.Section);
        services.AddSingleton<IValidateOptions<StorageOptions>, StorageOptionsValidator>();
        if (validateOnStart)
        {
            storage.ValidateOnStart();
        }

        services.AddSingleton<IObjectStorage, S3ObjectStorage>();
        return services;
    }

    private sealed class StorageOptionsValidator : IValidateOptions<StorageOptions>
    {
        public ValidateOptionsResult Validate(string? name, StorageOptions options) =>
            options.Problems().ToArray() is { Length: > 0 } problems ? ValidateOptionsResult.Fail(problems) : ValidateOptionsResult.Success;
    }
}
