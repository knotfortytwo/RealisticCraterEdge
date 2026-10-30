using System.Collections.Generic;
using Nautilus.Json;
using Nautilus.Json.Attributes;

namespace RealisticCraterEdge
{
    [FileName("patrols")]
    internal sealed class PatrolSave : SaveDataCache
    {
        public Dictionary<int, float> Killed = new Dictionary<int, float>();
    }
}
