using System;
using System.IO;
using System.Reflection;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using System.Linq;
using JetBrains.Annotations;

#pragma warning disable CS0649

namespace GPThing;

internal static class Program
{
    private const double DefaultTemperature = 1.06;
    private const uint   DefaultMaxTokens   = 512;
    private const string DefaultName        = "Emily";
    private const string DefaultPrompt      = "Act like you are a human girl named $NAME.";

    private static double  _temperature = DefaultTemperature;
    private static uint    _maxTokens   = DefaultMaxTokens;
    private static string  _prompt      = DefaultPrompt;
    private static string  _name        = DefaultName;
    private static string? _apiKey;

    internal static bool Debug { private set; get; }

    private static void Main(string[] args)
    {
        ReadConfigFile();
        HandleParams(args);
        _prompt = _prompt.Replace("$NAME", _name);
        Debug = true;

        if (Debug) {
            Console.Error.WriteLine($"{nameof(_apiKey)}:      \"{_apiKey}\"");
            Console.Error.WriteLine($"{nameof(_maxTokens)}:   \"{_maxTokens}\"");
            Console.Error.WriteLine($"{nameof(_temperature)}: \"{_temperature}\"");
            Console.Error.WriteLine($"{nameof(_name)}:        \"{_name}\"");
            Console.Error.WriteLine($"{nameof(_prompt)}:      \"{_prompt}\"");
            Console.Error.WriteLine();
        }

        if (string.IsNullOrEmpty(_apiKey)) {
            ConsoleColor orig = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.Write("FATAL ERROR: ");
            Console.ForegroundColor = orig;
            Console.Error.Write("An API Key must be provided either in the configuration file or on the command line.\n");
            Environment.Exit(1);
        }

        var gpt = new GPT(_apiKey, _prompt, _name, _maxTokens, _temperature);

        for (;;) {
            var input = "";
            int ch;
            do {
                uint nread = 0;
                for (;;) {
                    ch = Console.Read();
                    switch (ch) {
                    case <= 0: return;
                    case '\r': continue;
                    case '\n': goto breakout;
                    }
                    input += (char)ch;
                    ++nread;
                }
            breakout:
                if (nread > 0 && input is [.., '\\']) {
                    input = input.Remove(input.Length - 1) + "\n";
                    ch    = '\\';
                }
            } while (ch == '\\' || input.Length == 0);

            if (Debug)
                Console.Error.WriteLine("Input: \"{0}\"", input);

            string response = gpt.Post(input);
            Console.WriteLine(response);
            Console.WriteLine();
        }
    }

    //----------------------------------------------------------------------------------

    private static void HandleParams(string[] args)
    {
        var optPrompt = new Option<string>(
            name: "--prompt",
            description: "The prompt to give OpenAI at the start and prepended (with other data) before each message.",
            getDefaultValue: () => _prompt);
        optPrompt.AddAlias("-p");

        var optName = new Option<string>(
            name: "--name",
            description: "The name OpenAI should adopt",
            getDefaultValue: () => _name);
        optName.AddAlias("-n");

        var optKey = new Option<string?>(
            name: "--key",
            description: "Your OpenAI key",
            getDefaultValue: () => _apiKey);
        optKey.AddAlias("-k");

        var optMaxTokens = new Option<uint>(
            name: "--maxtokens",
            description: "The maximum tokens OpenAI should send in one message (default 512).",
            getDefaultValue: () => _maxTokens);
        optMaxTokens.AddAlias("-M");

        var optTemp = new Option<double>(
            name: "--temperature",
            description: "The temperature setting for OpenAI.",
            getDefaultValue: () => _temperature);
        optTemp .AddAlias("-T");

        var optDebug = new Option<bool>(
            name: "--debug",
            description: "Debugging mode.",
            getDefaultValue: () => Debug)
        { IsHidden = true };

        var rootCommand = new RootCommand("This app eases one's ability to talk sexy to a pattern matching algorithm.") {
            optPrompt, optName, optKey, optMaxTokens, optTemp, optDebug
        };

        rootCommand.SetHandler(
            context =>
            {
                _prompt      = GetValueForHandlerParameter<string>(optPrompt, context)!;
                _name        = GetValueForHandlerParameter<string>(optName, context)!;
                _apiKey      = GetValueForHandlerParameter<string?>(optKey, context);
                _maxTokens   = GetValueForHandlerParameter<uint>(optMaxTokens, context);
                _temperature = GetValueForHandlerParameter<double>(optTemp, context);
                Debug        = GetValueForHandlerParameter<bool>(optDebug, context);

            });

        var    isHelp = false;
        Parser parser = new CommandLineBuilder(rootCommand).UseHelp(_ => isHelp = true).Build();
        parser.Invoke(args);
        if (isHelp)
            Environment.Exit(0);
    }

    private static T? GetValueForHandlerParameter<T>(IValueDescriptor symbol, InvocationContext context)
    {
        if (symbol is IValueSource valueSource &&
            valueSource.TryGetValue(symbol, context.BindingContext, out object? boundValue) &&
            boundValue is T value)
        {
            return value;
        }

        return symbol switch {
            Argument<T> argument => context.ParseResult.GetValueForArgument(argument),
            Option<T> option     => context.ParseResult.GetValueForOption(option),
            _                    => throw new ArgumentOutOfRangeException(nameof(symbol)),
        };
    }

    //----------------------------------------------------------------------------------

    [Serializable, UsedImplicitly(ImplicitUseTargetFlags.Members)]
    private struct ConfigData
    {
        public string? Prompt;
        public string? Name;
        public string? ApiKey;
        public uint?   MaxTokens;
        public double? Temperature;
        public bool?   Debug;
    }

    private static void ReadConfigFile()
    {
        string? path = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location);
        if (path == null)
            return;
        path = Path.Combine(path, "config.json");
        if (!File.Exists(path))
            return;

        string text = File.ReadAllText(path);
        var    data = Newtonsoft.Json.JsonConvert.DeserializeObject<ConfigData>(text);

        if (data.ApiKey != null)
            _apiKey = data.ApiKey;
        if (data.Name != null)
            _name = data.Name;
        if (data.Prompt  != null)
            _prompt = data.Prompt;
        if (data.MaxTokens  != null)
            _maxTokens = data.MaxTokens.Value;
        if (data.Temperature  != null)
            _temperature = data.Temperature.Value;
        if (data.Debug != null)
            Debug = data.Debug.Value;
    }
}