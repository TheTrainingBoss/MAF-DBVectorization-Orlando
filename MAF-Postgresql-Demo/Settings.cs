using Microsoft.Extensions.Configuration;

namespace PostgresVectorDemo;

/// <summary>Values come from dotnet user-secrets or environment variables, never from source code.</summary>
public record Settings(
    string PostgresHost,
    int PostgresPort,
    string PostgresUsername,
    string PostgresPassword,
    string DatabaseName,
    Uri AzureOpenAiEndpoint,
    string? AzureOpenAiKey,
    string EmbeddingDeployment,
    string ChatDeployment)
{
    public const int EmbeddingDimensions = 1536; // Native size of text-embedding-3-small.

    public static Settings Load()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets<Settings>()
            .AddEnvironmentVariables()
            .Build();

        string Required(string key) => config[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"Missing setting '{key}'. Run: dotnet user-secrets set \"{key}\" \"<value>\" (see README step 4).");

        return new Settings(
            PostgresHost: config["Postgres:Host"] ?? "localhost",
            PostgresPort: int.Parse(config["Postgres:Port"] ?? "5432"),
            PostgresUsername: config["Postgres:Username"] ?? "postgres",
            PostgresPassword: Required("Postgres:Password"),
            DatabaseName: config["Postgres:Database"] ?? "vectordemo",
            AzureOpenAiEndpoint: ToV1Endpoint(Required("AzureOpenAI:Endpoint")),
            AzureOpenAiKey: config["AzureOpenAI:ApiKey"],
            EmbeddingDeployment: config["AzureOpenAI:EmbeddingDeployment"] ?? "text-embedding-3-small",
            ChatDeployment: config["AzureOpenAI:ChatDeployment"] ?? "gpt-6-luna");
    }

    // Accepts the resource endpoint or a pasted Foundry project URL: only the host matters.
    private static Uri ToV1Endpoint(string endpoint) =>
        new($"{new Uri(endpoint).GetLeftPart(UriPartial.Authority)}/openai/v1/");
}
