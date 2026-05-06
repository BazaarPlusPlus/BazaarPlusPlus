using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus;

public partial class RunInfo
{
    public class SkillInfo
    {
        public ETier Tier;
        public Guid TemplateId;
        public string Name;
        public Dictionary<ECardAttributeType, int> Attributes { get; set; } =
            new Dictionary<ECardAttributeType, int>();
    }
}
