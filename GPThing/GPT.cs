using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System;
using JetBrains.Annotations;

namespace GPThing;

using Dict = Dictionary<string, string>;

public class GPT
{
    private const    string ChatUrl  = "https://api.openai.com/v1/chat/completions";
    private const    string Model    = "gpt-3.5-turbo";
    private readonly Uri    _chatUri = new(ChatUrl, UriKind.Absolute);

    private List<Dict> History { get; set; } = new();
    private string     ApiKey  { get; }

    [PublicAPI] public string Prompt      { get; set; }
    [PublicAPI] public string Name        { get; set; }
    [PublicAPI] public uint   MaxTokens   { get; set; }
    [PublicAPI] public double Temperature { get; set; }

    public GPT(string apiKey, string prompt, string name, uint maxTokens, double temperature)
    {
        ApiKey      = apiKey;
        Prompt      = prompt;
        Name        = name;
        MaxTokens   = maxTokens;
        Temperature = temperature;

        History.Add(new Dict { {"role", "system"}, {"content", Prompt} });
    }

    //----------------------------------------------------------------------------------

    [Serializable, UsedImplicitly(ImplicitUseTargetFlags.Members)]
    private struct MessageData
    {
        // ReSharper disable InconsistentNaming
        public string model;
        public uint   max_tokens;
        public double temperature;
        public double frequency_penalty;
        public double presence_penalty;
        public int    top_p;
        public Dict[] messages;
        // ReSharper restore InconsistentNaming

        public MessageData(IEnumerable<Dict> messages,
                           uint              maxTokens,
                           double            temp,
                           double            frequencyPenalty = 1.0,
                           double            presencePenalty  = 1.0,
                           int               topP             = 1)
        {
            model             = Model;
            max_tokens        = maxTokens;
            temperature       = temp;
            frequency_penalty = frequencyPenalty;
            presence_penalty  = presencePenalty;
            top_p             = topP;

            if (messages == null)
                throw new ArgumentNullException(nameof(messages));
            this.messages = messages as Dict[] ?? throw new InvalidCastException();
        }
    }

    private string MakeRequestBody(string message)
    {
        string ret;

        while (true) {
            var cur = new Dict {
                {"role", "user"},
                {"content",
                 $"(role-play conversation: {Prompt})\n\n" +
                 $"As {Name} create a reply this message of mine to continue the conversation: \"{message}\"\n\n" +
                 "(reply in English-US)"},
            };
            var mes  = new List<Dict>(History) {cur};
            var data = new MessageData(mes.ToArray(), MaxTokens, Temperature);
            ret      = Newtonsoft.Json.JsonConvert.SerializeObject(data, Newtonsoft.Json.Formatting.None);

            if (ret.Length >= 4096 - MaxTokens) {
                var tmp = new List<Dict> {History[0]};
                tmp.AddRange(History.GetRange(2, History.Count - 1));
                History = tmp;
                continue;
            }
            break;
        }

        History.Add(new Dict { {"role", "user"}, {"content", message} });
        return ret;
    }

    //----------------------------------------------------------------------------------

    private async Task<string> PostAsync(string message)
    {
        using var client = new HttpClient();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        string requestBody    = MakeRequestBody(message);
        var    requestContent = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");

        if (Program.Debug)
            Console.Error.WriteLine($"--- Making request:\n{requestBody}\n");

        HttpResponseMessage response = await client.PostAsync(_chatUri, requestContent);

        if (!response.IsSuccessStatusCode) {
            Console.Error.WriteLine(await response.Content.ReadAsStringAsync());
            Console.Error.WriteLine("Request failed with status code: " + response.StatusCode);
            return "";
        }

        return await response.Content.ReadAsStringAsync();
    }

    [PublicAPI]
    public string Post(string message)
    {
        string rawResponse = PostAsync(message).Result;
        var    response    = Newtonsoft.Json.JsonConvert.DeserializeObject<JsonClass.Rootobject>(rawResponse);
        string ret         = response?.choices[0].message.content ?? "";
        History.Add(new Dict{ {"role", "assistant"}, {"content", ret} });
        return ret;
    }
}
