using System.Text;

namespace CosmosVectorDemo;

/// <summary>Small console helpers so each step reads clearly on a projector.</summary>
public static class Ui
{
    public static void Heading(string text) => Write($"\n=== {text} ===\n", ConsoleColor.Cyan);

    public static void Sub(string text) => Write(text, ConsoleColor.Yellow);

    public static void Note(string text) => Write(text, ConsoleColor.Green);

    public static void Warn(string text) => Write(text, ConsoleColor.Red);

    public static string Wrap(string text, int width = 90)
    {
        var result = new StringBuilder();
        var line = new StringBuilder("  ");

        foreach (var word in text.Split(' '))
        {
            if (line.Length + word.Length + 1 > width)
            {
                result.AppendLine(line.ToString());
                line.Clear().Append("  ");
            }

            line.Append(word).Append(' ');
        }

        return result.Append(line).ToString();
    }

    /// <summary>Pressing Enter accepts the suggestion, so the presenter never has to type live.</summary>
    public static string Prompt(string label, string suggestion)
    {
        Console.WriteLine($"{label}\n  [Enter for: {suggestion}]");
        Console.Write("> ");
        var input = Console.ReadLine();
        return string.IsNullOrWhiteSpace(input) ? suggestion : input.Trim();
    }

    private static void Write(string text, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }
}
