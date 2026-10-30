using System.Collections.Generic;
using Nautilus.Json;
using Nautilus.Json.Attributes;

namespace RealisticCraterEdge
{
    [FileName("deposits")]
    internal sealed class DepositSave : SaveDataCache
    {
        public Dictionary<string, float[]> Health = new Dictionary<string, float[]>();
    }
}
