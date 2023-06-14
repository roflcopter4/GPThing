using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AI.Dev.OpenAI.GPT;
using JetBrains.Annotations;

namespace GPThing;

public partial class GPT
{
    [UriString]
    const string ChatUrl          = "https://api.openai.com/v1/chat/completions";
    const string Model            = "gpt-3.5-turbo";
    const uint   TokenLimit       = 4096 * 2 / 3;
    const uint   DefaultMaxTokens = 256;
    const double DefaultTemp      = 1.0;
    const string DefaultGirlName  = "Emily";
    const string DefaultUserName  = "Jack";
    const string DefaultPrompt    = "";
    const string DefaultSysPrompt = 
        "(This dialogue should be sexual in nature)\n" +
        "Write a brief response to this prompt from the perspective of ${GirlName}.\n\n" +
        "${Message}\n\n.";

    readonly string            _apiKey;
    readonly Uri               _chatUri = new(ChatUrl, UriKind.Absolute);
    readonly List<API.Message> _history = new();

    public System.IO.StreamWriter? DebugLog { private get; init; }

    [PublicAPI] public string Prompt           { get; set; } = DefaultPrompt;
    [PublicAPI] public string SysPrompt        { get; set; } = DefaultSysPrompt;
    [PublicAPI] public string GirlName         { get; set; } = DefaultGirlName;
    [PublicAPI] public string UserName         { get; set; } = DefaultUserName;
    [PublicAPI] public uint   MaxTokens        { get; set; } = DefaultMaxTokens;
    [PublicAPI] public double Temperature      { get; set; } = DefaultTemp;
    [PublicAPI] public double FrequencyPenalty { get; set; } = 1.0;
    [PublicAPI] public double PresencePenalty  { get; set; } = 1.0;
    [PublicAPI] public double TopP             { get; set; } = 1.0;

    //----------------------------------------------------------------------------------

    public GPT(string  apiKey,
               string? prompt = null,
               string? sysPrompt = null,
               string? girlName = null,
               string? yourName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(apiKey, nameof(apiKey));

        if (!string.IsNullOrWhiteSpace(girlName))
            GirlName = girlName;
        if (!string.IsNullOrWhiteSpace(yourName))
            UserName = yourName;

        _apiKey   = apiKey;
        Prompt    = ReplaceInPrompt(!string.IsNullOrWhiteSpace(prompt) ? prompt : Prompt);
        SysPrompt = ReplaceInPrompt(!string.IsNullOrWhiteSpace(sysPrompt) ? sysPrompt : SysPrompt);

        _history.Add(new API.Message(API.RoleType.System, SysPrompt));
    }

    //----------------------------------------------------------------------------------

    static partial class PromptRegexes
    {
        const RegexOptions Options = RegexOptions.IgnoreCase;

        [GeneratedRegex(@"(?<!\\)\$(?:\{Prompt\}|Prompt\b)", Options)]
        public static partial Regex Prompt();

        [GeneratedRegex(@"(?<!\\)\$(?:\{GirlName\}|GirlName\b)", Options)]
        public static partial Regex GirlName();

        [GeneratedRegex(@"(?<!\\)\$(?:\{UserName\}|UserName\b)", Options)]
        public static partial Regex UserName();
    }

    string ReplaceInPrompt(string prompt)
    {
        prompt = PromptRegexes.Prompt()  .Replace(prompt, Prompt);
        prompt = PromptRegexes.GirlName().Replace(prompt, GirlName);
        prompt = PromptRegexes.UserName().Replace(prompt, UserName);
        return prompt;
    }

    static bool GetAgain()
    {
        Console.Error.Write("Retry? (y/n): ");
        while (true) {
            int c = Console.In.Read();
            Console.In.ReadLine();
            switch (c) {
            case 'y' or 'Y':
                return true;
            case 'n' or 'N':
                return false;
            default:
                Console.Error.Write("Please specify either 'y' or 'n': ");
                break;
            }
        }
    }

    //----------------------------------------------------------------------------------

    string MakeRequestBody(string content)
    {
        const RegexOptions options      = RegexOptions.IgnoreCase | RegexOptions.NonBacktracking; 
        const uint         stupidOffset = 350;
        API.Request request;

        var    found   = false;
        string message = Regex.Replace(Prompt, @"\$\{?Message\}?", _ => {
            found = true;
            return content;
        }, options);
        if (!found)
            message = $"{Prompt}\n{content}\n\n";

        var cur = new API.Message(API.RoleType.User, message);

        while (true) {
            var mes       = new List<API.Message>(_history) {cur};
            request       = new API.Request(mes, Model, MaxTokens, Temperature, TopP);
            string text   = request.messages.Aggregate("", (s, m) => s + m.content + "\n");
            var    tokens = GPT3Tokenizer.Encode(text);
            uint   limit  = TokenLimit - MaxTokens;

            if ((uint)tokens.Count + stupidOffset < limit) {
                if (Program.Debug)
                    Console.Error.WriteLine($"Message is {tokens.Count} tokens in size.");
                break;
            }

            _history.RemoveAt(1);
        }

        _history.Add(new API.Message(API.RoleType.User, content));
        return request.Serialize();
    }

    async Task<string> PostAsync(HttpContent request)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

    again:
        HttpResponseMessage response = await client.PostAsync(_chatUri, request);
        if (!response.IsSuccessStatusCode) {
            await Console.Error.WriteLineAsync(await response.Content.ReadAsStringAsync());
            await Console.Error.WriteLineAsync($"Request failed with status code: {response.StatusCode}");
            if (GetAgain())
                goto again;
            return "";
        }

        return await response.Content.ReadAsStringAsync();
    }

    //----------------------------------------------------------------------------------

    [PublicAPI]
    public string Post(string message)
    {
        string requestBody = MakeRequestBody(message);
        var    request     = new StringContent(requestBody, Encoding.UTF8, "application/json");
        string rawResponse;

        DebugLog?.Write($"--- Making request:\n{requestBody}\n");

    again:
        try {
            rawResponse = PostAsync(request).Result;
        } 
        catch (AggregateException e) {
            Console.Error.WriteLine($"HTTP request failed (likely timeout): {e.HResult} \"{e}\"");
            if (GetAgain())
                goto again;
            return "";
        }

        API.Response? response = API.Response.Deserialize(rawResponse);
        string        ret      = response?.choices[0].message.content ?? "";
        _history.Add(new API.Message(API.RoleType.Assistant, ret));

        if (DebugLog is not null) {
            DebugLog.Write($"--- Received response:\n{rawResponse}\n");
            DebugLog.Flush();
        }

        return ret;
    }

    [PublicAPI]
    public void ClearHistoryAndUpdatePrompts()
    {
        Prompt    = ReplaceInPrompt(Prompt);
        SysPrompt = ReplaceInPrompt(SysPrompt);
        _history.Clear();
        _history.Add(new API.Message(API.RoleType.System, SysPrompt));
    }
}
