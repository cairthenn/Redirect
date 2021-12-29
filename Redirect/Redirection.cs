using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Redirect
{
    [Serializable]
    public class Redirection
    {
        public uint ID { get; init; }
        public List<string> Priority { get; set; } = new();

        [JsonIgnore]
        public int Count => Priority!.Count;

        [JsonIgnore]
        public string this[int i] { 
            get { return Priority[i]; } 
            set { Priority[i] = value; } 
        }

        public void RemoveAt(int i) => Priority.RemoveAt(i);
        public void Add(string value) => Priority.Add(value);
    }
}
