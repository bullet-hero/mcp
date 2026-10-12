using System;
using System.Text.Json;

namespace BH.MCP
{
    /// <summary> What a tool or prompt argument the MCP binder could not read says to the AI. </summary>
    public static class ArgumentErrors
    {
        /// <summary> The message for <paramref name="error"/> when it is a bad or missing argument, else null.
        /// </summary>
        public static string Describe(Exception error)
        {
            for (var e = error; e != null; e = e.InnerException)
            {
                switch (e)
                {
                    case JsonException json:
                        var at = string.IsNullOrEmpty(json.Path) || json.Path == "$" ? string.Empty : $" at {json.Path}";
                        return $"an argument has the wrong type{at} ({FirstSentence(json.Message)}); " +
                               "the tool's input schema gives each argument's type.";
                    case ArgumentException argument when !string.IsNullOrEmpty(argument.ParamName):
                        var suffix = $" (Parameter '{argument.ParamName}')";
                        var text = argument.Message.EndsWith(suffix, StringComparison.Ordinal)
                            ? argument.Message.Substring(0, argument.Message.Length - suffix.Length)
                            : argument.Message;
                        return $"{text.TrimEnd('.')}; the input schema lists the required arguments.";
                }
            }

            return null;
        }

        private static string FirstSentence(string message)
        {
            var end = message.IndexOf(". ", StringComparison.Ordinal);
            return end < 0 ? message.TrimEnd('.') : message.Substring(0, end);
        }
    }
}
