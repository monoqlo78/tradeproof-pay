using Azure;
using Azure.AI.OpenAI;
using HashKeyChain.Configuration;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace HashKeyChain.Services.Agent;

/// <summary>
/// Lazily provides a configured Azure OpenAI <see cref="ChatClient"/> shared by
/// the conversational agent and the OCR field-normalization step. Returns null
/// when the agent is not configured (no endpoint/key/deployment), so callers can
/// gracefully degrade instead of failing.
/// </summary>
public interface IChatClientProvider
{
    bool IsAvailable { get; }
    ChatClient? GetChatClient();
}

public sealed class ChatClientProvider : IChatClientProvider
{
    private readonly AgentOptions _options;
    private readonly ChatClient? _client;

    public ChatClientProvider(IOptions<AgentOptions> options)
    {
        _options = options.Value;
        if (_options.IsConfigured)
        {
            var azure = new AzureOpenAIClient(new Uri(_options.Endpoint), new AzureKeyCredential(_options.ApiKey));
            _client = azure.GetChatClient(_options.Deployment);
        }
    }

    public bool IsAvailable => _client is not null;

    public ChatClient? GetChatClient() => _client;
}
