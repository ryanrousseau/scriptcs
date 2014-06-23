using System;
using System.Linq;
using System.Reflection;
using PowerArgs;
using ScriptCs.Contracts;

namespace ScriptCs.Argument
{
    public class ArgumentHandler : IArgumentHandler
    {
        private readonly IArgumentParser _argumentParser;
        private readonly IConfigFileParser _configFileParser;
        private readonly IFileSystem _fileSystem;

        public ArgumentHandler(IArgumentParser argumentParser, IConfigFileParser configFileParser, IFileSystem fileSystem)
        {
            Guard.AgainstNullArgument("argumentParser", argumentParser);
            Guard.AgainstNullArgument("configFileParser", configFileParser);
            Guard.AgainstNullArgument("fileSystem", fileSystem);

            _fileSystem = fileSystem;
            _argumentParser = argumentParser;
            _configFileParser = configFileParser;
        }

        public ArgumentParseResult Parse(string[] args)
        {
            var sr = SplitScriptArgs(args);

            var commandArgs = _argumentParser.Parse(sr.CommandArguments);
            var localConfigFile = commandArgs != null ? commandArgs.Config : "scriptcs.opts";
            var localConfigPath = string.Format("{0}\\{1}", _fileSystem.CurrentDirectory, localConfigFile);
            var configArgs = _configFileParser.Parse(GetFileContent(localConfigPath));
            var globalConfigArgs = _configFileParser.Parse(GetFileContent(_fileSystem.GlobalConfigFile));

            var finalArguments = ReconcileArguments(globalConfigArgs ?? new ScriptCsArgs(), configArgs, sr);
            finalArguments = ReconcileArguments(finalArguments, commandArgs, sr);

            return new ArgumentParseResult(args, finalArguments, sr.ScriptArguments);
        }

        private string GetFileContent(string filePath)
        {
            if (_fileSystem.FileExists(filePath))
            {
                return _fileSystem.ReadFile(filePath);
            }

            return null;
        }

        public static SplitResult SplitScriptArgs(string[] args)
        {
            Guard.AgainstNullArgument("args", args);

            var result = new SplitResult() { CommandArguments = args };

            // Split the arguments list on "--".
            // The arguments before the "--" (or all arguments if there is no "--") are
            // for ScriptCs.exe, and the arguments after that are for the csx script.
            int separatorLocation = Array.IndexOf(args, "--");
            int scriptArgsCount = separatorLocation == -1 ? 0 : args.Length - separatorLocation - 1;
            result.ScriptArguments = new string[scriptArgsCount];
            Array.Copy(args, separatorLocation + 1, result.ScriptArguments, 0, scriptArgsCount);

            if (separatorLocation != -1)
                result.CommandArguments = args.Take(separatorLocation).ToArray();

            return result;
        }

        private static ScriptCsArgs ReconcileArguments(ScriptCsArgs baseArgs, ScriptCsArgs overrideArgs, SplitResult splitResult)
        {
            if (baseArgs == null)
                return overrideArgs;

            if (overrideArgs == null)
                return baseArgs;

            foreach (var property in typeof(ScriptCsArgs).GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                var currentValue = property.GetValue(baseArgs);
                var overrideValue = property.GetValue(overrideArgs);
                var defaultValue = GetPropertyDefaultValue(property);

                if (!object.Equals(currentValue, overrideValue))
                {
                    if (!object.Equals(overrideValue, defaultValue))
                    {
                        property.SetValue(baseArgs, overrideValue);
                    }
                    else
                    {
                        if (IsCommandLinePresent(splitResult.CommandArguments, property))
                            property.SetValue(baseArgs, overrideValue);
                    }
                }
            }

            return baseArgs;
        }

        private static bool IsCommandLinePresent(string[] args, PropertyInfo property)
        {
            bool attributeFound = false;

            var attribute = property.GetCustomAttribute<ArgShortcut>();

            if (attribute != null)
                attributeFound = args.Any(a => a.Contains("-" + (attribute as ArgShortcut).Shortcut));

            var result = args.Any(a => a.Contains("-" + property.Name)) || attributeFound;
            return result;
        }

        private static object GetPropertyDefaultValue(PropertyInfo property)
        {
            var defaultAttribute = property.GetCustomAttribute<DefaultValueAttribute>();

            return defaultAttribute != null
                       ? ((PowerArgs.DefaultValueAttribute)defaultAttribute).Value
                       : property.PropertyType.IsValueType ? Activator.CreateInstance(property.PropertyType) : null;
        }

        public class SplitResult
        {
            public SplitResult()
            {
                CommandArguments = new string[0];
                ScriptArguments = new string[0];
            }

            public string[] CommandArguments { get; set; }
            public string[] ScriptArguments { get; set; }
        }
    }
}