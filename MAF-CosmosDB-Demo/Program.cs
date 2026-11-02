using System.Text;
using CosmosVectorDemo;

Console.OutputEncoding = Encoding.UTF8;

const string Menu = """

    ------------ Cosmos DB vectorization demo ------------
     1  Show the source JSON
     2  Create the container with a vector policy
     3  Import the documents (no vectors yet)
     4  Keyword search - the "before" picture
     5  Vectorize the documents
     6  Inspect a vector
     7  Compare keyword search and vector search
     8  Vector search with a price filter
     9  Ask the agent about the data
     N  Start a new agent conversation
     R  Reset (delete the container)
     Q  Quit
    -------------------------------------------------------
    """;

Settings settings;
try
{
    settings = Settings.Load();
}
catch (InvalidOperationException e)
{
    Ui.Warn(e.Message);
    return 1;
}

using var demo = new Demo(settings);

// Each visit to a step moves on to its next sample, so pressing Enter walks through a good demo.
var searches = new Samples(
    "something to keep my feet dry on a rainy walk",
    "I get cold at night when camping in winter",
    "how do I make a hot meal far from any town",
    "necesito algo para purificar agua en la montaña",
    "how can I call for help when I have no phone signal");

var questions = new Samples(
    "Which tent should I buy for a stormy mountain trip, and what is its warranty?",
    "How much does that one weigh?",
    "What is the best shelter for two if my budget is $300?",
    "I'm planning a two-week winter trek. Which sleeping bag and pack do you recommend, and what do they cost?",
    "Can your water filter make water safe from viruses?",
    "Do you sell surfboards?");

while (true)
{
    Console.WriteLine(Menu);
    Console.Write("Choose a step: ");
    var choice = Console.ReadLine()?.Trim().ToUpperInvariant();

    try
    {
        switch (choice)
        {
            case "1":
                await demo.ShowSourceDataAsync();
                break;
            case "2":
                await demo.CreateContainerAsync();
                break;
            case "3":
                await demo.ImportDocumentsAsync();
                break;
            case "4":
                await demo.KeywordSearchAsync(Ui.Prompt("Search for:", searches.Next()));
                break;
            case "5":
                await demo.VectorizeAsync();
                break;
            case "6":
                await demo.InspectVectorAsync();
                break;
            case "7":
                await demo.CompareSearchAsync(Ui.Prompt("Search for:", searches.Next()));
                break;
            case "8":
                var query = Ui.Prompt("Search for:", "a shelter for two people");
                var price = Ui.Prompt("Maximum price:", "300");
                await demo.FilteredVectorSearchAsync(query, decimal.Parse(price));
                break;
            case "9":
                var question = Ui.Prompt("Your question:", questions.Next());
                var compare = Ui.Prompt("Also ask the model without your data first? (y/n)", "y");
                await demo.AskAsync(question, compare.StartsWith('y'));
                break;
            case "N":
                demo.StartNewConversation();
                break;
            case "R":
                if (Ui.Prompt("Delete the demo container and all its documents? (y/n)", "n").StartsWith('y'))
                {
                    await demo.ResetAsync();
                }

                break;
            case "Q" or null:
                return 0;
            default:
                Ui.Warn("Unknown option.");
                break;
        }
    }
    catch (Exception e) when (e is not OutOfMemoryException)
    {
        // Keep the menu alive on stage so the presenter can fix the issue and retry.
        Ui.Warn($"{e.GetType().Name}: {e.Message}");
    }
}

internal sealed class Samples(params string[] values)
{
    private int _next;

    public string Next() => values[_next++ % values.Length];
}
