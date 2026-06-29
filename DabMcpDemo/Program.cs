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

			Console.WriteLine("DAB MCP tools:");
			foreach (McpClientTool tool in tools)
			{
				Console.WriteLine($"- {tool.Name}");
			}

			Console.WriteLine();

			List<ChatMessage> messages =
			[
				new(ChatRole.System, """

					You are a library assistant.

					Before querying an entity for the first time in a conversation, use describe_entities to inspect the available entities and fields.

					After you understand the schema, remember it for the remainder of the conversation.

					Use these field mappings when interpreting user requests:

					Book:
					- "book", "title", "book title", and "book name" all refer to Book.Title.
					- "page count", "pages", and "number of pages" refer to Book.Pages.
					- "year", "publication year", "published", and "published year" refer to Book.Year.

					Author:
					- "author", "author name", and "writer" refer to the combination of Author.FirstName, Author.MiddleName, and Author.LastName.
					
					If the user asks for books written by a specific author, use the BookDetail entity first. 
					BookDetail exposes book data with aggregated author names in the Authors field.

					Use BookDetail.Authors to match author names such as "Isaac Asimov".
					Use BookDetail.Title for the book name.
					Use BookDetail.Pages for page count.
					Use BookDetail.Year for publication year.

					Do not try to join Book and Author directly with read_records.
					DAB MCP read_records cannot perform relationship traversal or joins.
				
					When using read_records, request enough rows to answer the question completely.
					If the result may be paged or limited, continue retrieving additional records until all relevant records have been checked.

					For co-written book questions, use execute_entity with GetBooksCowrittenByAuthor.

					Use read_records for tables and views.
					Use execute_entity for stored procedure entities.
					The stored procedure entity is GetBooksCowrittenByAuthor.

					Do not invent data.
					Explain answers clearly and briefly.

				""")
			];

			while (true)
			{
				Console.Write("> ");
				var prompt = Console.ReadLine();

				if (string.IsNullOrWhiteSpace(prompt))
				{
					break;
				}

				messages.Add(new(ChatRole.User, prompt));

				var updates = new List<ChatResponseUpdate>();

				var chatOptions = new ChatOptions
				{
					Tools = []
				};

				foreach (var tool in tools)
				{
					chatOptions.Tools.Add(tool);
				}

				await foreach (var update in chatClient.GetStreamingResponseAsync(messages, chatOptions))
				{
					Console.Write(update);
					updates.Add(update);
				}

				Console.WriteLine();
				messages.AddMessages(updates);
			}
		}

	}
}
