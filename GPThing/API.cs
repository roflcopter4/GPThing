using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using JetBrains.Annotations;

#pragma warning disable CS8618
// ReSharper disable InconsistentNaming

namespace GPThing;

public static class API
{
    [PublicAPI]
    public enum RoleType { System, Assistant, User }

    static string RoleName(RoleType role)
    {
        return role switch {
            RoleType.System    => "system",
            RoleType.Assistant => "assistant",
            RoleType.User      => "user",
            _                  => throw new InvalidEnumArgumentException($"Invalid enumeration value: {(int)role}"),
        };
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public class Response
    {
        public string   id;
        public string   _object;
        public int      created;
        public string   model;
        public Usage    usage;
        public Choice[] choices;

        [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
        public class Usage
        {
            public int prompt_tokens;
            public int completion_tokens;
            public int total_tokens;
        }

        [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
        public class Choice
        {
            public Message message;
            public string  finish_reason;
            public int     index;
        }

        public static Response? Deserialize(string data)
        {
            var options = new JsonSerializerOptions {IncludeFields = true};
            return JsonSerializer.Deserialize<Response>(data, options);
        }
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public class Request
    {
        public Message[] messages;
        public string    model;
        public uint      max_tokens;
        public double    temperature;
        public double    frequency_penalty;
        public double    presence_penalty;
        //public double    top_p;

        public Request(
                IEnumerable<Message> messageList,
                string               modelType,
                uint                 maxTokens,
                double               temp,
                double               frequencyPenalty = 1.0,
                double               presencePenalty  = 1.0,
                double               topP             = 1.0)
        {
            if (messageList is null)
                throw new ArgumentNullException(nameof(messageList));
            var messageArray = messageList.ToArray();

            messages          = messageArray;
            model             = modelType;
            max_tokens        = maxTokens;
            temperature       = temp;
            frequency_penalty = frequencyPenalty;
            presence_penalty  = presencePenalty;
            //top_p             = topP;
        }

        public string Serialize()
        {
            var options = new JsonSerializerOptions {WriteIndented = Program.Debug, IncludeFields = true};
            return JsonSerializer.Serialize(this, options);
        }
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public class Message
    {
        public string role;
        public string content;

        public Message() {}
        public Message(RoleType role, string content)
        {
            this.role    = RoleName(role);
            this.content = content;
        }
    }
}
