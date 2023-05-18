using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using AI.Dev.OpenAI.GPT;

namespace GPThing;

public class GPT
{
    const    uint   TokenLimit = 4096;
    const    string ChatUrl    = "https://api.openai.com/v1/chat/completions";
    const    string Model      = "gpt-3.5-turbo";
    readonly Uri    _chatUri   = new(ChatUrl, UriKind.Absolute);

    List<API.Message> History { get; } = new();
    string            ApiKey  { get; }

    [PublicAPI] public string Prompt      { get; set; }
    [PublicAPI] public string SysPrompt   { get; set; }
    [PublicAPI] public string GirlName    { get; set; }
    [PublicAPI] public string YourName    { get; set; }
    [PublicAPI] public uint   MaxTokens   { get; set; }
    [PublicAPI] public double Temperature { get; set; }

    public GPT(string apiKey, string prompt, string sysPrompt, string girlName,
               string yourName, uint maxTokens, double temperature)
    {
        ApiKey      = apiKey;
        Prompt      = prompt;
        SysPrompt   = sysPrompt;
        GirlName    = girlName;
        YourName    = yourName;
        MaxTokens   = maxTokens;
        Temperature = temperature;

        History.Add(new API.Message(API.RoleType.System, SysPrompt));
    }

    //----------------------------------------------------------------------------------

    string MakeRequestBody(string content)
    {
        const RegexOptions options = RegexOptions.NonBacktracking | RegexOptions.IgnoreCase | RegexOptions.Multiline;
        const uint stupidOffset = 350;

        API.Request request;
        var         found = false;

        string message = Regex.Replace(Prompt, @"\$\{?Message\}?", _ => {
            found = true;
            return content;
        }, options);

        if (!found)
            message = Prompt + "\n" + content;

        var cur = new API.Message (
            API.RoleType.User,
            message
             //$"(role-play dialogue: {Prompt})\nI say \"{message}\".\n\n"
             //"(role-play conversation) " +
             //"(This dialogue should be sexual in nature)\n" +
             //$"Write a brief response to this prompt from the perspective of {GirlName}.\n\n{content}\n\n"
             //"(reply in English-US)",
        );

        while (true) {
            var mes       = new List<API.Message>(History) {cur};
            request       = new API.Request(mes, Model, MaxTokens, Temperature);
            string text   = request.messages.Aggregate("", (current, message) => current + message.content + "\n");
            var    tokens = GPT3Tokenizer.Encode(text);
            uint   limit  = TokenLimit/2 - MaxTokens;

            if ((uint)tokens.Count + stupidOffset < limit) {
                Console.Error.WriteLine($"Message is {tokens.Count} tokens in size.");
                break;
            }

            History.RemoveAt(1);
        }

        History.Add(new API.Message(API.RoleType.User, content));
        return request.Serialize();
    }

    async Task<string> PostAsync(HttpContent request)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response = await client.PostAsync(_chatUri, request);
        if (!response.IsSuccessStatusCode) {
            await Console.Error.WriteLineAsync(await response.Content.ReadAsStringAsync());
            await Console.Error.WriteLineAsync("Request failed with status code: " + response.StatusCode);
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

        if (Program.Debug)
            Console.Error.WriteLine($"--- Making request:\n{requestBody}\n");

        string        rawResponse = PostAsync(request).Result;
        API.Response? response    = API.Response.Deserialize(rawResponse);
        string        ret         = response?.choices[0].message.content ?? "";

        History.Add(new API.Message(API.RoleType.User, ret));
        return ret;
    }
}
