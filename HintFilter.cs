using System.Collections.Generic;
using BH.SDK.Agent;
using Newtonsoft.Json.Linq;

namespace BH.MCP
{
    // Keyed by AgentHintKey: the game attaches a hint every time its trigger happens, the server decides
    // whether the AI has been told recently enough. Calls are counted per process, not per level.

    /// <summary> Drops a hint already sent within <see cref="AgentRules.HintRepeatAfterCalls"/> calls.
    /// </summary>
    public sealed class HintFilter
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, int> _sentAt = new Dictionary<string, int>();
        private int _call;

        /// <summary> Counts one tool call; the hints filtered next belong to it. </summary>
        public void BeginCall()
        {
            lock (_gate) _call++;
        }

        /// <summary> The texts of <paramref name="hints"/> that are due, remembering each as sent now.
        /// </summary>
        public List<string> Due(JArray hints)
        {
            var due = new List<string>();
            if (hints == null) return due;

            lock (_gate)
            {
                foreach (var hint in hints)
                {
                    var text = hint["text"]?.Value<string>();
                    if (string.IsNullOrEmpty(text)) continue;

                    var key = hint["key"]?.Value<string>();

                    if (key != null)
                    {
                        if (_sentAt.TryGetValue(key, out var at) && _call - at < AgentRules.HintRepeatAfterCalls) continue;
                        _sentAt[key] = _call;
                    }

                    due.Add(text);
                }
            }

            return due;
        }
    }
}
