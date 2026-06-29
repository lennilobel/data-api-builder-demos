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

					Use the available MCP tools to answer questions from the Library database.

					Important:
					- Use read_records for tables and views.
					- Use execute_entity for stored procedure entities.
					- The stored procedure entity is GetBooksCowrittenByAuthor.
					- Do not invent data.
					- Explain answers clearly and briefly.
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
