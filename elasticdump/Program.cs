using System.CommandLine;

namespace elasticdump;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var urlArgument = new Argument<string>(
            "url",
            "Elasticsearch Url"
        );
        var usernameArgument = new Argument<string>(
            "username",
            "Elasticsearch username"
        );
        var passwordArgument = new Argument<string>(
            "password",
            "Elasticsearch password"
        );
        var destinationDirectoryArgument = new Argument<string>(
            "out-dir",
            "Output directory"
        );
        var indiceArgument = new Argument<string>(
            "indice",
            "Elasticsearch indice"
        );


        var rootCommand = new RootCommand("Creates a .NET solution file and adds projects to it.");

        rootCommand.Description = "This program creates a .NET solution file, adds all .csproj files found in the specified directories to the solution, and replaces DLL references with project references where applicable.";
        rootCommand.AddArgument(urlArgument);
        rootCommand.AddArgument(usernameArgument);
        rootCommand.AddArgument(passwordArgument);
        rootCommand.AddArgument(destinationDirectoryArgument);
        rootCommand.AddArgument(indiceArgument);

        rootCommand.SetHandler((url, username, password, destinationDirectory, indice) =>
        {
            RunProgram(url, username, password, destinationDirectory, indice);
        },
            urlArgument,
            usernameArgument,
            passwordArgument,
            destinationDirectoryArgument,
            indiceArgument
        );

        return await rootCommand.InvokeAsync(args);
    }

    static async Task RunProgram(string url, string username, string password, string destinationDirectory, string indice)
    {
        if (string.IsNullOrEmpty(url))
        {
            Console.WriteLine("url not found or empty.");
        }

        if (string.IsNullOrEmpty(username))
        {
            Console.WriteLine("username not found or empty.");
        }

        ElasticDump elasticDump = new ElasticDump(url, username, password, destinationDirectory);
        await elasticDump.Dump(
            index: indice,
            rowPerRequest: 10000
        );
    }
}
