using System;
using JetBrains.Annotations;

#pragma warning disable CS8618
#pragma warning disable IDE1006
// ReSharper disable InconsistentNaming

namespace GPThing;

[Serializable]
public class JsonClass
{
    [Serializable, UsedImplicitly]
    public class Rootobject
    {
        public string   id      { get; set; }
        public string   _object { get; set; }
        public int      created { get; set; }
        public string   model   { get; set; }
        public Usage    usage   { get; set; }
        public Choice[] choices { get; set; }
    }

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

    [Serializable, UsedImplicitly]
    public class Message
    {
        public string role    { get; set; }
        public string content { get; set; }
    }
}
