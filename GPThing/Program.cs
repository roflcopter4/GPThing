using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using JetBrains.Annotations;

#pragma warning disable CS0649

namespace GPThing;

internal static partial class Program
{
    static readonly ConsoleColor OriginalColor = Console.ForegroundColor;
    static readonly string?      ExePath       = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location);

    static StreamWriter? _conversationLog;

    static double? _temperature;
    static double? _topP;
    static double? _presencePenalty;
    static double? _frequencyPenalty;
    static uint?   _maxTokens;
    static string? _prompt;
    static string? _girlName;
    static string? _yourName;
    static string? _sysPrompt;
    static string? _apiKey;
    static bool    _debugLog;

    internal static bool Debug { private set; get; }

    static void Main(string[] argv)
    {
        ReadConfigFile();
        HandleParams(argv);
        _conversationLog = GetConversationLogFilename();

        if (string.IsNullOrWhiteSpace(_apiKey)) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.Write("FATAL ERROR: ");
            Console.ForegroundColor = OriginalColor;
            Console.Error.Write("An API Key must be provided either in the configuration file or on the command line.\n");
            Environment.Exit(1);
        }

        var gpt = new GPT(_apiKey, _prompt, _sysPrompt, _girlName, _yourName)
        {
            DebugLog = (Debug || _debugLog) && ExePath is not null
                           ? new StreamWriter(Path.Combine(ExePath, "debug.log"), false)
                           : null,
        };
        if (_temperature      is not null) gpt.Temperature      = _temperature.Value;
        if (_topP             is not null) gpt.TopP             = _topP.Value;
        if (_maxTokens        is not null) gpt.MaxTokens        = _maxTokens.Value;
        if (_presencePenalty  is not null) gpt.PresencePenalty  = _presencePenalty.Value;
        if (_frequencyPenalty is not null) gpt.FrequencyPenalty = _frequencyPenalty.Value;

        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("System:");
        Console.ForegroundColor = OriginalColor;
        Console.Write(gpt.SysPrompt + "\n\n");

        for (;;)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("User:");
            Console.ForegroundColor = OriginalColor;

            string input = GetUserInput();

            if (input is ['!', _, ..]) {
                // System command. Modify parameters.
                ParseCommand.Parse(ref gpt, input[1..].Trim());
            } else {
                // Normal user message. Send as a request.
                string response = gpt.Post(input);

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("\nAI:\n");
                Console.ForegroundColor = OriginalColor;
                Console.Write(response + "\n\n");

                if (_conversationLog is not null) {
                    _conversationLog.Write($"***** USER *****\n{input}\n\n" +
                                           $"*****  AI  *****\n{response}\n\n");
                    _conversationLog.Flush();
                }
            }
        }
    }

    //----------------------------------------------------------------------------------

    /// <summary>
    /// Gets a line of user input, possibly containing escaped newlines. Blocks only for
    /// the first character. Exits upon EOF.
    /// </summary>
    static string GetUserInput()
    {
        var input = "";
        int ch;

        do {
            var nread = 0;
            for (;;) {
                ch = Console.Read();
                switch (ch) {
                case <= 0: Environment.Exit(0); break; // EOF
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

        return input;
    }

    static StreamWriter? GetConversationLogFilename()
    {
        if (string.IsNullOrEmpty(ExePath))
            return null;

        string logPath = Path.Combine(ExePath, "Logs");
        if (!Path.Exists(logPath))
            Directory.CreateDirectory(logPath);

        var path = "";

        for (uint i = 0; i < 99999; ++i) {
            path = Path.Combine(logPath, $"ConversationLog_{i:D5}.txt");
            if (!Path.Exists(path))
                break;
        }

        return Path.Exists(path) ? null : new StreamWriter(path, false);
    }

    static partial class ParseCommand
    {
        const RegexOptions Options = RegexOptions.IgnoreCase;

        [GeneratedRegex(@"(?:^set\s*)?\b(?:max(?:[_ ]?tokens)?)\b\s*=?\s*(?<val>\d+)", Options)]
        private static partial Regex SetMaxTokens();

        [GeneratedRegex(@"(?:^set\s*)?\b(?:temp(?:erature)?)\b\s*=?\s*(?<val>\d+)", Options)]
        private static partial Regex SetTemperature();

        [GeneratedRegex(@"\b^exit\b", Options)]
        private static partial Regex Exit();

        public static void Parse(ref GPT gpt, string command)
        {
            // ReSharper disable once JoinDeclarationAndInitializer
            Match m;

            m = SetMaxTokens().Match(command);
            if (m.Success) {
                string val = m.Groups["val"].Value;
                try {
                    uint number = uint.Parse(m.Groups["val"].Value);
                    if (number > 4096)
                        number = 4096;
                    gpt.MaxTokens = number;
                }
                catch (Exception e) when (e is FormatException or OverflowException) {
                    Console.Error.WriteLine($"Error parsing number \"{val}\"");
                }

                return;
            }

            m = SetTemperature().Match(command);
            if (m.Success) {
                string val = m.Groups["val"].Value;
                try {
                    double number = double.Parse(m.Groups["val"].Value);
                    gpt.Temperature = number switch {
                        > 2.0 => 2.0,
                        < 0.0 => 0.0,
                        _     => number,
                    };
                }
                catch (Exception e) when (e is FormatException or OverflowException) {
                    Console.Error.WriteLine($"Error parsing number \"{val}\"");
                }

                return;
            }

            if (Exit().IsMatch(command))
                Environment.Exit(0);

            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("error: ");
            Console.ForegroundColor = OriginalColor;
            Console.Write("unknown command\n\n");
        }
    }

    //----------------------------------------------------------------------------------

    static void HandleParams(string[] args)
    {
        var optPrompt = new Option<string?>(
            name: "--prompt",
            description: "The prompt which is prepended (with other data) before each message.",
            getDefaultValue: () => _prompt);
        optPrompt.AddAlias("-p");

        var optSysPrompt = new Option<string?>(
            name: "--sys-prompt",
            description: "The prompt to give OpenAI at the start of the conversation..",
            getDefaultValue: () => _sysPrompt);
        optPrompt.AddAlias("-P");

        var optGirlName = new Option<string?>(
            name: "--girl-name",
            description: "The name OpenAI should adopt.",
            getDefaultValue: () => _girlName);
        optGirlName.AddAlias("-n");

        var optUserName = new Option<string?>(
            name: "--your-name",
            description: "The name OpenAI should use to refer to you.",
            getDefaultValue: () => _yourName);
        optUserName.AddAlias("-N");

        var optKey = new Option<string?>(
            name: "--key",
            description: "Your OpenAI key.",
            getDefaultValue: () => _apiKey);
        optKey.AddAlias("-k");

        var optMaxTokens = new Option<uint?>(
            name: "--maxtokens",
            description: "The maximum tokens OpenAI should send in one message.",
            getDefaultValue: () => _maxTokens);
        optMaxTokens.AddAlias("-M");

        var optTemperature = new Option<double?>(
            name: "--temperature",
            description: "The temperature setting for OpenAI.",
            getDefaultValue: () => _temperature);
        optTemperature.AddAlias("-t");

        var optTopP = new Option<double?>(
            name: "--top-p",
            description: "The TopP setting for OpenAI.",
            getDefaultValue: () => _topP);
        optTopP.AddAlias("-T");

        var optPresencePenalty = new Option<double?>(
            name: "--presence-penalty",
            description: "The presence penalty setting for OpenAI.",
            getDefaultValue: () => _presencePenalty);

        var optFrequencyPenalty = new Option<double?>(
            name: "--frequency-penalty",
            description: "The frequency penalty setting for OpenAI.",
            getDefaultValue: () => _frequencyPenalty);

        var optDebug = new Option<bool>(
            name: "--debug",
            description: "Debugging mode.",
            getDefaultValue: () => Debug);

        var optDebugLog = new Option<bool>(
            name: "--debug-log",
            description: "Make a debugging log without enabling full debug-mode.",
            getDefaultValue: () => _debugLog);
        optDebugLog.AddAlias("-D");

        var rootCommand = new RootCommand(
            "This app eases one's ability to talk sexy to a pattern matching algorithm.") {
            optPrompt, optSysPrompt, optGirlName, optUserName, optKey, optMaxTokens,
            optTemperature, optTopP, optPresencePenalty, optFrequencyPenalty,
            optDebug, optDebugLog,
        };

        rootCommand.SetHandler(
            context =>
            {
                _prompt           = GetValueForHandlerParameter<string?>(optPrompt, context)!;
                _sysPrompt        = GetValueForHandlerParameter<string?>(optSysPrompt, context)!;
                _girlName         = GetValueForHandlerParameter<string?>(optGirlName, context)!;
                _yourName         = GetValueForHandlerParameter<string?>(optUserName, context)!;
                _apiKey           = GetValueForHandlerParameter<string?>(optKey, context);
                _maxTokens        = GetValueForHandlerParameter<uint?>(optMaxTokens, context);
                _temperature      = GetValueForHandlerParameter<double?>(optTemperature, context);
                _topP             = GetValueForHandlerParameter<double?>(optTopP, context);
                _frequencyPenalty = GetValueForHandlerParameter<double?>(optFrequencyPenalty, context);
                _presencePenalty  = GetValueForHandlerParameter<double?>(optPresencePenalty, context);
                Debug             = GetValueForHandlerParameter<bool>(optDebug, context);
                _debugLog         = GetValueForHandlerParameter<bool>(optDebugLog, context);
            });

        var    isHelp = false;
        Parser parser = new CommandLineBuilder(rootCommand).UseHelp(_ => isHelp = true).Build();
        parser.Invoke(args);
        if (isHelp)
            Environment.Exit(0);
    }

    static TP? GetValueForHandlerParameter<TP>(IValueDescriptor symbol, InvocationContext context)
    {
        if (symbol is IValueSource valueSource &&
            valueSource.TryGetValue(symbol, context.BindingContext, out object? boundValue) &&
            boundValue is TP value)
        {
            return value;
        }

        return symbol switch {
            Argument<TP> argument => context.ParseResult.GetValueForArgument(argument),
            Option<TP> option     => context.ParseResult.GetValueForOption(option),
            _                     => throw new ArgumentOutOfRangeException(nameof(symbol)),
        };
    }

    //----------------------------------------------------------------------------------

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    class ConfigData
    {
        public string? ApiKey;
        public string? Prompt;
        public string? SysPrompt;
        public string? GirlName;
        public string? UserName;
        public uint?   MaxTokens;
        public double? Temperature;
        public double? PresencePenalty;
        public double? FrequencyPenalty;
        public bool?   Debug;
    }

    static void ReadConfigFile()
    {
        if (ExePath is null)
            return;
        string path = Path.Combine(ExePath, "config.json");
        if (!File.Exists(path))
            return;

        string text = File.ReadAllText(path);
        var options = new JsonSerializerOptions {IncludeFields = true};
        var data    = JsonSerializer.Deserialize<ConfigData>(text, options);

        if (data is null)
            return;
        if (!string.IsNullOrWhiteSpace(data.ApiKey))
            _apiKey = data.ApiKey;
        if (data.Debug is not null)
            Debug = data.Debug.Value;

        _prompt           = data.Prompt;
        _sysPrompt        = data.SysPrompt;
        _girlName         = data.GirlName;
        _yourName         = data.UserName;
        _maxTokens        = data.MaxTokens;
        _temperature      = data.Temperature;
        _presencePenalty  = data.PresencePenalty;
        _frequencyPenalty = data.FrequencyPenalty;
    }
}