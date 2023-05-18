using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using JetBrains.Annotations;
using Newtonsoft.Json;

#pragma warning disable CS8618
#pragma warning disable IDE1006
// ReSharper disable MemberHidesStaticFromOuterClass
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

    [Serializable, UsedImplicitly]
    public class Response
    {
        public string   id      { get; set; }
        public string   _object { get; set; }
        public int      created { get; set; }
        public string   model   { get; set; }
        public Usage    usage   { get; set; }
        public Choice[] choices { get; set; }

        [Serializable, UsedImplicitly]
        public class Usage
        {
            public int prompt_tokens     { get; set; }
            public int completion_tokens { get; set; }
            public int total_tokens      { get; set; }
        }

        [Serializable, UsedImplicitly]
        public class Choice
        {
            public Message message       { get; set; }
            public string  finish_reason { get; set; }
            public int     index         { get; set; }
        }

        public static Response? Deserialize(string data)
        {
            return JsonConvert.DeserializeObject<Response>(data);
        }
    }

    [Serializable, UsedImplicitly]
    public class Request
    {
        public Message[] messages;
        public string    model;
        public uint      max_tokens;
        public double    temperature;
        public double    frequency_penalty;
        public double    presence_penalty;
        public int       top_p;

        public Request(
                IEnumerable<Message> messageList,
                string               modelType,
                uint                 maxTokens,
                double               temp,
                double               frequencyPenalty = 1.0,
                double               presencePenalty  = 1.0,
                int                  topP             = 1)
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
            top_p             = topP;
        }

        public string Serialize()
        {
            Formatting fmt = Program.Debug ? Formatting.Indented : Formatting.None;
            return JsonConvert.SerializeObject(this, fmt);
        }
    }

    [Serializable, UsedImplicitly]
    public class Message
    {
        public string role    { get; set; }
        public string content { get; set; }

        public Message() {}
        public Message(RoleType role, string content)
        {
            this.role    = RoleName(role);
            this.content = content;
        }
    }
}
