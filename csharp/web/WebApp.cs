#:sdk Microsoft.NET.Sdk.Web

namespace Dotfiles.Web;

public static class WebApp {
    public static WebApplicationBuilder CreateBuilder(string[] args) {
        var builder = WebApplication.CreateSlimBuilder(args);
        var isDevelopment = builder.Environment.IsDevelopment();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddEnvironmentVariables();
        builder.Configuration.AddEnvironmentVariables(prefix: "DOTNET_");
        builder.Configuration.AddEnvironmentVariables(prefix: "ASPNETCORE_");
        if (args is { Length: > 0 }) builder.Configuration.AddCommandLine(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
        if (isDevelopment)
            builder.Logging.AddConsole();
        else
            builder.Logging.AddJsonConsole();
        return builder;
    }
}
