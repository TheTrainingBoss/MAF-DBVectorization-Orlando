using Microsoft.Extensions.Configuration;

namespace AzureAISearchVectorDemo;

/// <summary>Values come from dotnet user-secrets or environment variables, never from source code.</summary>
public record Settings(
    Uri SearchEndpoint,
    string? SearchKey,
    string IndexName,
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

        var searchEndpoint = new Uri(Required("Search:Endpoint"));
        if (searchEndpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "Search:Endpoint must start with https://, for example https://YOUR-SERVICE.search.windows.net.");
        }

        return new Settings(
            SearchEndpoint: searchEndpoint,
            SearchKey: config["Search:ApiKey"],
            IndexName: config["Search:IndexName"] ?? "products",
            AzureOpenAiEndpoint: ToV1Endpoint(Required("AzureOpenAI:Endpoint")),
            AzureOpenAiKey: config["AzureOpenAI:ApiKey"],
            EmbeddingDeployment: config["AzureOpenAI:EmbeddingDeployment"] ?? "text-embedding-3-small",
            ChatDeployment: config["AzureOpenAI:ChatDeployment"] ?? "gpt-6-luna");
    }

    // Accepts the resource endpoint or a pasted Foundry project URL: only the host matters.
    private static Uri ToV1Endpoint(string endpoint) =>
        new($"{new Uri(endpoint).GetLeftPart(UriPartial.Authority)}/openai/v1/");
}
