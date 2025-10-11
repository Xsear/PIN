using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GameServer.Physics.ZoneLoader;

public class ENWFData
{
    public class ENWFLayer : ITagfileExternalStorage
    {
        public ulong Id;
        public uint NumPhysicsMatIds;
        public uint[] PhysicsMatIds;
        public uint NumVertBlocks;
        public uint NumIndiceBlocks;
        public uint NumMatItems;
        public uint NumMoppBlocks;

        public VertBlockContent[] VertBlocks { get; set; }
        public IndiceBlockContent[] IndiceBlocks { get; set; }

        [JsonConverter(typeof(TagfileObjectDictionaryConverter))]
        public Dictionary<string, BaseTagfileObject> TagfileObjects { get; set; }

        public BaseTagfileObject GetTagfileObject(string query)
        {
            TagfileObjects.TryGetValue(query, out BaseTagfileObject result);

            if (result != null)
            {
                return result;
            }

            Console.WriteLine($"Failed to find TagfileObject with query {query}");
            return null;
        }
    }
}