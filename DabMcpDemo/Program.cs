using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DataApiBuilderDemos
{
	internal class Program
	{
		private static string SystemPrompt1 = """

			You are a library assistant.
				
			Before querying an entity for the first time in a conversation, use describe_entities to inspect the available entities and fields.
				
			After you understand the schema, remember it for the remainder of the conversation.
				
			Use read_records for tables and views.
			Use execute_entity for stored procedure entities.
			Do not invent data.
			Explain answers clearly and briefly.
			Number each book in the list of books you return, and include page count and year.
			If the result may be paged or limited, continue retrieving additional records until all relevant records have been checked.
		
			Use these field mappings when interpreting user requests:
				
			Book:
			- "book", "title", "book title", and "book name" all refer to Book.Title.
			- "page count", "pages", and "number of pages" refer to Book.Pages.
			- "year", "publication year", "published", and "published year" refer to Book.Year.
				
			Author:
			- "author", "author name", and "writer" refer to the combination of Author.FirstName, Author.MiddleName, and Author.LastName.
					
			If the user asks for books written by a specific author, use the BookDetail entity first. 
			BookDetail exposes book data with comma-separated aggregated author names in the Authors field.
				
			BookDetail:
			- Use BookDetail.Authors to match author names such as "Isaac Asimov".
			- Use BookDetail.Title for the book name.
			- Use BookDetail.Pages for page count.
			- Use BookDetail.Year for publication year.
				
			Do not try to join Book and Author directly with read_records.
			DAB MCP read_records cannot perform relationship traversal or joins.
				
			When using read_records, request enough rows to answer the question completely.
			If the result may be paged or limited, continue retrieving additional records until all relevant records have been checked.
			
			If the user asks for books co-written by a specific author, use execute_entity with GetBooksCowrittenByAuthor stored procedure first.
		
			GetBooksCowrittenByAuthor: 
			- Accepts a SearchType parameter and an Author parameter.
			- Set SearchType to either "C" (for "contains") or "S" (for "starts with").
			- The Author parameter should be set to the author name provided by the user.

		""";

		private static string SystemPrompt2 = """

			Beneath each answer, draw a line and then provide a brief explanation of how you arrived at the answer, including which entity
			and fields were used to retrieve the data. Describe your process as numbered steps.
			
		""";

		private static readonly string[] AutoQuestions =
		[
			"How many books are there in the library?",
			"What books were written by just a single author?",
			"What books were co-written by two or more authors?",
			"Show me the books written by Isaac Asimov",
			"Show me the books written by Isaac Asimov, with no other co-authors",
			"Show me the books co-written by an author whose name contains Asimov",
			"Show me the books co-written by an author whose name starts with Asimov",
			"What books do you have that were published in 2020 or later?",
			"I'm looking for books that were co-written by two or more authors, and published in 2020 or later.",
			"Show me your sci-fi books.",
			"Show me your database books."
		];

		static async Task Main(string[] args)
		{
			var configuration = new ConfigurationBuilder()
				.AddUserSecrets<Program>()
				.Build();

			var openAiHostName = configuration["AzureOpenAI:HostName"];
			var openAiApiKey = configuration["AzureOpenAI:ApiKey"];
			var openAiDeploymentName = configuration["AzureOpenAI:DeploymentName"];

			var endpoint = new Uri($"https://{openAiHostName}.openai.azure.com/");
			var credential = new AzureKeyCredential(openAiApiKey);
			var azureOpenAiClient = new AzureOpenAIClient(endpoint, credential);
			var openAiChatClient = azureOpenAiClient.GetChatClient(openAiDeploymentName);

			var chatClient = new ChatClientBuilder(openAiChatClient.AsIChatClient())
				.UseFunctionInvocation()
				.Build();

			var transport = new HttpClientTransport(new HttpClientTransportOptions
			{
				Name = "DAB Library MCP",
				Endpoint = new Uri("http://localhost:5000/mcp"),
				TransportMode = HttpTransportMode.StreamableHttp
			});

			await using McpClient mcpClient = await McpClient.CreateAsync(transport);

			var tools = await mcpClient.ListToolsAsync();

			Console.WriteLine("I am a library assistant. I can answer questions about the books and authors in the library.");
			Console.WriteLine();
			Console.WriteLine("My knowledge is based on a library database using these MCP Tools exposed by Data API Builder:");
			foreach (McpClientTool tool in tools)
			{
				Console.WriteLine($"- {tool.Name}");
			}

			Console.WriteLine();

			var chatOptions = new ChatOptions
			{
				Tools = []
			};

			foreach (var tool in tools)
			{
				chatOptions.Tools.Add(tool);
			}

			var messages = new List<ChatMessage>
			{
				new(ChatRole.System, SystemPrompt1),
//				new(ChatRole.System, SystemPrompt2),
			};

			var autoQuestionIndex = 0;

			while (true)
			{
				Console.WriteLine();
				Console.Write("[M]anual, [A]uto, [ESC] Exit > ");

				var key = Console.ReadKey(intercept: true);
				Console.WriteLine();

				var prompt = default(string);

				if (key.Key == ConsoleKey.Escape)
				{
					break;
				}
				else if (key.Key == ConsoleKey.M)
				{
					Console.WriteLine();
					Console.Write("> ");
					Console.ForegroundColor = ConsoleColor.Cyan;
					prompt = Console.ReadLine();
					Console.ResetColor();

					if (string.IsNullOrWhiteSpace(prompt))
					{
						continue;
					}
				}
				else if (key.Key == ConsoleKey.A)
				{
					if (autoQuestionIndex >= AutoQuestions.Length)
					{
						Console.WriteLine("No more auto-questions");
						break;
					}

					prompt = AutoQuestions[autoQuestionIndex++];

					Console.ForegroundColor = ConsoleColor.Cyan;
					Console.WriteLine();
					Console.WriteLine(prompt);
					Console.ResetColor();
				}
				else
				{
					continue;
				}

				messages.Add(new(ChatRole.User, prompt));

				var updates = new List<ChatResponseUpdate>();

				Console.WriteLine();
				Console.ForegroundColor = ConsoleColor.Yellow;
				await foreach (var update in chatClient.GetStreamingResponseAsync(messages, chatOptions))
				{
					Console.Write(update);
					updates.Add(update);
				}
				Console.ResetColor();

				Console.WriteLine();
				messages.AddMessages(updates);
			}

		}

	}
}
