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
		private static readonly string LibraryAssistantMinimalPrompt = """

			You are a library assistant.
			You can answer questions about the books and authors in the library.
			Explain answers clearly and briefly.
			Do not invent data.
			  Exception: You may answer genre-related questions even though the library database has no
			  genre information. However, you must clearly state that the answer is based on general
			  knowledge and not the library database.
		
			Do not guess at schema (i.e., don't try to figure out plausible entity names and field names).
			Call the describe_entities tool to discover schema
			Call the read_records tool to retrieve data from table and view entities 
			Call the aggregate_records tool to count entities such as books and authors
			Call the execute_entity tool to retrieve data from stored procedure entities 
			Do not call read_records or execute_entity unless you are sure that the entity and fields exist

		""";

		private static readonly string LibraryAssistantVerbosePrompt = """

			You are a library assistant.
			You can answer questions about the books and authors in the library.
			Explain answers clearly and briefly.
			Do not invent data.
			  Exception: You may answer genre-related questions even though the library database has no
			  genre information. However, you must clearly state that the answer is based on general
			  knowledge and not the library database.
		
			Do not guess at schema (i.e., don't try to figure out plausible entity names and field names).
			Call the describe_entities tool to discover schema
			Call the read_records tool to retrieve data from table and view entities 
			Call the aggregate_records tool to count entities such as books and authors
			Call the execute_entity tool to retrieve data from stored procedure entities 
			Do not call read_records or execute_entity unless you are sure that the entity and fields exist
				
			Before querying an entity for the first time in a conversation, use describe_entities to inspect the available entities and fields.
				
			After you understand the schema, remember it for the remainder of the conversation.
				
			Use the read_records MCP tool for tables and views.
			Use the execute_entity MCP tool for stored procedure entities.
			Number each book in the list of books you return, and include page count and year.
		
			Use these Data API Builder entities and field mappings when interpreting user requests:
				
			Book entity:
			- Use Book.Title for questions relating to "book", "title", "book title", and "book name".
			- Use Book.Pages for questions about "page count", "pages", and "number of pages".
			- Use Book.Year for questions concerning "year", "publication year", "published", and "published year".
				
			If the user asks for books written by a specific author, or if they ask any question related to number of authors,
			use the BookDetail entity first. BookDetail exposes book data with author count and comma-separated aggregated author names in
			the Authors field.
				
			BookDetail entity:
			- Use BookDetail.Authors to match author names such as "Isaac Asimov".
			- Use BookDetail.AuthorCount to determine the number of authors of each book.
			- Use BookDetail.Title for the book name.
			- Use BookDetail.Pages for page count.
			- Use BookDetail.Year for publication year.
				
			If the user asks for books co-written by a specific author, use execute_entity with GetBooksCowrittenByAuthor stored procedure first.
		
			GetBooksCowrittenByAuthor: 
			- Accepts a SearchType parameter and an Author parameter.
			- Set SearchType to either "C" (for "contains") or "S" (for "starts with").
			- The Author parameter should be set to the author name provided by the user.

		""";

		private static readonly string ExplainProcessPrompt = """

			Beneath each answer, draw a line and then provide a brief explanation of how you arrived at the answer,
			including which entity and fields were used to retrieve the data. Describe your process as numbered
			steps, including the raw JSON for requests and responses for all invoked MCP tools.
			
		""";

		private static readonly string[] AutoQuestions =
		[
			"How many books are there in the library?",
			"How many distinct authors are there in the library?",
			"What books were written by just a single author?",
			"What books were co-written by two or more authors?",
			"Show me the books written or co-written by Isaac Asimov.",
			"Show me the books written by Isaac Asimov, with no other co-authors.",
			"Show me the books co-written by an author whose name contains Asimov.",
			"Show me the books co-written by an author whose name starts with Asimov.",
			"What books do you have that were published in 2020 or later?",
			"I'm looking for books that were co-written by two or more authors, and published in 2020 or later.",
			"Show me your sci-fi books.",
			"Show me your database books.",
			"Show me books published in the UK.",
		];

		static async Task Main(string[] args)
		{
			// Retrieve the application's Azure OpenAI configuration

			var configuration = new ConfigurationBuilder()
				.AddUserSecrets<Program>()
				.Build();

			var openAiHostName = configuration["AzureOpenAI:HostName"];
			var openAiApiKey = configuration["AzureOpenAI:ApiKey"];
			var openAiDeploymentName = configuration["AzureOpenAI:DeploymentName"];

			// Create a low-level chat client that communicates directly with Azure OpenAI

			var endpoint = new Uri($"https://{openAiHostName}.openai.azure.com/");
			var credential = new AzureKeyCredential(openAiApiKey);
			var azureOpenAiClient = new AzureOpenAIClient(endpoint, credential);

			var openAiChatClient = azureOpenAiClient.GetChatClient(openAiDeploymentName);

			// Wrap the Azure OpenAI chat client with middleware that enables automatic MCP tool invocation

			var chatClient = new ChatClientBuilder(openAiChatClient.AsIChatClient())
				.UseFunctionInvocation()    // Enables automatic execution of MCP tool calls requested by the AI model
				.Build();

			// Establish a connection to the MCP endpoint exposed by Data API Builder

			var transport = new HttpClientTransport(new HttpClientTransportOptions
			{
				Endpoint = new Uri("http://localhost:5000/mcp"),
				TransportMode = HttpTransportMode.StreamableHttp
			});

			await using var mcpClient = await McpClient.CreateAsync(transport);

			// Get the available tools from the MCP endpoint and register as chat options for the AI model

			var tools = await mcpClient.ListToolsAsync();

			var chatOptions = new ChatOptions
			{
				Tools = []
			};

			foreach (var tool in tools)
			{
				chatOptions.Tools.Add(tool);
			}

			// Configure the chat client with the library assistant prompt(s)

			var messages = new List<ChatMessage>
			{
				new(ChatRole.System, LibraryAssistantMinimalPrompt),
//				new(ChatRole.System, LibraryAssistantVerbosePrompt),
//				new(ChatRole.System, ExplainProcessPrompt),
			};

			// Display the library assistant prompt(s)

			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine("I am a library assistant. I can answer questions about the books and authors in the library.");
			Console.WriteLine("My knowledge is based on a library database using MCP Tools exposed by Data API Builder, and these prompts:");
			Console.ForegroundColor = ConsoleColor.White;
			foreach (var message in messages)
			{
				Console.WriteLine(" ┌── " + message.Text.Replace("\r\n", "\r\n │ ").Replace("\t", string.Empty));
				Console.WriteLine(" └── ");
			}

			// Start the chat loop to accept user questions and provide answers

			var autoQuestionIndex = 0;

			while (true)
			{
				Console.ResetColor();
				Console.WriteLine();
				Console.Write("[A] = Auto / [M] = Manual / [Q] = Quit: ");

				var key = Console.ReadKey();
				Console.WriteLine();

				var prompt = default(string);

				// Obtain the user prompt (either auto or manual), or exit the loop if the user chooses to quit

				if (key.Key == ConsoleKey.A)
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
				}
				else if (key.Key == ConsoleKey.M)
				{
					Console.WriteLine();
					Console.Write("> ");
					Console.ForegroundColor = ConsoleColor.Cyan;
					prompt = Console.ReadLine();

					if (string.IsNullOrWhiteSpace(prompt))
					{
						continue;
					}
				}
				else if (key.Key == ConsoleKey.Q)
				{
					break;
				}
				else
				{
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine("Invalid option. Please select A, M, or Q.");
					continue;
				}

				// Accumulate the user prompt into the chat messages

				messages.Add(new(ChatRole.User, prompt));

				// Collect the streamed response chunks so the complete assistant reply can be added back to the conversation history

				var updates = new List<ChatResponseUpdate>();

				// Stream the assistant response as it is generated, including any MCP tool calls automatically invoked by the chat client

				Console.WriteLine();
				Console.ForegroundColor = ConsoleColor.Yellow;
				await foreach (var update in chatClient.GetStreamingResponseAsync(messages, chatOptions))
				{
					Console.Write(update);
					updates.Add(update);
				}
				Console.ResetColor();
				Console.WriteLine();

				// Add the assistant's full streamed response to the message history so future turns retain conversation context

				messages.AddMessages(updates);
			}

		}

	}
}
