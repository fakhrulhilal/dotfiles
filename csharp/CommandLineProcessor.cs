using System;
using System.Collections.Generic;
using System.Linq;
using Iontas.KnownTypes.Classes;

namespace Iontas.DataLayer
{
    public class CommandLineProcessor
    {
        private readonly List<TokenDefinition> _tokenDefinitions = new List<TokenDefinition>
        {
            // switch parameter
            new TokenDefinition
            {
                Opening = '%',
                Closing = ' ',
                Factory = word => new TriggerCommandSwitchParameter(word)
            },
            // variable parameter
            new TokenDefinition
            {
                Wrapper = '"',
                Opening = '<',
                Closing = '>',
                Factory = word => new TriggerCommandVariableParameter(default(int), string.Empty, word)
            },
            // hotkey parameter
            new TokenDefinition
            {
                Opening = '{',
                Closing = '}',
                Factory = word => new TriggerCommandHotKeyParameter(default(int), string.Empty, word)
            },
            // literal text
            new TokenDefinition
            {
                Opening = '"',
                Closing = '"',
                Factory = word => new TriggerCommandLiteralParameter(word)
            }
        };

        public List<TriggerCommandParameter> Parse(string cmdLine)
        {
            if (string.IsNullOrWhiteSpace(cmdLine)) return new List<TriggerCommandParameter>(0);
            var mapping = _tokenDefinitions.ToDictionary(t => t.Opening, t => t);
            int startingTokenPosition = 0, sequence = 1;
            var output = new List<TriggerCommandParameter>();
            char wrapperCharacter = default(char);
            bool insideWrapper = false;
            var pendingOutput = new List<TriggerCommandParameter>();
            TokenDefinition activeToken = null, wrappingToken = null;
            for (int i = 0; i < cmdLine.Length; i++)
            {
                char currentChar = cmdLine[i];
                bool doesMeetAnotherToken = activeToken != null && mapping.ContainsKey(currentChar);
                bool isEmptyToken = i == startingTokenPosition + 1;
                wrapperCharacter = _tokenDefinitions.FirstOrDefault(t => t.Wrapper == currentChar)?.Wrapper ?? wrapperCharacter;
                if (i > startingTokenPosition && doesMeetAnotherToken)
                {
                    if (!isEmptyToken)
                    {
                        string token = cmdLine.Substring(startingTokenPosition, i - startingTokenPosition);
                        token += activeToken.Closing;
                        output.Add(BuildParameter(token, sequence++, activeToken));
                    }
                    startingTokenPosition = i + 1;
                    insideWrapper = mapping[currentChar].Wrapper != default(char) &&
                                    mapping[currentChar].Wrapper == wrapperCharacter;
                    if (insideWrapper) wrappingToken = activeToken;
                    activeToken = mapping[currentChar];
                }

                bool doesMeetStartingToken = activeToken == null && mapping.ContainsKey(currentChar);
                if (doesMeetStartingToken)
                    activeToken = mapping[currentChar];
                bool doesMeetEndingWrapper = wrapperCharacter != default(char) && currentChar == wrapperCharacter;
                if (insideWrapper && doesMeetEndingWrapper)
                {
                    if (pendingOutput.Count > 0)
                    {
                        output.AddRange(pendingOutput);
                        pendingOutput.Clear();
                    }

                    insideWrapper = false;
                    activeToken = wrappingToken;
                    wrapperCharacter = default(char);
                }
                bool doesMeetClosingToken = activeToken != null && currentChar == activeToken.Closing;
                if (i > startingTokenPosition && doesMeetClosingToken)
                {
                    string token = cmdLine.Substring(startingTokenPosition, i - startingTokenPosition + 1);
                    var parameter = BuildParameter(token, sequence++, activeToken);
                    bool hasToBeWrapped = activeToken.Wrapper != default(char);
                    if (hasToBeWrapped && insideWrapper)
                        pendingOutput.Add(parameter);
                    else if (!hasToBeWrapped)
                        output.Add(parameter);
                    startingTokenPosition = i + 1;
                    activeToken = null;
                }
            }

            if (startingTokenPosition == 0 && activeToken != null)
                output.Add(BuildParameter(cmdLine, sequence, activeToken));

            output = output.OrderBy(p => p.Sequence).ToList();
            sequence = 1;
            output.ForEach(p => p.Sequence = sequence++);
            return output;
        }

        private TriggerCommandParameter BuildParameter(string token, int sequence, TokenDefinition definition)
        {
            var parameter = definition.Factory(token);
            parameter.Sequence = sequence;
            return parameter;
        }

        class TokenDefinition
        {
            public char Wrapper { get; set; }
            public char Opening { get; set; }
            public char Closing { get; set; }
            public Func<string, TriggerCommandParameter> Factory { get; set; }

            public override string ToString() => $"{Opening}TOKEN{Closing}";
        }
    }
}
