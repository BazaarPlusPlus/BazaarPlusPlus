using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus;

public partial class RunInfo
{
    public class CardInfo
    {
        public ETier Tier;
        public string Name;
        public Guid TemplateId;
        public EContainerSocketId? Left;
        public InstanceId Instance;
        public HashSet<ECardTag> Tags;
        public Dictionary<ECardAttributeType, int> Attributes { get; set; } =
            new Dictionary<ECardAttributeType, int>();
        public string Enchant;
    }
}
