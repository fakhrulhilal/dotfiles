#:include ../models/DbConfig.cs

#:sdk Microsoft.NET.Sdk.Web

using System.Data;
using Dotfiles.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Dotfiles.Web;

public sealed class DbHealthCheck(IOptions<DbConfig> options, IDbConnection db) : IHealthCheck {
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = new()) {
        var config = options.Value;
        try {
            const int expected = 1;
            var queryResult = GetTestResult(expected);
            var healthCheckResult = queryResult == expected
                ? HealthCheckResult.Healthy()
                : new HealthCheckResult(context.Registration.FailureStatus, "DB responds with unexpected result");
            return Task.FromResult(healthCheckResult);
        }
        catch (Exception exception) {
            return Task.FromResult(new HealthCheckResult(context.Registration.FailureStatus,
                $"Unable to get result from {config.Type} DB",
                exception));
        }
    }

    private int GetTestResult(int expected) {
        if (db.State != ConnectionState.Open)
            db.Open();
        using var command = db.CreateCommand();
        command.CommandText = $"SELECT {expected} AS Result";
        command.CommandType = CommandType.Text;
        using var reader = command.ExecuteReader();
        while (reader.Read()) return reader.GetInt32(0);

        return -1;
    }
}

public sealed class DbConfigValidator : IValidateOptions<DbConfig> {
    public ValidateOptionsResult Validate(string? name, DbConfig options) {
        if (!Enum.IsDefined(options.Type)) return ValidateOptionsResult.Fail("Invalid DB type");
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            return ValidateOptionsResult.Fail("DB connection string is required");

        return ValidateOptionsResult.Success;
    }
}
