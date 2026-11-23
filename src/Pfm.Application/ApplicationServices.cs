using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pfm.Application.Accounts;
using Pfm.Application.Contracts.Accounts;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.Contracts.Transactions;
using Pfm.Application.ExchangeRates;
using Pfm.Application.Identity;
using Pfm.Application.Transactions;
using Pfm.Application.Transactions.Categories;
using Pfm.Application.Transactions.Transfers;
using Pfm.Application.UseCases;

namespace Pfm.Application;

public static class ApplicationServices
{
    /// <summary>
    /// Registers the use cases of every module: each <see cref="CommandHandler{TCommand, TOutcome}"/>,
    /// each <see cref="IQueryHandler"/> and each validator in this assembly. Called by the API and the worker.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(ApplicationServices).Assembly;

        services.TryAddSingleton(TimeProvider.System);
        services.AddValidatorsFromAssembly(assembly, ServiceLifetime.Scoped);

        foreach (var handler in assembly.GetTypes().Where(type => IsCommandHandler(type) || IsQueryHandler(type)))
        {
            services.AddScoped(handler);
        }

        // Contracts other modules call.
        services.AddScoped<IDefaultCategories, DefaultCategorySet>();
        services.AddScoped<IAccountLookup, AccountLookup>();
        services.AddScoped<IUserCalendar, UserCalendar>();
        services.AddScoped<TransactionContext>();
        services.AddScoped<TransferContext>();
        services.AddScoped<TransactionConversion>();
        services.AddScoped<ITransactionConversion>(provider => provider.GetRequiredService<TransactionConversion>());
        services.AddScoped<IBaseCurrency, CurrentUserBaseCurrency>();

        // Every module converts between currencies through this.
        services.AddScoped<ICurrencyConversion, CurrencyConversion>();

        return services;
    }

    private static bool IsQueryHandler(Type type) => type is { IsAbstract: false, IsInterface: false } && typeof(IQueryHandler).IsAssignableFrom(type);

    private static bool IsCommandHandler(Type type)
    {
        if (type.IsAbstract)
        {
            return false;
        }

        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(CommandHandler<,>))
            {
                return true;
            }
        }

        return false;
    }
}
