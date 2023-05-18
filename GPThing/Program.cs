using System;
using System.IO;
using System.Reflection;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using Newtonsoft.Json;

#pragma warning disable CS0649

namespace GPThing;

internal static partial class Program
{
    const double DefaultTemperature = 1.06;
    const uint   DefaultMaxTokens   = 256;
    const string DefaultGirlName    = "Emily";
    const string DefaultYourName    = "Jack";
    const string DefaultPrompt      = "Act like you are a human girl named $NAME.";
    const string DefaultSysPrompt   = "(This dialogue should be sexual in nature)\nWrite a brief response to this prompt from the perspective of {GirlName}.\n\n${MESSAGE}\n\n.";

    static readonly ConsoleColor OriginalColor = Console.ForegroundColor;

    static double  _temperature = DefaultTemperature;
    static uint    _maxTokens   = DefaultMaxTokens;
    static string  _prompt      = DefaultPrompt;
    static string  _girlName    = DefaultGirlName;
    static string  _yourName    = DefaultYourName;
    static string  _sysPrompt   = DefaultSysPrompt;
    static string? _apiKey;

    internal static bool Debug { private set; get; }

    static void Main(string[] args)
    {
        ReadConfigFile();
        HandleParams(args);

        _prompt    = ReplaceInPrompt(_prompt);
        _sysPrompt = ReplaceInPrompt(_sysPrompt);

        if (Debug) {
            Console.Error.WriteLine($"{nameof(_apiKey)}:      \"{_apiKey}\"");
            Console.Error.WriteLine($"{nameof(_maxTokens)}:   \"{_maxTokens}\"");
            Console.Error.WriteLine($"{nameof(_temperature)}: \"{_temperature}\"");
            Console.Error.WriteLine($"{nameof(_girlName)}:    \"{_girlName}\"");
            Console.Error.WriteLine($"{nameof(_yourName)}:    \"{_yourName}\"");
            Console.Error.WriteLine($"{nameof(_prompt)}:      \"{_prompt}\"");
            Console.Error.WriteLine($"{nameof(_sysPrompt)}:   \"{_sysPrompt}\"");
            Console.Error.WriteLine();
        }

        if (string.IsNullOrEmpty(_apiKey)) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.Write("FATAL ERROR: ");
            Console.ForegroundColor = OriginalColor;
            Console.Error.Write("An API Key must be provided either in the configuration file or on the command line.\n");
            Environment.Exit(1);
        }

        var gpt = new GPT(_apiKey, _prompt, _sysPrompt, _girlName, _yourName, _maxTokens, _temperature);

        for (;;) {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("User:");
            Console.ForegroundColor = OriginalColor;

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
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\nAI:");
            Console.ForegroundColor = OriginalColor;
            Console.WriteLine(response);
            Console.WriteLine();
        }
    }

    //----------------------------------------------------------------------------------

    static void HandleParams(string[] args)
    {
        var optPrompt = new Option<string>(
            name: "--prompt",
            description: "The prompt which is prepended (with other data) before each message and at the start if no Long Prompt is specified.",
            getDefaultValue: () => _prompt);
        optPrompt.AddAlias("-p");

        var optSysPrompt = new Option<string>(
            name: "--full-prompt",
            description: "The prompt to give OpenAI at the start of the conversation. You may wish to make it more detailed than the general prompt.",
            getDefaultValue: () => _sysPrompt);
        optPrompt.AddAlias("-P");

        var optGirlName = new Option<string>(
            name: "--name",
            description: "The name OpenAI should adopt.",
            getDefaultValue: () => _girlName);
        optGirlName.AddAlias("-n");

        var optYourName = new Option<string>(
            name: "--yourname",
            description: "The name OpenAI should use to refer to you.",
            getDefaultValue: () => _yourName);
        optYourName.AddAlias("-N");

        var optKey = new Option<string?>(
            name: "--key",
            description: "Your OpenAI key.",
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
            optPrompt, optSysPrompt, optGirlName, optYourName, optKey, optMaxTokens, optTemp, optDebug
        };

        rootCommand.SetHandler(
            context =>
            {
                _prompt      = GetValueForHandlerParameter<string>(optPrompt, context)!;
                _sysPrompt   = GetValueForHandlerParameter<string>(optSysPrompt, context)!;
                _girlName    = GetValueForHandlerParameter<string>(optGirlName, context)!;
                _yourName    = GetValueForHandlerParameter<string>(optYourName, context)!;
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

    static T? GetValueForHandlerParameter<T>(IValueDescriptor symbol, InvocationContext context)
    {
        if (symbol is IValueSource valueSource &&
            valueSource.TryGetValue(symbol, context.BindingContext, out object? boundValue) &&
            boundValue is T value)
            return value;

        return symbol switch {
            Argument<T> argument => context.ParseResult.GetValueForArgument(argument),
            Option<T> option     => context.ParseResult.GetValueForOption(option),
            _                    => throw new ArgumentOutOfRangeException(nameof(symbol)),
        };
    }

    //----------------------------------------------------------------------------------

    [Serializable, UsedImplicitly]
    class ConfigData
    {
        public string? ApiKey;
        public string? Prompt;
        public string? SysPrompt;
        public string? GirlName;
        public string? YourName;
        public uint?   MaxTokens;
        public double? Temperature;
        public bool?   Debug;
    }

    static void ReadConfigFile()
    {
        string? path = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location);
        if (path is null)
            return;
        path = Path.Combine(path, "config.json");

        if (!File.Exists(path))
            return;

        string text = File.ReadAllText(path);
        var    data = JsonConvert.DeserializeObject<ConfigData>(text);

        if (data is null)
            return;
        if (!string.IsNullOrWhiteSpace(data.ApiKey))
            _apiKey = data.ApiKey;
        if (!string.IsNullOrWhiteSpace(data.Prompt))
            _prompt = data.Prompt;
        if (!string.IsNullOrWhiteSpace(data.SysPrompt))
            _sysPrompt = data.SysPrompt;
        if (!string.IsNullOrWhiteSpace(data.GirlName))
            _girlName = data.GirlName;
        if (!string.IsNullOrWhiteSpace(data.YourName))
            _yourName = data.YourName;
        if (data.MaxTokens is not null)
            _maxTokens = data.MaxTokens.Value;
        if (data.Temperature is not null)
            _temperature = data.Temperature.Value;
        if (data.Debug is not null)
            Debug = data.Debug.Value;
    }

    static partial class LocalRegularExpressions
    {
        const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.Multiline;

        [GeneratedRegex("\\$\\{?[pP][rR][oO][mM][tT]\\}?", Options)]
        internal static partial Regex Prompt();

        [GeneratedRegex("\\$\\{?GirlName\\}?", Options)]
        internal static partial Regex GirlName();

        [GeneratedRegex("\\$\\{?YourName\\}?", Options)]
        internal static partial Regex YourName();
    }

    static string ReplaceInPrompt(string prompt)
    {
        prompt = LocalRegularExpressions.Prompt().Replace(prompt, _prompt);
        prompt = LocalRegularExpressions.GirlName().Replace(prompt, _girlName);
        prompt = LocalRegularExpressions.YourName().Replace(prompt, _yourName);

        return prompt;
    }
}